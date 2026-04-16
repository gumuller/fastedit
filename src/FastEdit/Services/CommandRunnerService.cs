using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FastEdit.Services;

/// <summary>
/// Manages a persistent PowerShell session with sentinel-based command completion detection.
/// </summary>
public class CommandRunnerService : IDisposable
{
    private Process? _shellProcess;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string _workingDirectory;
    private bool _disposed;
    private bool _isReady = true;
    private int _commandId;
    private readonly StringBuilder _outputBuffer = new();
    private System.Timers.Timer? _flushTimer;

    private const string SentinelPrefix = "##FASTEDIT_SENTINEL##";

    public event Action<string>? OutputReceived;
    public event Action<string>? WorkingDirectoryChanged;
    public event Action? CommandStarted;
    public event Action? CommandCompleted;

    public string WorkingDirectory => _workingDirectory;
    public IReadOnlyList<string> History => _commandHistory;
    public bool IsRunning
    {
        get
        {
            try { return _shellProcess != null && !_shellProcess.HasExited; }
            catch { return false; }
        }
    }
    public bool IsBusy => !_isReady;

    public CommandRunnerService()
    {
        _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void StartShell(string? initialDirectory = null)
    {
        if (_shellProcess != null && !_shellProcess.HasExited) return;

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            _workingDirectory = initialDirectory;

        var shellPath = FindPowerShell();

        var psi = new ProcessStartInfo
        {
            FileName = shellPath,
            Arguments = "-NoProfile -NoLogo -NonInteractive -Command -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _workingDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // Force UTF-8 output
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        _shellProcess = Process.Start(psi);
        if (_shellProcess == null)
        {
            OutputReceived?.Invoke("Failed to start shell.\r\n");
            return;
        }

        // Start reading output/error streams
        Task.Run(() => ReadOutputStream());
        Task.Run(() => ReadErrorStream());

        // Set up output batching timer (flush every 50ms)
        _flushTimer = new System.Timers.Timer(50);
        _flushTimer.Elapsed += (s, e) => FlushOutputBuffer();
        _flushTimer.AutoReset = true;
        _flushTimer.Start();

        // Initialize shell: set UTF-8, suppress progress, set location
        SendRawCommand("$OutputEncoding = [System.Text.Encoding]::UTF8");
        SendRawCommand("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8");
        SendRawCommand("$ProgressPreference = 'SilentlyContinue'");
        SendRawCommand($"Set-Location '{EscapePath(_workingDirectory)}'");

        // Emit initial sentinel to mark shell as ready
        _commandId++;
        SendRawCommand($"Write-Output '{SentinelPrefix}{_commandId}|' ; Write-Output ((Get-Location).Path)");
    }

    public void ExecuteCommand(string command)
    {
        if (_shellProcess == null || _shellProcess.HasExited)
        {
            StartShell();
        }

        if (string.IsNullOrWhiteSpace(command)) return;

        _commandHistory.Add(command);
        _historyIndex = _commandHistory.Count;

        _isReady = false;
        CommandStarted?.Invoke();
        OutputReceived?.Invoke($"\r\n❯ {command}\r\n");

        _commandId++;
        var id = _commandId;

        // Execute user command, then emit sentinel with cwd
        SendRawCommand(command);
        SendRawCommand($"Write-Output \"`n{SentinelPrefix}{id}|$((Get-Location).Path)\"");
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
        return string.Empty;
    }

    public void StopCurrentProcess()
    {
        // Kill the shell and restart it
        if (_shellProcess != null && !_shellProcess.HasExited)
        {
            try { _shellProcess.Kill(entireProcessTree: true); } catch { }
            _shellProcess = null;
        }
        _isReady = true;
        OutputReceived?.Invoke("\r\n[Process interrupted]\r\n");
        CommandCompleted?.Invoke();
        StartShell();
    }

    public bool SetWorkingDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;

        // If it's a file path, use its directory
        if (File.Exists(directory))
            directory = Path.GetDirectoryName(directory);

        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            _workingDirectory = directory;
            if (_shellProcess != null && !_shellProcess.HasExited)
            {
                SendRawCommand($"Set-Location '{EscapePath(directory)}'");
            }
            WorkingDirectoryChanged?.Invoke(directory);
            return true;
        }
        return false;
    }

