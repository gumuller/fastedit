using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.Infrastructure;
using FastEdit.Services;
using FastEdit.ViewModels;

namespace FastEdit.Views.Controls;

public class TerminalTabInfo : IDisposable
{
    public string Title { get; set; } = "";
    public CommandRunnerService Service { get; }
    public FlowDocument Document { get; }
    public Run? PromptRun { get; set; }
    public Run? InputRun { get; set; }
    public string CurrentPrompt { get; set; } = "❯ ";

    public TerminalTabInfo(string title)
    {
        Title = title;
        Service = new CommandRunnerService();
        Document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New"),
            FontSize = 12
        };

        var paraStyle = new Style(typeof(Paragraph));
        paraStyle.Setters.Add(new Setter(Block.MarginProperty, new Thickness(0)));
        paraStyle.Setters.Add(new Setter(Block.PaddingProperty, new Thickness(0)));
        Document.Resources.Add(typeof(Paragraph), paraStyle);
    }

    public void Dispose()
    {
        Service.Dispose();
    }
}

public partial class CommandRunnerPanel : UserControl
{
    private readonly List<TerminalTabInfo> _tabs = new();
    private TerminalTabInfo? _activeTab;
    private int _tabCounter;
    private const int MaxParagraphs = 5000;

    public CommandRunnerPanel()
    {
        InitializeComponent();

        AddNewTab();

        Loaded += (s, e) =>
        {
            _activeTab?.Service.StartShell();
        };
        Unloaded += (s, e) =>
        {
            foreach (var tab in _tabs)
                tab.Dispose();
        };

        DataObject.AddPastingHandler(TerminalBox, OnPaste);

        TerminalBox.AddHandler(CommandManager.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler(OnPreviewCommandExecuted), true);
    }

    private TerminalTabInfo AddNewTab()
    {
        _tabCounter++;
        var tab = new TerminalTabInfo($"Terminal {_tabCounter}");

        tab.Service.OutputReceived += text => Dispatcher.Invoke(() =>
        {
            if (_activeTab == tab)
                AppendOutput(text);
            else
                AppendOutputToTab(tab, text);
        });
        tab.Service.CommandStarted += () => Dispatcher.Invoke(() =>
        {
            if (_activeTab == tab) StopButton.IsEnabled = true;
        });
        tab.Service.CommandCompleted += () => Dispatcher.Invoke(() =>
        {
            if (_activeTab == tab)
            {
                StopButton.IsEnabled = false;
                ShowPrompt();
            }
            else
            {
                ShowPromptOnTab(tab);
            }
        });
        tab.Service.WorkingDirectoryChanged += cwd => Dispatcher.Invoke(() =>
        {
            UpdatePromptForTab(tab, cwd);
        });

        UpdatePromptForTab(tab, tab.Service.WorkingDirectory);
        _tabs.Add(tab);
        SwitchToTab(tab);
        RebuildTabStrip();
        return tab;
    }

    private void RemoveTab(TerminalTabInfo tab)
    {
        if (_tabs.Count <= 1) return;

        var idx = _tabs.IndexOf(tab);
        var wasActive = _activeTab == tab;
        _tabs.Remove(tab);
        tab.Dispose();

        if (TerminalTabClosePolicy.TryGetNextActiveIndex(_tabs.Count + 1, idx, wasActive, out var newIdx))
        {
            SwitchToTab(_tabs[newIdx]);
        }

        RebuildTabStrip();
    }

    private void SwitchToTab(TerminalTabInfo tab)
    {
        if (_activeTab != null)
        {
            _activeTab.PromptRun = _promptRun;
            _activeTab.InputRun = _inputRun;
        }

        _activeTab = tab;

        TerminalBox.Document = tab.Document;
        _promptRun = tab.PromptRun;
        _inputRun = tab.InputRun;

        StopButton.IsEnabled = tab.Service.IsBusy;

        RebuildTabStrip();
    }

