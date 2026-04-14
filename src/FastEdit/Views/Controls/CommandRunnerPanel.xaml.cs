using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FastEdit.Views.Controls;

public partial class CommandRunnerPanel : UserControl
{
    private Process? _currentProcess;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private string? _workingDirectory;

    public CommandRunnerPanel()
    {
        InitializeComponent();
        _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public void SetWorkingDirectory(string? directory)
    {
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            _workingDirectory = directory;
            AppendOutput($"[Working directory: {directory}]\n");
        }
    }

    public void FocusInput()
    {
        InputBox.Focus();
    }

    private async void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var command = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(command)) return;

            _commandHistory.Add(command);
            _historyIndex = _commandHistory.Count;
            InputBox.Clear();

            AppendOutput($"❯ {command}\n");

            // Handle built-in cd command
            if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
            {
                HandleCd(command[3..].Trim());
                return;
            }

            await RunCommandAsync(command);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
                InputBox.Text = _commandHistory[_historyIndex];
                InputBox.CaretIndex = InputBox.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_historyIndex < _commandHistory.Count - 1)
            {
                _historyIndex++;
                InputBox.Text = _commandHistory[_historyIndex];
                InputBox.CaretIndex = InputBox.Text.Length;
            }
            else
            {
                _historyIndex = _commandHistory.Count;
                InputBox.Clear();
            }
            e.Handled = true;
        }
    }

    private void HandleCd(string path)
    {
        try
        {
            var newPath = Path.GetFullPath(Path.Combine(_workingDirectory ?? ".", path));
            if (Directory.Exists(newPath))
            {
                _workingDirectory = newPath;
                AppendOutput($"[Changed to: {newPath}]\n");
            }
            else
            {
                AppendOutput($"Directory not found: {path}\n");
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Error: {ex.Message}\n");
        }
    }

    private async Task RunCommandAsync(string command)
    {
        StopButton.IsEnabled = true;

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
                WorkingDirectory = _workingDirectory ?? "."
            };

            _currentProcess = Process.Start(psi);
            if (_currentProcess == null)
            {
                AppendOutput("Failed to start process.\n");
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
                AppendOutput("[Process timed out]\n");
                try { _currentProcess.Kill(); } catch { }
            }

            if (_currentProcess.ExitCode != 0)
            {
                AppendOutput($"[Exit code: {_currentProcess.ExitCode}]\n");
            }
        }
        catch (Exception ex)
        {
            AppendOutput($"Error: {ex.Message}\n");
        }
        finally
        {
            _currentProcess = null;
            StopButton.IsEnabled = false;
        }
    }

    private async Task ReadStreamAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        int bytesRead;
        while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            var text = StripAnsiCodes(new string(buffer, 0, bytesRead));
            Dispatcher.Invoke(() => AppendOutput(text));
        }
    }

    private static string StripAnsiCodes(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\x1B\[[0-9;]*[a-zA-Z]", "");
    }

    private void AppendOutput(string text)
    {
        OutputBox.AppendText(text);
        OutputScroll.ScrollToEnd();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        OutputBox.Clear();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProcess != null && !_currentProcess.HasExited)
        {
            try { _currentProcess.Kill(entireProcessTree: true); } catch { }
            AppendOutput("\n[Process stopped]\n");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }
}
