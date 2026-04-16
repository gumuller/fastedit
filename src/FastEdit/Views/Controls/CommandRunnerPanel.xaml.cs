using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastEdit.Services;

namespace FastEdit.Views.Controls;

public partial class CommandRunnerPanel : UserControl
{
    private readonly CommandRunnerService _runner = new();
    private const int MaxOutputLength = 500_000;

    public CommandRunnerPanel()
    {
        InitializeComponent();

        _runner.OutputReceived += text => Dispatcher.Invoke(() => AppendOutput(text));
        _runner.CommandStarted += () => Dispatcher.Invoke(() => StopButton.IsEnabled = true);
        _runner.CommandCompleted += () => Dispatcher.Invoke(() =>
        {
            StopButton.IsEnabled = false;
            InputBox.Focus();
        });
        _runner.WorkingDirectoryChanged += cwd => Dispatcher.Invoke(() => UpdatePrompt(cwd));

        UpdatePrompt(_runner.WorkingDirectory);
        Loaded += (s, e) => _runner.StartShell();
        Unloaded += (s, e) => _runner.Dispose();
    }

    public void SetWorkingDirectory(string? directory)
    {
        if (_runner.SetWorkingDirectory(directory))
        {
            UpdatePrompt(_runner.WorkingDirectory);
        }
    }

    public void FocusInput()
    {
        InputBox.Focus();
    }

    public void EnsureStarted()
    {
        _runner.StartShell();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var command = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(command)) return;

            InputBox.Clear();
            _runner.ExecuteCommand(command);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            var prev = _runner.GetPreviousHistoryItem();
            if (prev != null)
            {
                InputBox.Text = prev;
                InputBox.CaretIndex = InputBox.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            var next = _runner.GetNextHistoryItem();
            if (next != null)
            {
                InputBox.Text = next;
                InputBox.CaretIndex = InputBox.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (string.IsNullOrEmpty(InputBox.SelectedText) && _runner.IsBusy)
            {
                _runner.StopCurrentProcess();
                e.Handled = true;
            }
        }
    }

    private void AppendOutput(string text)
    {
        OutputBox.AppendText(text);

        // Cap scrollback to prevent memory issues
        if (OutputBox.Text.Length > MaxOutputLength)
        {
            var excess = OutputBox.Text.Length - MaxOutputLength;
            var newlineIdx = OutputBox.Text.IndexOf('\n', excess);
            if (newlineIdx > 0)
            {
                OutputBox.Text = OutputBox.Text[(newlineIdx + 1)..];
            }
        }

        OutputScroll.ScrollToEnd();
    }

    private void UpdatePrompt(string cwd)
    {
        // Shorten path like VSCode: show last 2 segments
        try
        {
            var parts = cwd.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var shortPath = parts.Length <= 2
                ? cwd
                : string.Join(Path.DirectorySeparatorChar.ToString(), parts[^2..]);
            PromptLabel.Text = $"{shortPath} ❯ ";
        }
        catch
        {
            PromptLabel.Text = "❯ ";
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        OutputBox.Clear();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _runner.StopCurrentProcess();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }
}
