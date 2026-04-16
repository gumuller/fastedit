using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.Services;
using FastEdit.ViewModels;

namespace FastEdit.Views.Controls;

public partial class CommandRunnerPanel : UserControl
{
    private readonly CommandRunnerService _runner = new();
    private const int MaxParagraphs = 5000;

    private string _currentPrompt = "❯ ";
    private Run? _promptRun;
    private Run? _inputRun;

    public CommandRunnerPanel()
    {
        InitializeComponent();

        // Set up the FlowDocument with no spacing between paragraphs
        TerminalBox.Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
            FontSize = 12
        };

        // Override default paragraph style to remove spacing
        var paraStyle = new Style(typeof(Paragraph));
        paraStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        paraStyle.Setters.Add(new Setter(Block.PaddingProperty, new Thickness(0)));
        TerminalBox.Document.Resources.Add(typeof(Paragraph), paraStyle);

        _runner.OutputReceived += text => Dispatcher.Invoke(() => AppendOutput(text));
        _runner.CommandStarted += () => Dispatcher.Invoke(() => StopButton.IsEnabled = true);
        _runner.CommandCompleted += () => Dispatcher.Invoke(() =>
        {
            StopButton.IsEnabled = false;
            ShowPrompt();
        });
        _runner.WorkingDirectoryChanged += cwd => Dispatcher.Invoke(() => UpdatePrompt(cwd));

        UpdatePrompt(_runner.WorkingDirectory);
        Loaded += (s, e) =>
        {
            _runner.StartShell();
        };
        Unloaded += (s, e) => _runner.Dispose();

        // Intercept paste to strip newlines and enforce input area
        DataObject.AddPastingHandler(TerminalBox, OnPaste);

