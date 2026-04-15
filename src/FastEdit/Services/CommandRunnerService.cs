using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace FastEdit.Services;

/// <summary>
/// Manages command execution, history, and working directory for the command runner panel.
/// </summary>
public class CommandRunnerService : IDisposable
{
    private Process? _currentProcess;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _workingDirectory;
    private bool _disposed;

    public event Action<string>? OutputReceived;
    public event Action? CommandCompleted;

    public string WorkingDirectory => _workingDirectory;
    public IReadOnlyList<string> History => _commandHistory;
    public bool IsRunning => _currentProcess != null && !_currentProcess.HasExited;

    public CommandRunnerService()
    {
        _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public bool SetWorkingDirectory(string? directory)
    {
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            _workingDirectory = directory;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Processes a command string. Returns true if it was handled internally (e.g. cd).
    /// </summary>
    public async Task<bool> ExecuteCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        _commandHistory.Add(command);
        _historyIndex = _commandHistory.Count;

        OutputReceived?.Invoke($"❯ {command}\n");

        if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            HandleCd(command[3..].Trim());
            return true;
        }

        await RunProcessAsync(command);
        return true;
    }

    public string? GetPreviousHistoryItem()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            return _commandHistory[_historyIndex];
        }
        return null;
    }

    public string? GetNextHistoryItem()
    {
        if (_historyIndex < _commandHistory.Count - 1)
        {
            _historyIndex++;
            return _commandHistory[_historyIndex];
        }
        _historyIndex = _commandHistory.Count;
        return string.Empty; // Clear input
    }

    public void StopCurrentProcess()
    {
        if (_currentProcess != null && !_currentProcess.HasExited)
        {
            try { _currentProcess.Kill(entireProcessTree: true); } catch { }
            OutputReceived?.Invoke("\n[Process stopped]\n");
        }
    }

    private void HandleCd(string path)
    {
        try
        {
            var newPath = Path.GetFullPath(Path.Combine(_workingDirectory, path));
            if (Directory.Exists(newPath))
            {
                _workingDirectory = newPath;
                OutputReceived?.Invoke($"[Changed to: {newPath}]\n");
            }
            else
            {
                OutputReceived?.Invoke($"Directory not found: {path}\n");
            }
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke($"Error: {ex.Message}\n");
        }
    }

    private async Task RunProcessAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };

            _currentProcess = Process.Start(psi);
            if (_currentProcess == null)
            {
                OutputReceived?.Invoke("Failed to start process.\n");
                return;
            }

            var outputTask = ReadStreamAsync(_currentProcess.StandardOutput);
            var errorTask = ReadStreamAsync(_currentProcess.StandardError);

            await Task.WhenAll(outputTask, errorTask);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _currentProcess.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                OutputReceived?.Invoke("[Process timed out]\n");
                try { _currentProcess.Kill(); } catch { }
            }

            if (_currentProcess.ExitCode != 0)
            {
                OutputReceived?.Invoke($"[Exit code: {_currentProcess.ExitCode}]\n");
            }
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke($"Error: {ex.Message}\n");
        }
        finally
        {
            _currentProcess = null;
            CommandCompleted?.Invoke();
        }
    }

    private async Task ReadStreamAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        int bytesRead;
        while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var text = StripAnsiCodes(new string(buffer, 0, bytesRead));
            OutputReceived?.Invoke(text);
        }
    }

    public static string StripAnsiCodes(string text)
    {
        return Regex.Replace(text, @"\x1B\[[0-9;]*[a-zA-Z]", "");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCurrentProcess();
        GC.SuppressFinalize(this);
    }
}
