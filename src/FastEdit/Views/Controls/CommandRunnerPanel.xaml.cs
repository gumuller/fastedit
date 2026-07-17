using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.Infrastructure;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;

namespace FastEdit.Views.Controls;

public sealed class TerminalTabInfo
{
    public string Title { get; set; } = "";
    public ICommandRunner Service { get; }
    public FlowDocument Document { get; }
    public Run? PromptRun { get; set; }
    public Run? InputRun { get; set; }
    public Run? PendingOutputRun { get; set; }
    public string CurrentPrompt { get; set; } = "❯ ";
    public Action<string>? OutputReceivedHandler { get; set; }
    public Action<string>? WorkingDirectoryChangedHandler { get; set; }
    public Action? CommandStartedHandler { get; set; }
    public Action? CommandCompletedHandler { get; set; }
    public object OutputDispatchLock { get; } = new();
    public StringBuilder PendingUiOutput { get; } = new();
    public bool IsOutputDispatchPending { get; set; }

    public TerminalTabInfo(string title, ICommandRunner service)
    {
        Title = title;
        Service = service;
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

}

public partial class CommandRunnerPanel : UserControl
{
    private static readonly ICommandRunnerFactory FallbackRunnerFactory = new CommandRunnerFactory();
    private readonly List<TerminalTabInfo> _tabs = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private TerminalTabInfo? _activeTab;
    private int _tabCounter;
    private bool _shutdownRequested;
    private string? _desiredWorkingDirectory;
    private const int MaxParagraphs = 5000;

    public static readonly DependencyProperty RunnerFactoryProperty =
        DependencyProperty.Register(
            nameof(RunnerFactory),
            typeof(ICommandRunnerFactory),
            typeof(CommandRunnerPanel),
            new PropertyMetadata(null));

    public ICommandRunnerFactory? RunnerFactory
    {
        get => (ICommandRunnerFactory?)GetValue(RunnerFactoryProperty);
        set => SetValue(RunnerFactoryProperty, value);
    }

    public CommandRunnerPanel()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        DataObject.AddPastingHandler(TerminalBox, OnPaste);