        // Block cut/undo/redo commands that could corrupt output
        TerminalBox.AddHandler(CommandManager.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler(OnPreviewCommandExecuted), true);
    }

    public void SetWorkingDirectory(string? directory)
    {
        if (_runner.SetWorkingDirectory(directory))
            UpdatePrompt(_runner.WorkingDirectory);
    }

    public void FocusInput()
    {
        TerminalBox.Focus();
        if (_inputRun != null)
            TerminalBox.CaretPosition = _inputRun.ContentEnd;
    }

    public void EnsureStarted()
    {
        _runner.StartShell();
    }

    private Brush GetPromptBrush()
    {
        return (FindResource("AccentBrush") as Brush) ?? Brushes.Cyan;
    }

    private Brush GetForegroundBrush()
    {
        return (FindResource("EditorForegroundBrush") as Brush) ?? Brushes.White;
    }

    private void ShowPrompt()
    {
        _promptRun = new Run(_currentPrompt) { Foreground = GetPromptBrush() };
        _inputRun = new Run("") { Foreground = GetForegroundBrush() };

        var para = new Paragraph { Margin = new Thickness(0) };
        para.Inlines.Add(_promptRun);
        para.Inlines.Add(_inputRun);

        TerminalBox.Document.Blocks.Add(para);
        TerminalBox.CaretPosition = _inputRun.ContentEnd;
        TerminalBox.Focus();
        TerminalBox.ScrollToEnd();
    }

    private string GetInputText()
    {
        if (_inputRun == null) return "";
        return new TextRange(_inputRun.ContentStart, _inputRun.ContentEnd).Text;
    }

    private void SetInputText(string text)
    {
        if (_inputRun == null) return;
        new TextRange(_inputRun.ContentStart, _inputRun.ContentEnd).Text = text;
        TerminalBox.CaretPosition = _inputRun.ContentEnd;
    }

    private bool IsCaretInInputRun()
    {
        if (_inputRun == null) return false;
        var caretPos = TerminalBox.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
        var start = _inputRun.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        var end = _inputRun.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        return caretPos.CompareTo(start) >= 0 && caretPos.CompareTo(end) <= 0;
    }

    private bool IsSelectionInInputRun()
    {
        if (_inputRun == null) return false;
        var start = _inputRun.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        var end = _inputRun.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        var selStart = TerminalBox.Selection.Start.GetInsertionPosition(LogicalDirection.Forward);
        var selEnd = TerminalBox.Selection.End.GetInsertionPosition(LogicalDirection.Backward);
        return selStart.CompareTo(start) >= 0 && selEnd.CompareTo(end) <= 0;
    }

    private bool IsCaretAtInputStart()
    {
        if (_inputRun == null) return true;
        var caretPos = TerminalBox.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
        var start = _inputRun.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        return caretPos.CompareTo(start) <= 0;
    }

    private void MoveCaretToInputEnd()
    {
        if (_inputRun != null)
            TerminalBox.CaretPosition = _inputRun.ContentEnd;
    }

    private void TerminalBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+C: copy or interrupt
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (TerminalBox.Selection.IsEmpty && _runner.IsBusy)
            {
                _runner.StopCurrentProcess();
                e.Handled = true;
            }
            return;
        }

        // Allow Ctrl+A (select all), Ctrl+V (paste handled separately)
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key != Key.V)
            return;

        // Ctrl+V: force caret into input area
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!IsCaretInInputRun()) MoveCaretToInputEnd();
            return;
        }

        // Enter: submit command
        if (e.Key == Key.Enter)
        {
            var input = GetInputText().Trim();
            if (!string.IsNullOrEmpty(input))
            {
                // Freeze the prompt line (no longer editable via _inputRun tracking)
                _inputRun = null;
                _promptRun = null;
                _runner.ExecuteCommand(input);
            }
            e.Handled = true;
            return;
        }

        // Up/Down: history
        if (e.Key == Key.Up)
        {
            var prev = _runner.GetPreviousHistoryItem();
            if (prev != null) SetInputText(prev);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Down)
        {
            var next = _runner.GetNextHistoryItem();
            if (next != null) SetInputText(next);
            e.Handled = true;
            return;
        }

        // Home: go to start of input
        if (e.Key == Key.Home)
        {
            if (_inputRun != null)
                TerminalBox.CaretPosition = _inputRun.ContentStart;
            e.Handled = true;
            return;
        }

        // Backspace: block at start of input or if selection spans outside
        if (e.Key == Key.Back)
        {
            if (!IsCaretInInputRun() || IsCaretAtInputStart() ||
                (!TerminalBox.Selection.IsEmpty && !IsSelectionInInputRun()))
            {
                e.Handled = true;
                return;
            }
        }

        // Delete: block outside input or if selection spans outside
        if (e.Key == Key.Delete)
        {
            if (!IsCaretInInputRun() ||
                (!TerminalBox.Selection.IsEmpty && !IsSelectionInInputRun()))
            {
                e.Handled = true;
                return;
            }
        }

        // Left: block at start of input
        if (e.Key == Key.Left)
        {
            if (IsCaretAtInputStart())
            {
                e.Handled = true;
                return;
            }
        }

        // Any other key: ensure caret is in input area
        if (!IsCaretInInputRun())
        {
            MoveCaretToInputEnd();
        }
    }

    private void TerminalBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!IsCaretInInputRun())
        {
            MoveCaretToInputEnd();
        }
    }

    private void TerminalBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            // Focus the control, then redirect caret to prompt after layout processes the click
            TerminalBox.Focus();
            Dispatcher.BeginInvoke(() => MoveCaretToInputEnd(),
                System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        // Strip newlines from pasted text and ensure it goes to input area
        if (!IsCaretInInputRun()) MoveCaretToInputEnd();

        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = (string)e.DataObject.GetData(DataFormats.UnicodeText);
            // Strip newlines — single-line shell input only
            text = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();

            // Replace the clipboard data with cleaned text
            var dataObj = new DataObject();
            dataObj.SetData(DataFormats.UnicodeText, text);
            e.DataObject = dataObj;
        }
    }

    private void OnPreviewCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        // Block Cut, Undo, Redo if selection spans outside input area
        if (e.Command == ApplicationCommands.Cut ||
            e.Command == ApplicationCommands.Undo ||
            e.Command == ApplicationCommands.Redo)
        {
            if (!IsSelectionInInputRun())
            {
                e.Handled = true;
            }
        }
    }

    private void AppendOutput(string text)
    {
        // Trim trailing blank lines
        text = text.TrimEnd('\r', '\n', ' ');
        if (string.IsNullOrEmpty(text)) return;

        var outputRun = new Run(text + "\n") { Foreground = GetForegroundBrush() };
        var para = new Paragraph { Margin = new Thickness(0) };
        para.Inlines.Add(outputRun);

        // Insert before the prompt paragraph if it exists
        var promptPara = _promptRun?.Parent as Paragraph;
        if (promptPara != null)
            TerminalBox.Document.Blocks.InsertBefore(promptPara, para);
        else
            TerminalBox.Document.Blocks.Add(para);

        // Cap scrollback
        while (TerminalBox.Document.Blocks.Count > MaxParagraphs)
            TerminalBox.Document.Blocks.Remove(TerminalBox.Document.Blocks.FirstBlock);

        TerminalBox.ScrollToEnd();
    }

    private void UpdatePrompt(string cwd)
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var displayPath = cwd;
            if (cwd.Equals(userProfile, StringComparison.OrdinalIgnoreCase))
                displayPath = "~";
            else if (cwd.StartsWith(userProfile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                displayPath = "~" + cwd[userProfile.Length..];
            _currentPrompt = $"{displayPath} ❯ ";
        }
        catch
        {
            _currentPrompt = "❯ ";
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        TerminalBox.Document.Blocks.Clear();
        ShowPrompt();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _runner.StopCurrentProcess();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsCommandRunnerVisible = false;
        else
            Visibility = Visibility.Collapsed;
    }
}
