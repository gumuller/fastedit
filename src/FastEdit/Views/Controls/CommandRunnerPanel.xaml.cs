using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastEdit.Services;

namespace FastEdit.Views.Controls;

public partial class CommandRunnerPanel : UserControl
{
    private readonly CommandRunnerService _runner = new();

    public CommandRunnerPanel()
    {
        InitializeComponent();
        _runner.OutputReceived += text => Dispatcher.Invoke(() => AppendOutput(text));
        _runner.CommandCompleted += () => Dispatcher.Invoke(() => StopButton.IsEnabled = false);
    }

    public void SetWorkingDirectory(string? directory)
    {
        if (_runner.SetWorkingDirectory(directory))
        {
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

            InputBox.Clear();
            StopButton.IsEnabled = true;

            await _runner.ExecuteCommandAsync(command);
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
        _runner.StopCurrentProcess();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
    }
}