    private void SendRawCommand(string text)
    {
        if (_shellProcess?.HasExited != false) return;
        try
        {
            _shellProcess.StandardInput.WriteLine(text);
            _shellProcess.StandardInput.Flush();
        }
        catch { }
    }

    private async Task ReadOutputStream()
    {
        if (_shellProcess == null) return;
        var reader = _shellProcess.StandardOutput;
        var buffer = new char[4096];

        try
        {
            int bytesRead;
            while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var text = new string(buffer, 0, bytesRead);
                ProcessOutput(text);
            }
        }
        catch { }
    }

    private async Task ReadErrorStream()
    {
        if (_shellProcess == null) return;
        var reader = _shellProcess.StandardError;
        var buffer = new char[4096];

        try
        {
            int bytesRead;
            while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var text = new string(buffer, 0, bytesRead);
                var cleaned = StripAnsiCodes(text);
                if (!string.IsNullOrEmpty(cleaned))
                {
                    lock (_outputBuffer)
                    {
                        _outputBuffer.Append(cleaned);
                    }
                }
            }
        }
        catch { }
    }

    private void ProcessOutput(string text)
    {
        var cleaned = StripAnsiCodes(text);
        var lines = cleaned.Split('\n');
        var filteredLines = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (line.Contains(SentinelPrefix))
            {
                // Parse sentinel: ##FASTEDIT_SENTINEL##<id>|<cwd>
                var idx = line.IndexOf(SentinelPrefix, StringComparison.Ordinal);
                var payload = line[(idx + SentinelPrefix.Length)..];
                var pipeIdx = payload.IndexOf('|');
                if (pipeIdx >= 0)
                {
                    var cwdPart = payload[(pipeIdx + 1)..].Trim();
                    if (!string.IsNullOrEmpty(cwdPart) && Directory.Exists(cwdPart))
                    {
                        _workingDirectory = cwdPart;
                        WorkingDirectoryChanged?.Invoke(cwdPart);
                    }
                }
                _isReady = true;
                CommandCompleted?.Invoke();
                continue; // Don't show sentinel in output
            }

            // Filter out internal setup commands
            if (line.Contains("$OutputEncoding") ||
                line.Contains("[Console]::OutputEncoding") ||
                line.Contains("$ProgressPreference") ||
                line.Contains("Write-Output") && line.Contains(SentinelPrefix))
                continue;

            filteredLines.Append(line);
            filteredLines.Append('\n');
        }

        var output = filteredLines.ToString();
        if (!string.IsNullOrEmpty(output))
        {
            lock (_outputBuffer)
            {
                _outputBuffer.Append(output);
            }
        }
    }

    private void FlushOutputBuffer()
    {
        string? text = null;
        lock (_outputBuffer)
        {
            if (_outputBuffer.Length > 0)
            {
                text = _outputBuffer.ToString();
                _outputBuffer.Clear();
            }
        }

        if (text != null)
        {
            OutputReceived?.Invoke(text);
        }
    }

    private static string FindPowerShell()
    {
        // Prefer pwsh (PowerShell 7+), fallback to Windows PowerShell
        var pwshPaths = new[]
        {
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Program Files (x86)\PowerShell\7\pwsh.exe"
        };

        foreach (var path in pwshPaths)
        {
            if (File.Exists(path)) return path;
        }

        // Check PATH
        try
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var dir in pathDirs)
            {
                var pwsh = Path.Combine(dir, "pwsh.exe");
                if (File.Exists(pwsh)) return pwsh;
            }
        }
        catch { }

        return "powershell.exe";
    }

    private static string EscapePath(string path) => path.Replace("'", "''");

    public static string StripAnsiCodes(string text)
    {
        return Regex.Replace(text, @"\x1B\[[0-9;]*[a-zA-Z]", "");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer?.Stop();
        _flushTimer?.Dispose();

        if (_shellProcess != null && !_shellProcess.HasExited)
        {
            try
            {
                _shellProcess.StandardInput.WriteLine("exit");
                if (!_shellProcess.WaitForExit(2000))
                    _shellProcess.Kill(entireProcessTree: true);
            }
            catch
            {
                try { _shellProcess.Kill(entireProcessTree: true); } catch { }
            }
        }

        _shellProcess?.Dispose();
        GC.SuppressFinalize(this);
    }
}