        TerminalBox.AddHandler(CommandManager.PreviewExecutedEvent,
            new ExecutedRoutedEventHandler(OnPreviewCommandExecuted), true);
    }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_shutdownRequested)
            return;

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownRequested)
                return;

            if (_tabs.Count == 0)
                AddNewTab();

            if (_activeTab != null)
            {
                await _activeTab.Service.StartShellAsync(
                    _desiredWorkingDirectory,
                    cancellationToken);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _shutdownRequested = true;
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_tabs.Count == 0)
                return;

            var tabs = _tabs.ToArray();
            foreach (var tab in tabs)
                UnsubscribeFromRunner(tab);

            var shutdownTask = Task.WhenAll(tabs.Select(tab => tab.Service.ShutdownAsync()));
            try
            {
                await shutdownTask.WaitAsync(cancellationToken);
            }
            finally
            {
                _tabs.Clear();
                _activeTab = null;
                _promptRun = null;
                _inputRun = null;
                StopButton.IsEnabled = false;
                TabStripPanel.Children.Clear();
                TerminalBox.Document = new FlowDocument();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _shutdownRequested = false;
        try
        {
            await EnsureStartedAsync();
        }
        catch (ObjectDisposedException ex)
        {
            Trace.TraceWarning("Terminal runner was disposed while loading the panel: {0}", ex.Message);
        }
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await ShutdownAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Trace.TraceWarning("Terminal panel shutdown exceeded 5000 ms while unloading.");
        }
    }

    private void SubscribeToRunner(TerminalTabInfo tab)
    {
        tab.OutputReceivedHandler = text => QueueOutputForUi(tab, text);
        tab.CommandStartedHandler = () => DispatchToUi(() =>
        {
            if (_activeTab == tab)
                StopButton.IsEnabled = true;
        });
        tab.CommandCompletedHandler = () => DispatchToUi(() =>
        {
            if (!_tabs.Contains(tab))
                return;

            if (_activeTab == tab)
                StopButton.IsEnabled = false;
            ShowPrompt(tab, focus: _activeTab == tab);
        });
        tab.WorkingDirectoryChangedHandler = cwd => DispatchToUi(() =>
        {
            UpdatePromptForTab(tab, cwd);
            if (_activeTab == tab)
                _desiredWorkingDirectory = cwd;
        });

        tab.Service.OutputReceived += tab.OutputReceivedHandler;
        tab.Service.CommandStarted += tab.CommandStartedHandler;
        tab.Service.CommandCompleted += tab.CommandCompletedHandler;
        tab.Service.WorkingDirectoryChanged += tab.WorkingDirectoryChangedHandler;
    }

    private static void UnsubscribeFromRunner(TerminalTabInfo tab)
    {
        if (tab.OutputReceivedHandler != null)
            tab.Service.OutputReceived -= tab.OutputReceivedHandler;
        if (tab.CommandStartedHandler != null)
            tab.Service.CommandStarted -= tab.CommandStartedHandler;
        if (tab.CommandCompletedHandler != null)
            tab.Service.CommandCompleted -= tab.CommandCompletedHandler;
        if (tab.WorkingDirectoryChangedHandler != null)
            tab.Service.WorkingDirectoryChanged -= tab.WorkingDirectoryChangedHandler;

        tab.OutputReceivedHandler = null;
        tab.CommandStartedHandler = null;
        tab.CommandCompletedHandler = null;
        tab.WorkingDirectoryChangedHandler = null;
        lock (tab.OutputDispatchLock)
        {
            tab.PendingUiOutput.Clear();
            tab.IsOutputDispatchPending = false;
        }
    }

    private void QueueOutputForUi(TerminalTabInfo tab, string text)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        lock (tab.OutputDispatchLock)
        {
            tab.PendingUiOutput.Append(text);
            if (tab.IsOutputDispatchPending)
                return;

            tab.IsOutputDispatchPending = true;
        }

        _ = Dispatcher.BeginInvoke(() => DrainOutputOnUi(tab));
    }

    private void DrainOutputOnUi(TerminalTabInfo tab)
    {
        string text;
        lock (tab.OutputDispatchLock)
        {
            text = tab.PendingUiOutput.ToString();
            tab.PendingUiOutput.Clear();
            tab.IsOutputDispatchPending = false;
        }

        if (_tabs.Contains(tab))
            AppendOutput(tab, text);
    }

    private void DispatchToUi(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        _ = Dispatcher.BeginInvoke(action);
    }

    private TerminalTabInfo AddNewTab()
    {
        _tabCounter++;
        var tab = new TerminalTabInfo(
            $"Terminal {_tabCounter}",
            (RunnerFactory ?? FallbackRunnerFactory).Create());

        SubscribeToRunner(tab);

        UpdatePromptForTab(tab, tab.Service.WorkingDirectory);
        _tabs.Add(tab);
        SwitchToTab(tab);
        RebuildTabStrip();
        return tab;
    }

    private async Task RemoveTabAsync(TerminalTabInfo tab)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_shutdownRequested || _tabs.Count <= 1)
                return;

            var idx = _tabs.IndexOf(tab);
            var wasActive = _activeTab == tab;
            _tabs.Remove(tab);
            UnsubscribeFromRunner(tab);
            await tab.Service.ShutdownAsync().ConfigureAwait(true);

            if (TerminalTabClosePolicy.TryGetNextActiveIndex(
                    _tabs.Count + 1,
                    idx,
                    wasActive,
                    out var newIdx))
            {
                SwitchToTab(_tabs[newIdx]);
            }

            RebuildTabStrip();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void SwitchToTab(TerminalTabInfo tab)
    {
        if (_activeTab != null)
        {
            _activeTab.PromptRun = _promptRun;
            _activeTab.InputRun = _inputRun;
        }

        _activeTab = tab;
        if (tab.Service.IsRunning)
            _desiredWorkingDirectory = tab.Service.WorkingDirectory;

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
        addBtn.Click += AddTab_Click;
        TabStripPanel.Children.Add(addBtn);
    }

    private async void AddTab_Click(object sender, RoutedEventArgs e)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (_shutdownRequested)
                return;

            var newTab = AddNewTab();
            await newTab.Service.StartShellAsync(_desiredWorkingDirectory);
            FocusInput();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void TabBorder_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is TerminalTabInfo tab)
        {
            SwitchToTab(tab);
            FocusInput();
        }
    }

    private async void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TerminalTabInfo tab)
        {
            await RemoveTabAsync(tab);
        }
    }

    private Run? _promptRun;
    private Run? _inputRun;

    public async Task SetWorkingDirectoryAsync(
        string? directory,
        CancellationToken cancellationToken = default)
    {
        directory = NormalizeDirectory(directory);
        if (directory == null)
            return;

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownRequested)
                return;

            if (_activeTab == null)
            {
                _desiredWorkingDirectory = directory;
                return;
            }

            if (await _activeTab.Service.SetWorkingDirectoryAsync(directory, cancellationToken))
            {
                _desiredWorkingDirectory = _activeTab.Service.WorkingDirectory;
                UpdatePromptForTab(_activeTab, _activeTab.Service.WorkingDirectory);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static string? NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
            return null;

        if (File.Exists(directory))
            directory = Path.GetDirectoryName(directory);

        return !string.IsNullOrEmpty(directory) && Directory.Exists(directory)
            ? directory
            : null;
    }

    public void FocusInput()
    {
        TerminalBox.Focus();
        if (_inputRun != null)
            TerminalBox.CaretPosition = _inputRun.ContentEnd;
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
        if (_activeTab != null)
            ShowPrompt(_activeTab, focus: true);
    }

    private void ShowPrompt(TerminalTabInfo tab, bool focus)
    {
        var promptRun = new Run(tab.CurrentPrompt) { Foreground = GetPromptBrush() };
        var inputRun = new Run("") { Foreground = GetForegroundBrush() };

        var para = new Paragraph { Margin = new Thickness(0) };
        para.Inlines.Add(promptRun);
        para.Inlines.Add(inputRun);

        tab.Document.Blocks.Add(para);
        tab.PromptRun = promptRun;
        tab.InputRun = inputRun;
        tab.PendingOutputRun = null;

        if (!focus)
            return;

        _promptRun = promptRun;
        _inputRun = inputRun;
        TerminalBox.CaretPosition = inputRun.ContentEnd;
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

    private async void TerminalBox_PreviewKeyDown(object sender, KeyEventArgs e)
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
                await runner.StopCurrentProcessAsync();
                break;
            case TerminalKeyAction.MoveCaretToInputEnd:
                MoveCaretToInputEnd();
                break;
            case TerminalKeyAction.SubmitInput:
                await SubmitInputAsync(runner);
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

    private async Task SubmitInputAsync(ICommandRunner runner)
    {
        var input = GetInputText().Trim();
        if (string.IsNullOrEmpty(input))
            return;

        _inputRun = null;
        _promptRun = null;
        _activeTab!.InputRun = null;
        _activeTab.PromptRun = null;
        await runner.ExecuteCommandAsync(input);
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

    private void AppendOutput(TerminalTabInfo tab, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var hasNewLine = index < lines.Length - 1;
            if (lines[index].Length > 0)
            {
                var outputRun = EnsureOutputRun(tab);
                outputRun.Text += lines[index];
            }
            else if (hasNewLine && tab.PendingOutputRun == null)
            {
                EnsureOutputRun(tab);
            }

            if (hasNewLine)
                tab.PendingOutputRun = null;
        }

        if (_activeTab == tab)
            TerminalBox.ScrollToEnd();
    }

    private Run EnsureOutputRun(TerminalTabInfo tab)
    {
        if (tab.PendingOutputRun != null)
            return tab.PendingOutputRun;

        var outputRun = new Run { Foreground = GetForegroundBrush() };
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        paragraph.Inlines.Add(outputRun);

        var promptParagraph = tab.PromptRun?.Parent as Paragraph;
        if (promptParagraph != null)
            tab.Document.Blocks.InsertBefore(promptParagraph, paragraph);
        else
            tab.Document.Blocks.Add(paragraph);

        while (tab.Document.Blocks.Count > MaxParagraphs)
            tab.Document.Blocks.Remove(tab.Document.Blocks.FirstBlock);

        tab.PendingOutputRun = outputRun;
        return outputRun;
    }

    private void UpdatePromptForTab(TerminalTabInfo tab, string cwd)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        tab.CurrentPrompt = TerminalPromptFormatter.FormatPrompt(cwd, userProfile);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        TerminalBox.Document.Blocks.Clear();
        if (_activeTab != null)
            _activeTab.PendingOutputRun = null;
        ShowPrompt();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTab != null)
            await _activeTab.Service.StopCurrentProcessAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsCommandRunnerVisible = false;
        else
            Visibility = Visibility.Collapsed;
    }
}