    private void RebuildTabStrip()
    {
        TabStripPanel.Children.Clear();

        foreach (var tab in _tabs)
        {
            var isActive = tab == _activeTab;

            var titleBlock = new TextBlock
            {
                Text = tab.Title,
                Foreground = (FindResource("EditorForegroundBrush") as Brush) ?? Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            var closeBtn = new Button
            {
                Content = "✕",
                FontSize = 9,
                Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = (FindResource("EditorForegroundBrush") as Brush) ?? Brushes.White,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
                Visibility = _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
                Tag = tab
            };
            closeBtn.Click += TabClose_Click;

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(titleBlock);
            panel.Children.Add(closeBtn);

            var border = new Border
            {
                Child = panel,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(2, 2, 0, 0),
                CornerRadius = new CornerRadius(7, 7, 0, 0),
                SnapsToDevicePixels = true,
                Cursor = Cursors.Hand,
                Background = isActive
                    ? ((FindResource("EditorBackgroundBrush") as Brush) ?? Brushes.Black)
                    : Brushes.Transparent,
                BorderThickness = new Thickness(1, 1, 1, 0),
                BorderBrush = isActive
                    ? ((FindResource("TabBorderBrush") as Brush) ?? Brushes.DimGray)
                    : Brushes.Transparent,
                Tag = tab
            };
            border.MouseLeftButtonDown += TabBorder_Click;

            TabStripPanel.Children.Add(border);
        }

        var addBtn = new Button
        {
            Content = "+",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(6, 2, 6, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = (FindResource("EditorForegroundBrush") as Brush) ?? Brushes.White,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            ToolTip = "New Terminal"
        };
        addBtn.Click += (s, e) =>
        {
            var newTab = AddNewTab();
            newTab.Service.StartShell();
            ShowPrompt();
            FocusInput();
        };
        TabStripPanel.Children.Add(addBtn);
    }

    private void TabBorder_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is TerminalTabInfo tab)
        {
            SwitchToTab(tab);
            FocusInput();
        }
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TerminalTabInfo tab)
        {
            RemoveTab(tab);
        }
    }

    private Run? _promptRun;
    private Run? _inputRun;

    public void SetWorkingDirectory(string? directory)
    {
        if (_activeTab == null) return;
        if (_activeTab.Service.SetWorkingDirectory(directory))
            UpdatePromptForTab(_activeTab, _activeTab.Service.WorkingDirectory);
    }

    public void FocusInput()
    {
        TerminalBox.Focus();
        if (_inputRun != null)
            TerminalBox.CaretPosition = _inputRun.ContentEnd;
    }

    public void EnsureStarted()
    {
        _activeTab?.Service.StartShell();
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
        if (_activeTab == null) return;
        _promptRun = new Run(_activeTab.CurrentPrompt) { Foreground = GetPromptBrush() };
        _inputRun = new Run("") { Foreground = GetForegroundBrush() };

        var para = new Paragraph { Margin = new Thickness(0) };
        para.Inlines.Add(_promptRun);
        para.Inlines.Add(_inputRun);

        TerminalBox.Document.Blocks.Add(para);
        TerminalBox.CaretPosition = _inputRun.ContentEnd;
        TerminalBox.Focus();
        TerminalBox.ScrollToEnd();

        _activeTab.PromptRun = _promptRun;
        _activeTab.InputRun = _inputRun;
    }

    private void ShowPromptOnTab(TerminalTabInfo tab)
    {
        var promptRun = new Run(tab.CurrentPrompt) { Foreground = GetPromptBrush() };
        var inputRun = new Run("") { Foreground = GetForegroundBrush() };

        var para = new Paragraph { Margin = new Thickness(0) };
        para.Inlines.Add(promptRun);
        para.Inlines.Add(inputRun);

        tab.Document.Blocks.Add(para);
        tab.PromptRun = promptRun;
        tab.InputRun = inputRun;
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
        var decision = TerminalKeyInputPolicy.Decide(
            e.Key,
            Keyboard.Modifiers,
            _activeTab != null,
            _activeTab?.Service.IsBusy == true,
            TerminalBox.Selection.IsEmpty,
            IsCaretInInputRun(),
            IsSelectionInInputRun(),
            IsCaretAtInputStart());

        if (decision.Action == TerminalKeyAction.None)
            return;

        if (_activeTab == null) return;
        var runner = _activeTab.Service;

        switch (decision.Action)
        {
            case TerminalKeyAction.StopProcess:
                runner.StopCurrentProcess();
                break;
            case TerminalKeyAction.MoveCaretToInputEnd:
                MoveCaretToInputEnd();
                break;
            case TerminalKeyAction.SubmitInput:
                SubmitInput(runner);
                break;
            case TerminalKeyAction.PreviousHistory:
                SetInputTextIfNotNull(runner.GetPreviousHistoryItem());
                break;
            case TerminalKeyAction.NextHistory:
                SetInputTextIfNotNull(runner.GetNextHistoryItem());
                break;
            case TerminalKeyAction.MoveToInputStart:
                if (_inputRun != null)
                    TerminalBox.CaretPosition = _inputRun.ContentStart;
                break;
        }

        e.Handled = decision.Handled;
    }

    private void SubmitInput(CommandRunnerService runner)
    {
        var input = GetInputText().Trim();
        if (string.IsNullOrEmpty(input))
            return;

        _inputRun = null;
        _promptRun = null;
        _activeTab!.InputRun = null;
        _activeTab.PromptRun = null;
        runner.ExecuteCommand(input);
    }

    private void SetInputTextIfNotNull(string? input)
    {
        if (input != null)
            SetInputText(input);
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
            TerminalBox.Focus();
            Dispatcher.BeginInvoke(() => MoveCaretToInputEnd(),
                System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (!IsCaretInInputRun()) MoveCaretToInputEnd();

        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            var text = (string)e.DataObject.GetData(DataFormats.UnicodeText);
            text = TerminalPasteNormalizer.NormalizeSingleLine(text);

            var dataObj = new DataObject();
            dataObj.SetData(DataFormats.UnicodeText, text);
            e.DataObject = dataObj;
        }
    }

    private void OnPreviewCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
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
        text = text.TrimEnd('\r', '\n', ' ');
        if (string.IsNullOrEmpty(text)) return;

        var outputRun = new Run(text + "\n") { Foreground = GetForegroundBrush() };
        var para = new Paragraph { Margin = new Thickness(0) };
        para.Inlines.Add(outputRun);

        var promptPara = _promptRun?.Parent as Paragraph;
        if (promptPara != null)
            TerminalBox.Document.Blocks.InsertBefore(promptPara, para);
        else
            TerminalBox.Document.Blocks.Add(para);

        while (TerminalBox.Document.Blocks.Count > MaxParagraphs)
            TerminalBox.Document.Blocks.Remove(TerminalBox.Document.Blocks.FirstBlock);

        TerminalBox.ScrollToEnd();
    }

    private void AppendOutputToTab(TerminalTabInfo tab, string text)
    {
        text = text.TrimEnd('\r', '\n', ' ');
        if (string.IsNullOrEmpty(text)) return;

        var outputRun = new Run(text + "\n") { Foreground = GetForegroundBrush() };
        var para = new Paragraph { Margin = new Thickness(0) };
        para.Inlines.Add(outputRun);

        var promptPara = tab.PromptRun?.Parent as Paragraph;
        if (promptPara != null)
            tab.Document.Blocks.InsertBefore(promptPara, para);
        else
            tab.Document.Blocks.Add(para);

        while (tab.Document.Blocks.Count > MaxParagraphs)
            tab.Document.Blocks.Remove(tab.Document.Blocks.FirstBlock);
    }

    private void UpdatePromptForTab(TerminalTabInfo tab, string cwd)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        tab.CurrentPrompt = TerminalPromptFormatter.FormatPrompt(cwd, userProfile);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        TerminalBox.Document.Blocks.Clear();
        ShowPrompt();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _activeTab?.Service.StopCurrentProcess();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsCommandRunnerVisible = false;
        else
            Visibility = Visibility.Collapsed;
    }
}
