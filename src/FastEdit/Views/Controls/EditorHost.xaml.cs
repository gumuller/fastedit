using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FastEdit.Helpers;
using FastEdit.Infrastructure;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;
using FastEdit.ViewModels;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;

namespace FastEdit.Views.Controls;

public class BreadcrumbDisplayItem
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Separator { get; set; } = "›";
    public Visibility SeparatorVisibility { get; set; } = Visibility.Visible;
    public int Line { get; set; }
}

public partial class EditorHost : UserControl
{
    private EditorTabViewModel? _currentVm;
    private SearchPanel? _searchPanel;
    private System.Windows.Controls.TextBox? _replaceTextBox;
    private System.Windows.Controls.Primitives.ToggleButton? _replaceToggle;
    private FoldingManager? _foldingManager;
    private BracketHighlightRenderer? _bracketRenderer;
    private IndentGuideRenderer? _indentGuideRenderer;
    private OccurrenceHighlightRenderer? _occurrenceRenderer;
    private readonly FileWatcherService _fileWatcher = new();
    private readonly List<int> _bookmarks = new();
    private System.Windows.Threading.DispatcherTimer? _foldingTimer;
    private CompletionWindow? _completionWindow;
    private System.Windows.Threading.DispatcherTimer? _breadcrumbTimer;
    private CaretStyleAdorner? _caretAdorner;
    private FilterBackgroundRenderer? _filterRenderer;
    private FilterDimTransformer? _filterDimTransformer;
    private ILineFilterService? _lineFilterService;
    private IThemeService? _themeService;
    private ISettingsService? _settingsService;
    private ITextToolsService? _textToolsService;
    private IMacroService? _macroService;
    private IDialogService? _dialogService;
    private IFileSystemService? _fileSystemService;
    private MainViewModel? _mainViewModel;
    private Dictionary<int, Models.LineFilterResult> _filterCache = new();
    private bool _isFilterFoldingActive;

    private static readonly IReadOnlyDictionary<MacroAction, Action<EditorHost, MacroStep, ITextToolsService?>> MacroStepHandlers =
        new Dictionary<MacroAction, Action<EditorHost, MacroStep, ITextToolsService?>>
        {
            [MacroAction.TypeText] = static (host, step, _) => host.InsertMacroText(step.Parameter),
            [MacroAction.DeleteBackward] = static (host, _, _) => host.DeleteMacroTextBackward(),
            [MacroAction.DeleteForward] = static (host, _, _) => host.DeleteMacroTextForward(),
            [MacroAction.NewLine] = static (host, _, _) => host.InsertMacroText(Environment.NewLine),
            [MacroAction.DuplicateLine] = static (host, _, _) => host.OnDuplicateLineRequested(),
            [MacroAction.MoveLineUp] = static (host, _, _) => host.OnMoveLineRequested(true),
            [MacroAction.MoveLineDown] = static (host, _, _) => host.OnMoveLineRequested(false),
            [MacroAction.TextTool] = static (host, step, _) => host.RunMacroTextTool(step.Parameter),
        };

    public EditorHost()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    public static readonly DependencyProperty LineFilterServiceProperty =
        DependencyProperty.Register(
            nameof(LineFilterService),
            typeof(ILineFilterService),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public ILineFilterService? LineFilterService
    {
        get => (ILineFilterService?)GetValue(LineFilterServiceProperty);
        set => SetValue(LineFilterServiceProperty, value);
    }

    public static readonly DependencyProperty ThemeServiceProperty =
        DependencyProperty.Register(
            nameof(ThemeService),
            typeof(IThemeService),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public IThemeService? ThemeService
    {
        get => (IThemeService?)GetValue(ThemeServiceProperty);
        set => SetValue(ThemeServiceProperty, value);
    }

    public static readonly DependencyProperty SettingsServiceProperty =
        DependencyProperty.Register(
            nameof(SettingsService),
            typeof(ISettingsService),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public ISettingsService? SettingsService
    {
        get => (ISettingsService?)GetValue(SettingsServiceProperty);
        set => SetValue(SettingsServiceProperty, value);
    }

    public static readonly DependencyProperty TextToolsServiceProperty =
        DependencyProperty.Register(
            nameof(TextToolsService),
            typeof(ITextToolsService),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public ITextToolsService? TextToolsService
    {
        get => (ITextToolsService?)GetValue(TextToolsServiceProperty);
        set => SetValue(TextToolsServiceProperty, value);
    }

    public static readonly DependencyProperty MacroServiceProperty =
        DependencyProperty.Register(
            nameof(MacroService),
            typeof(IMacroService),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public IMacroService? MacroService
    {
        get => (IMacroService?)GetValue(MacroServiceProperty);
        set => SetValue(MacroServiceProperty, value);
    }

    public static readonly DependencyProperty DialogServiceProperty =
        DependencyProperty.Register(
            nameof(DialogService),
            typeof(IDialogService),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public IDialogService? DialogService
    {
        get => (IDialogService?)GetValue(DialogServiceProperty);
        set => SetValue(DialogServiceProperty, value);
    }

    public static readonly DependencyProperty FileSystemServiceProperty =
        DependencyProperty.Register(
            nameof(FileSystemService),
            typeof(IFileSystemService),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public IFileSystemService? FileSystemService
    {
        get => (IFileSystemService?)GetValue(FileSystemServiceProperty);
        set => SetValue(FileSystemServiceProperty, value);
    }

    public static readonly DependencyProperty MainViewModelProperty =
        DependencyProperty.Register(
            nameof(MainViewModel),
            typeof(MainViewModel),
            typeof(EditorHost),
            new PropertyMetadata(null));

    public MainViewModel? MainViewModel
    {
        get => (MainViewModel?)GetValue(MainViewModelProperty);
        set => SetValue(MainViewModelProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConfigureServices();

        // Install search panel with explicit style to bypass WPF UI's implicit TextBox style
        _searchPanel = SearchPanel.Install(TextEditor);
        _searchPanel.Style = (Style)FindResource("FastEditSearchPanelStyle");

        // Wire up replace buttons after template is applied
        _searchPanel.ApplyTemplate();
        WireReplaceControls();

        ApplySearchPanelMarkerBrush();
        ApplyEditorThemeBrushes();
        ApplyEditorSettings();

        // Install bracket highlight renderer
        _bracketRenderer = new BracketHighlightRenderer(TextEditor);
        TextEditor.TextArea.TextView.BackgroundRenderers.Add(_bracketRenderer);
        TextEditor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForBrackets;

        // Install breadcrumb debounce timer
        _breadcrumbTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _breadcrumbTimer.Tick += (s, args) =>
        {
            _breadcrumbTimer.Stop();
            UpdateBreadcrumbs();
        };

        // Install indent guide renderer
        _indentGuideRenderer = new IndentGuideRenderer(TextEditor.TextArea.TextView);
        TextEditor.TextArea.TextView.BackgroundRenderers.Add(_indentGuideRenderer);

        // Install occurrence highlight renderer
        _occurrenceRenderer = new OccurrenceHighlightRenderer(TextEditor.TextArea.TextView);
        TextEditor.TextArea.TextView.BackgroundRenderers.Add(_occurrenceRenderer);
        TextEditor.TextArea.SelectionChanged += OnSelectionChangedForOccurrences;

        // Install filter renderers
        _filterRenderer = new FilterBackgroundRenderer(TextEditor.TextArea.TextView);
        TextEditor.TextArea.TextView.BackgroundRenderers.Add(_filterRenderer);
        _filterDimTransformer = new FilterDimTransformer();
        TextEditor.TextArea.TextView.LineTransformers.Add(_filterDimTransformer);
        _lineFilterService!.FiltersChanged += OnLineFiltersChanged;

        // Folding update timer
        _foldingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _foldingTimer.Tick += (s, args) => UpdateFoldings();
        _foldingTimer.Start();

        // File watcher for auto-reload
        _fileWatcher.FileChanged += OnFileWatcherChanged;

        // Install custom caret style adorner
        _caretAdorner = new CaretStyleAdorner(TextEditor.TextArea);
        var adornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(TextEditor.TextArea);
        adornerLayer?.Add(_caretAdorner);
        ApplyCaretStyle();

        // Subscribe to theme changes
        if (_themeService != null)
        {
            _themeService.ThemeChanged += OnThemeChanged;
        }

        // Subscribe to ViewModel events for editor actions
        if (_mainViewModel != null)
        {
            _mainViewModel.FindRequested += OnFindRequested;
            _mainViewModel.ReplaceRequested += OnReplaceRequested;
            _mainViewModel.GoToLineRequested += OnGoToLineRequested;
            _mainViewModel.DuplicateLineRequested += OnDuplicateLineRequested;
            _mainViewModel.MoveLineRequested += OnMoveLineRequested;
            _mainViewModel.FormatDocumentRequested += OnFormatDocumentRequested;
            _mainViewModel.MinifyDocumentRequested += OnMinifyDocumentRequested;
            _mainViewModel.ToggleBookmarkRequested += OnToggleBookmark;
            _mainViewModel.NextBookmarkRequested += OnNextBookmark;
            _mainViewModel.PrevBookmarkRequested += OnPrevBookmark;
            _mainViewModel.ShowCompletionRequested += OnShowCompletion;
            _mainViewModel.ToggleSplitViewRequested += OnToggleSplitView;
            _mainViewModel.TextToolRequested += OnTextToolRequested;
            _mainViewModel.PrintRequested += OnPrintRequested;
            _mainViewModel.SelectNextOccurrenceRequested += OnSelectNextOccurrence;
            _mainViewModel.SelectAllOccurrencesRequested += OnSelectAllOccurrences;
            _mainViewModel.MacroStartRecordingRequested += OnMacroStartRecording;
            _mainViewModel.MacroStopRecordingRequested += OnMacroStopRecording;
            _mainViewModel.MacroPlaybackRequested += OnMacroPlayback;
            _mainViewModel.PropertyChanged += OnMainVmPropertyChanged;
        }

        if (_currentVm != null)
        {
            UpdateEditor(_currentVm);
        }
    }

    private void ConfigureServices()
    {
        _lineFilterService = LineFilterService ?? throw MissingDependency(nameof(LineFilterService));
        _themeService = ThemeService ?? throw MissingDependency(nameof(ThemeService));
        _settingsService = SettingsService ?? throw MissingDependency(nameof(SettingsService));
        _textToolsService = TextToolsService ?? throw MissingDependency(nameof(TextToolsService));
        _macroService = MacroService ?? throw MissingDependency(nameof(MacroService));
        _dialogService = DialogService ?? throw MissingDependency(nameof(DialogService));
        _fileSystemService = FileSystemService ?? throw MissingDependency(nameof(FileSystemService));
        _mainViewModel = MainViewModel ?? throw MissingDependency(nameof(MainViewModel));
    }

    private static InvalidOperationException MissingDependency(string propertyName) =>
        new($"EditorHost requires {propertyName} before it is loaded.");

    private void SetStatus(string statusText)
    {
        if (_mainViewModel != null)
            _mainViewModel.StatusText = statusText;
    }

    private void OnMainVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsWordWrapEnabled):
                TextEditor.WordWrap = vm.IsWordWrapEnabled;
                break;
            case nameof(MainViewModel.IsWhitespaceVisible):
                TextEditor.Options.ShowTabs = vm.IsWhitespaceVisible;
                TextEditor.Options.ShowSpaces = vm.IsWhitespaceVisible;
                TextEditor.Options.ShowEndOfLine = vm.IsWhitespaceVisible;
                break;
            case nameof(MainViewModel.EditorFontSize):
                TextEditor.FontSize = vm.EditorFontSize;
                break;
            case nameof(MainViewModel.IsFoldingEnabled):
                if (vm.IsFoldingEnabled && GetCurrentFeatureGate().FoldingEnabled)
                    InstallFolding();
                else
                    UninstallFolding();
                break;
            case nameof(MainViewModel.IsMinimapVisible):
                UpdateMinimapVisibility(vm.IsMinimapVisible);
                break;
            case nameof(MainViewModel.IsAutoReloadEnabled):
                UpdateAutoReload(vm.IsAutoReloadEnabled);
                break;
            case nameof(MainViewModel.IsIndentGuidesEnabled):
                UpdateIndentGuides(vm.IsIndentGuidesEnabled);
                break;
            case nameof(MainViewModel.IsBreadcrumbVisible):
                UpdateBreadcrumbVisibility(vm.IsBreadcrumbVisible);
                break;
        }
    }

    private void ApplyEditorSettings()
    {
        if (_mainViewModel == null) return;

        TextEditor.WordWrap = _mainViewModel.IsWordWrapEnabled;
        TextEditor.FontSize = _mainViewModel.EditorFontSize;
        TextEditor.Options.ShowTabs = _mainViewModel.IsWhitespaceVisible;
        TextEditor.Options.ShowSpaces = _mainViewModel.IsWhitespaceVisible;
        TextEditor.Options.ShowEndOfLine = _mainViewModel.IsWhitespaceVisible;
    }

    private void ApplyCaretStyle()
    {
        if (_caretAdorner == null) return;
        if (_settingsService != null)
            _caretAdorner.CaretStyle = _settingsService.CursorStyle;
    }

    // --- Find & Replace ---
    private void OnFindRequested()
    {
        if (!IsActiveEditorHost()) return;

        // Route to hex search when in binary mode
        var vm = DataContext as EditorTabViewModel;
        if (IsBinaryMode(vm))
        {
            HexEditor.ShowSearch();
            return;
        }

        _searchPanel?.Open();
    }

    private void OnReplaceRequested()
    {
        if (!IsActiveEditorHost()) return;
        _searchPanel?.Open();
        // Toggle replace row visible
        if (_replaceToggle != null)
            _replaceToggle.IsChecked = true;
    }

    private void WireReplaceControls()
    {
        if (_searchPanel == null) return;

        _replaceTextBox = _searchPanel.Template.FindName("PART_replaceTextBox", _searchPanel) as System.Windows.Controls.TextBox;
        _replaceToggle = _searchPanel.Template.FindName("PART_toggleReplace", _searchPanel) as System.Windows.Controls.Primitives.ToggleButton;

        if (_searchPanel.Template.FindName("PART_replaceButton", _searchPanel) is System.Windows.Controls.Button replaceBtn)
            replaceBtn.Click += ReplaceButton_Click;

        if (_searchPanel.Template.FindName("PART_replaceAllButton", _searchPanel) is System.Windows.Controls.Button replaceAllBtn)
            replaceAllBtn.Click += ReplaceAllButton_Click;
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchPanel == null || _replaceTextBox == null) return;

        var searchPattern = _searchPanel.SearchPattern;
        var replaceText = _replaceTextBox.Text ?? "";
        if (string.IsNullOrEmpty(searchPattern)) return;

        var doc = TextEditor.Document;
        var selection = TextEditor.TextArea.Selection;

        // If current selection matches the search pattern, replace it
        if (!selection.IsEmpty)
        {
            var selectedText = selection.GetText();
            bool matches = _searchPanel.MatchCase
                ? selectedText == searchPattern
                : string.Equals(selectedText, searchPattern, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                doc.Replace(selection.SurroundingSegment.Offset, selection.SurroundingSegment.Length, replaceText);
            }
        }

        // Find next
        _searchPanel.FindNext();
    }

    private void ReplaceAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_searchPanel == null || _replaceTextBox == null) return;

        var searchPattern = _searchPanel.SearchPattern;
        var replaceText = _replaceTextBox.Text ?? "";
        if (string.IsNullOrEmpty(searchPattern)) return;

        var doc = TextEditor.Document;
        var comparison = _searchPanel.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = doc.Text;
        int count = 0;

        doc.BeginUpdate();
        // Replace from end to start to preserve offsets
        int index = text.LastIndexOf(searchPattern, comparison);
        while (index >= 0)
        {
            doc.Replace(index, searchPattern.Length, replaceText);
            count++;
            if (index == 0) break;
            text = doc.Text;
            index = text.LastIndexOf(searchPattern, index - 1, comparison);
        }
        doc.EndUpdate();

        SetStatus($"Replaced {count} occurrence(s)");
    }

    // --- Go To Line ---
    private void OnGoToLineRequested(int currentLine)
    {
        if (!IsActiveEditorHost()) return;
        // Handled by MainWindow dialog — just set caret
    }

    public void GoToLine(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > TextEditor.Document.LineCount) return;
        var line = TextEditor.Document.GetLineByNumber(lineNumber);
        TextEditor.CaretOffset = line.Offset;
        TextEditor.ScrollToLine(lineNumber);
        TextEditor.TextArea.Caret.BringCaretToView();
        TextEditor.Focus();
    }

    // --- Line Operations ---
    private void OnDuplicateLineRequested()
    {
        if (!IsActiveEditorHost() || !IsTextMode(_currentVm)) return;
        DuplicateLine();
    }

    public void DuplicateLine()
    {
        var doc = TextEditor.Document;
        var caret = TextEditor.TextArea.Caret;
        var line = doc.GetLineByNumber(caret.Line);
        var lineText = doc.GetText(line.Offset, line.TotalLength);

        // If the line has no delimiter (last line), prepend a newline
        if (line.DelimiterLength == 0)
            lineText = Environment.NewLine + doc.GetText(line.Offset, line.Length);

        doc.Insert(line.Offset + line.TotalLength, lineText);
    }

    private void OnMoveLineRequested(bool moveUp)
    {
        if (!IsActiveEditorHost() || !IsTextMode(_currentVm)) return;
        if (moveUp) MoveLineUp(); else MoveLineDown();
    }

    public void MoveLineUp()
    {
        var doc = TextEditor.Document;
        var caret = TextEditor.TextArea.Caret;
        if (caret.Line <= 1) return;

        var currentLine = doc.GetLineByNumber(caret.Line);
        var prevLine = doc.GetLineByNumber(caret.Line - 1);
        var col = caret.Column;

        var currentText = doc.GetText(currentLine.Offset, currentLine.Length);
        var prevText = doc.GetText(prevLine.Offset, prevLine.Length);

        doc.BeginUpdate();
        doc.Replace(prevLine.Offset, prevLine.Length, currentText);
        var newCurrentLine = doc.GetLineByNumber(caret.Line);
        doc.Replace(newCurrentLine.Offset, newCurrentLine.Length, prevText);
        doc.EndUpdate();

        caret.Line = caret.Line - 1;
        caret.Column = col;
    }

    public void MoveLineDown()
    {
        var doc = TextEditor.Document;
        var caret = TextEditor.TextArea.Caret;
        if (caret.Line >= doc.LineCount) return;

        var currentLine = doc.GetLineByNumber(caret.Line);
        var nextLine = doc.GetLineByNumber(caret.Line + 1);
        var col = caret.Column;

        var currentText = doc.GetText(currentLine.Offset, currentLine.Length);
        var nextText = doc.GetText(nextLine.Offset, nextLine.Length);

        doc.BeginUpdate();
        // Replace next line first (higher offset) to avoid offset shift
        doc.Replace(nextLine.Offset, nextLine.Length, currentText);
        doc.Replace(currentLine.Offset, currentLine.Length, nextText);
        doc.EndUpdate();

        caret.Line = caret.Line + 1;
        caret.Column = col;
    }

    // --- Format / Minify ---
    private void OnFormatDocumentRequested()
    {
        if (!IsActiveEditorHost() || !IsTextMode(_currentVm)) return;
        var lang = _currentVm?.SyntaxLanguage ?? "";

        (string result, string? error) output;
        if (FormatHelper.IsJsonLanguage(lang))
            output = FormatHelper.PrettyPrintJson(TextEditor.Text);
        else if (FormatHelper.IsXmlLanguage(lang))
            output = FormatHelper.PrettyPrintXml(TextEditor.Text);
        else return;

        if (output.error != null)
        {
            SetStatus(output.error);
            return;
        }
        TextEditor.Document.Text = output.result;
    }

    private void OnMinifyDocumentRequested()
    {
        if (!IsActiveEditorHost() || !IsTextMode(_currentVm)) return;
        var lang = _currentVm?.SyntaxLanguage ?? "";

        (string result, string? error) output;
        if (FormatHelper.IsJsonLanguage(lang))
            output = FormatHelper.MinifyJson(TextEditor.Text);
        else if (FormatHelper.IsXmlLanguage(lang))
            output = FormatHelper.MinifyXml(TextEditor.Text);
        else return;

        if (output.error != null)
        {
            SetStatus(output.error);
            return;
        }
        TextEditor.Document.Text = output.result;
    }

    // --- Text Tools ---
    private void OnTextToolRequested(TextToolOperation operation)
    {
        if (!IsActiveEditorHost() || !IsTextMode(_currentVm)) return;

        if (_textToolsService == null) return;

        bool hasSelection = TextEditor.SelectionLength > 0;
        string input = hasSelection ? TextEditor.SelectedText : TextEditor.Text;

        TextToolResult result = TextToolOperationRunner.Execute(_textToolsService, operation, input);
        var plan = TextToolApplicationPlanner.Create(operation, result, hasSelection);

        if (plan.Target == TextToolApplicationTarget.Selection)
        {
            TextEditor.Document.Replace(TextEditor.SelectionStart, TextEditor.SelectionLength, plan.ReplacementText ?? "");
        }
        else if (plan.Target == TextToolApplicationTarget.Document)
        {
            TextEditor.Document.Text = plan.ReplacementText ?? "";
        }

        if (plan.StatusText != null)
            SetStatus(plan.StatusText);
    }

    // --- Print ---
    private void OnPrintRequested()
    {
        if (!IsActiveEditorHost()) return;

        var printDialog = new System.Windows.Controls.PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        var flowDoc = new System.Windows.Documents.FlowDocument(
            new System.Windows.Documents.Paragraph(
                new System.Windows.Documents.Run(TextEditor.Text)));
        flowDoc.FontFamily = TextEditor.FontFamily;
        flowDoc.FontSize = TextEditor.FontSize;
        flowDoc.PagePadding = new Thickness(50);
        flowDoc.ColumnWidth = double.PositiveInfinity;

        var title = _currentVm?.FileName ?? "Untitled";
        printDialog.PrintDocument(
            ((System.Windows.Documents.IDocumentPaginatorSource)flowDoc).DocumentPaginator,
            $"FastEdit - {title}");

        SetStatus($"Printed: {title}");
    }

    // --- Occurrence Selection ---
    private int _lastSelectNextOffset = -1;

    private void OnSelectNextOccurrence()
    {
        if (!CanUseTextFeature(gate => gate.OccurrenceHighlightingEnabled)) return;

        var selectedText = TextEditor.SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            // Select current word
            var offset = TextEditor.CaretOffset;
            var doc = TextEditor.Document;
            int start = offset, end = offset;
            while (start > 0 && IsWordChar(doc.GetCharAt(start - 1))) start--;
            while (end < doc.TextLength && IsWordChar(doc.GetCharAt(end))) end++;
            if (start < end)
            {
                TextEditor.Select(start, end - start);
                _lastSelectNextOffset = start;
            }
            return;
        }

        // Find next occurrence after current selection
        var searchFrom = _lastSelectNextOffset >= 0
            ? TextEditor.SelectionStart + TextEditor.SelectionLength
            : TextEditor.SelectionStart + TextEditor.SelectionLength;

        var text = TextEditor.Text;
        var nextIndex = text.IndexOf(selectedText, searchFrom, StringComparison.Ordinal);

        // Wrap around if not found
        if (nextIndex < 0)
            nextIndex = text.IndexOf(selectedText, 0, StringComparison.Ordinal);

        if (nextIndex >= 0 && nextIndex != TextEditor.SelectionStart)
        {
            TextEditor.Select(nextIndex, selectedText.Length);
            TextEditor.ScrollTo(TextEditor.Document.GetLineByOffset(nextIndex).LineNumber, 0);
            _lastSelectNextOffset = nextIndex;

            SetStatus($"Selected occurrence at offset {nextIndex}");
        }
    }

    private void OnSelectAllOccurrences()
    {
        if (!CanUseTextFeature(gate => gate.OccurrenceHighlightingEnabled)) return;

        var selectedText = TextEditor.SelectedText;
        if (string.IsNullOrEmpty(selectedText))
        {
            // Select current word first
            OnSelectNextOccurrence();
            selectedText = TextEditor.SelectedText;
            if (string.IsNullOrEmpty(selectedText)) return;
        }

        // Count all occurrences
        var text = TextEditor.Text;
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(selectedText, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += selectedText.Length;
        }

        SetStatus($"Found {count} occurrences of \"{(selectedText.Length > 30 ? selectedText[..27] + "..." : selectedText)}\"");

        // Since AvalonEdit doesn't support true multi-cursor,
        // highlight all occurrences and open Find/Replace pre-filled for batch operations
        _occurrenceRenderer?.SetHighlightWord(selectedText, TextEditor.Document);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // --- Macro Recording/Playback ---
    private void OnMacroStartRecording()
    {
        if (!IsActiveEditorHost()) return;
        if (_macroService == null) return;

        _macroService.StartRecording();

        // Hook into text input for recording
        TextEditor.TextArea.TextEntering += OnMacroTextEntering;

        SetStatus("🔴 Recording macro...");
    }

    private void OnMacroStopRecording()
    {
        if (!IsActiveEditorHost()) return;
        if (_macroService == null) return;

        _macroService.StopRecording();
        TextEditor.TextArea.TextEntering -= OnMacroTextEntering;

        SetStatus($"Macro recorded: {_macroService.RecordedStepCount} step(s)");
    }

    private void OnMacroTextEntering(object? sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        if (_macroService?.IsRecording == true && !string.IsNullOrEmpty(e.Text))
        {
            _macroService.RecordStep(new MacroStep(MacroAction.TypeText, e.Text));
        }
    }

    private void OnMacroPlayback(int count)
    {
        if (!IsActiveEditorHost()) return;
        if (_macroService == null || !_macroService.HasMacro) return;

        if (count == 0)
        {
            // Prompt for count
            var input = _dialogService?.ShowInputDialog("Playback Macro", "Number of times to play:", "1");
            if (string.IsNullOrEmpty(input) || !int.TryParse(input, out count) || count <= 0) return;
        }

        var steps = _macroService.GetRecordedSteps();

        for (int i = 0; i < count; i++)
        {
            foreach (var step in steps)
            {
                ExecuteMacroStep(step, _textToolsService);
            }
        }

        SetStatus($"Macro played {count} time(s)");
    }

    private void ExecuteMacroStep(MacroStep step, ITextToolsService? textTools)
    {
        if (MacroStepHandlers.TryGetValue(step.Action, out var execute))
            execute(this, step, textTools);
    }

    private void InsertMacroText(string? text)
    {
        if (text != null)
            TextEditor.Document.Insert(TextEditor.CaretOffset, text);
    }

    private void DeleteMacroTextBackward()
    {
        if (TextEditor.CaretOffset > 0)
            TextEditor.Document.Remove(TextEditor.CaretOffset - 1, 1);
    }

    private void DeleteMacroTextForward()
    {
        if (TextEditor.CaretOffset < TextEditor.Document.TextLength)
            TextEditor.Document.Remove(TextEditor.CaretOffset, 1);
    }

    private void RunMacroTextTool(string? operationName)
    {
        if (operationName != null && TextToolOperationRunner.TryParseLegacyName(operationName, out var operation))
            OnTextToolRequested(operation);
    }

    // --- Code Folding ---
    private void InstallFolding()
    {
        var vm = _currentVm;
        if (!IsTextMode(vm) || !GetCurrentFeatureGate().FoldingEnabled) return;
        _foldingManager = FoldingHelper.Install(TextEditor, vm.SyntaxLanguage);
    }

    private void UninstallFolding()
    {
        FoldingHelper.Uninstall(TextEditor);
        _foldingManager = null;
    }

    private void UpdateFoldings()
    {
        if (_isFilterFoldingActive) return;
        if (_foldingManager == null || _currentVm == null) return;
        FoldingHelper.Update(_foldingManager, _currentVm.SyntaxLanguage, TextEditor.Document);
    }

    // --- Line Filters ---
    private void OnLineFiltersChanged()
    {
        Dispatcher.Invoke(() =>
        {
            RecomputeFilterCache();
            ApplyFilterFolding();
        });
    }

    private void RecomputeFilterCache()
    {
        _filterCache.Clear();

        if (_lineFilterService == null || !_lineFilterService.HasActiveFilters)
        {
            _filterRenderer?.UpdateResults(_filterCache);
            _filterDimTransformer?.UpdateResults(_filterCache, false);
            TextEditor.TextArea.TextView.Redraw();
            UpdateFilterMatchCount();
            return;
        }

        var doc = TextEditor.Document;
        if (doc == null) return;

        for (int i = 1; i <= doc.LineCount; i++)
        {
            var line = doc.GetLineByNumber(i);
            var text = doc.GetText(line.Offset, line.Length);
            var result = _lineFilterService.EvaluateLine(text);
            if (result != Models.LineFilterResult.NoMatch)
                _filterCache[i] = result;
        }

        _filterRenderer?.UpdateResults(_filterCache);
        _filterDimTransformer?.UpdateResults(_filterCache, true);
        TextEditor.TextArea.TextView.Redraw();
        UpdateFilterMatchCount();
    }

    private void ApplyFilterFolding()
    {
        if (_lineFilterService == null) return;

        if (_lineFilterService.ShowOnlyFilteredLines && _lineFilterService.HasActiveFilters)
            EnterFilterFoldingMode();
        else if (_isFilterFoldingActive)
            ExitFilterFoldingMode();
    }

    private void EnterFilterFoldingMode()
    {
        _isFilterFoldingActive = true;
        _foldingTimer?.Stop();
        EnsureFilterFoldingManager();

        var doc = TextEditor.Document;
        if (doc == null || _foldingManager == null) return;

        _foldingManager.UpdateFoldings(FilterFoldingBuilder.Create(doc, _filterCache), -1);
    }

    private void EnsureFilterFoldingManager()
    {
        _foldingManager ??= FoldingHelper.Install(TextEditor, _currentVm?.SyntaxLanguage ?? "Text");
        _foldingManager ??= FoldingManager.Install(TextEditor.TextArea);
    }

    private void ExitFilterFoldingMode()
    {
        _isFilterFoldingActive = false;
        UninstallFolding();

        if (IsTextMode(_currentVm))
        {
            InstallFolding();
            UpdateFoldings();
        }

        _foldingTimer?.Start();
    }

    private void UpdateFilterMatchCount()
    {
        // Find the FilterPanel in the visual tree and update match count
        var mainWindow = Window.GetWindow(this) as MainWindow;
        if (mainWindow == null) return;

        var filterPanel = mainWindow.FindName("LineFilterPanel") as FilterPanel;
        if (filterPanel == null) return;

        int matched = _filterCache.Count(kv => kv.Value.IsVisible);
        int total = TextEditor.Document?.LineCount ?? 0;
        filterPanel.UpdateMatchCount(matched, total);
    }

    /// <summary>Jump to the next line matching any active filter (wraps around).</summary>
    public void NavigateToNextFilterMatch() => NavigateFilterMatch(forward: true);

    /// <summary>Jump to the previous line matching any active filter (wraps around).</summary>
    public void NavigateToPreviousFilterMatch() => NavigateFilterMatch(forward: false);

    private void NavigateFilterMatch(bool forward)
    {
        if (_filterCache.Count == 0 || TextEditor.Document == null) return;

        var matchingLines = _filterCache
            .Where(kv => kv.Value.IsVisible)
            .Select(kv => kv.Key)
            .OrderBy(n => n)
            .ToList();

        if (matchingLines.Count == 0) return;

        int currentLine = TextEditor.TextArea.Caret.Line;
        int target;

        if (forward)
        {
            target = matchingLines.FirstOrDefault(n => n > currentLine);
            if (target == 0) target = matchingLines[0]; // wrap to start
        }
        else
        {
            target = matchingLines.LastOrDefault(n => n < currentLine);
            if (target == 0) target = matchingLines[^1]; // wrap to end
        }

        var line = TextEditor.Document.GetLineByNumber(target);
        TextEditor.TextArea.Caret.Offset = line.Offset;
        TextEditor.TextArea.Caret.BringCaretToView();
        TextEditor.ScrollToLine(target);
        TextEditor.Focus();
    }

    // --- Bracket Matching ---
    private void OnCaretPositionChangedForBrackets(object? sender, EventArgs e)
    {
        if (_bracketRenderer == null) return;
        if (!GetCurrentFeatureGate().BracketMatchingEnabled)
        {
            _bracketRenderer.SetHighlight(null);
            return;
        }

        var result = BracketSearcher.FindMatchingBracket(TextEditor.Document, TextEditor.CaretOffset);
        _bracketRenderer.SetHighlight(result);
    }

    // --- Bookmarks ---
    private void OnToggleBookmark()
    {
        if (!IsActiveEditorHost() || !IsTextMode(_currentVm)) return;
        int line = TextEditor.TextArea.Caret.Line;
        if (_bookmarks.Contains(line))
            _bookmarks.Remove(line);
        else
            _bookmarks.Add(line);

        _bookmarks.Sort();
        SetStatus(_bookmarks.Contains(line)
            ? $"Bookmark set at line {line}"
            : $"Bookmark removed from line {line}");
    }

    private void OnNextBookmark()
    {
        if (!IsActiveEditorHost() || _bookmarks.Count == 0) return;
        int currentLine = TextEditor.TextArea.Caret.Line;
        var next = _bookmarks.FirstOrDefault(b => b > currentLine);
        if (next == 0) next = _bookmarks[0]; // wrap around
        GoToLine(next);
    }

    private void OnPrevBookmark()
    {
        if (!IsActiveEditorHost() || _bookmarks.Count == 0) return;
        int currentLine = TextEditor.TextArea.Caret.Line;
        var prev = _bookmarks.LastOrDefault(b => b < currentLine);
        if (prev == 0) prev = _bookmarks[^1]; // wrap around
        GoToLine(prev);
    }

    // --- Minimap ---
    private void UpdateMinimapVisibility(bool visible)
    {
        if (visible && IsTextMode(_currentVm) && GetCurrentFeatureGate().MinimapEnabled)
        {
            MinimapColumn.Width = new GridLength(100);
            DocumentMap.Visibility = Visibility.Visible;
            DocumentMap.AttachEditor(TextEditor);
        }
        else
        {
            MinimapColumn.Width = new GridLength(0);
            DocumentMap.Visibility = Visibility.Collapsed;
            DocumentMap.DetachEditor();
        }
    }

    // --- Auto-Reload / Log Tailing ---
    private void UpdateAutoReload(bool enabled)
    {
        if (!IsActiveEditorHost()) return;
        if (enabled && !string.IsNullOrEmpty(_currentVm?.FilePath))
        {
            _fileWatcher.StartWatching(_currentVm.FilePath);
        }
        else
        {
            _fileWatcher.StopWatching();
        }
    }

    private async void OnFileWatcherChanged(object? sender, string filePath)
    {
        // Small delay to let the writing process finish
        await Task.Delay(200);

        await Dispatcher.InvokeAsync(async () =>
        {
            if (_currentVm == null || _currentVm.FilePath != filePath) return;

            try
            {
                var content = await _fileSystemService!.ReadAllTextAsync(filePath);
                TextEditor.Text = content;
                // Scroll to bottom (tail mode)
                TextEditor.ScrollToEnd();

                SetStatus($"Auto-reloaded: {_fileSystemService.GetFileName(filePath)}");
            }
            catch (IOException ex)
            {
                Trace.TraceWarning($"Auto-reload skipped for '{filePath}': {ex.Message}");
                SetStatus($"Auto-reload skipped; file is unavailable: {_fileSystemService!.GetFileName(filePath)}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Trace.TraceWarning($"Auto-reload skipped for '{filePath}': {ex.Message}");
                SetStatus($"Auto-reload skipped; access denied: {_fileSystemService!.GetFileName(filePath)}");
            }
        });
    }

    private bool IsActiveEditorHost()
    {
        return _mainViewModel?.SelectedTab != null && _mainViewModel.SelectedTab == _currentVm;
    }

    private static bool IsBinaryMode([NotNullWhen(true)] EditorTabViewModel? vm) => vm?.Mode == FileOpenMode.Binary;

    private static bool IsTextMode([NotNullWhen(true)] EditorTabViewModel? vm) => vm?.Mode == FileOpenMode.Text;

    private EditorFeatureGate GetCurrentFeatureGate()
    {
        return _currentVm == null
            ? EditorFeatureGatePolicy.Create(FileOpenMode.Text, fileSize: 0)
            : EditorFeatureGatePolicy.Create(_currentVm.Mode, _currentVm.FileSize);
    }

    private bool CanUseTextFeature(Func<EditorFeatureGate, bool> isEnabled)
    {
        return IsActiveEditorHost() &&
               IsTextMode(_currentVm) &&
               isEnabled(GetCurrentFeatureGate());
    }

    private void OnThemeChanged(object? sender, ThemeDefinition theme)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyEditorThemeBrushes();
            ApplySearchPanelMarkerBrush();
            ApplySyntaxThemeColors(theme);
            HexEditor.OnThemeChanged();
        });
    }

    private void ApplySyntaxThemeColors(ThemeDefinition theme)
    {
        if (TextEditor.SyntaxHighlighting == null) return;

        var syntax = theme.SyntaxColors;
        var colorMap = BuildSyntaxColorMap(syntax);

        foreach (var namedColor in TextEditor.SyntaxHighlighting.NamedHighlightingColors)
        {
            if (colorMap.TryGetValue(namedColor.Name, out var hex))
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                namedColor.Foreground = new ICSharpCode.AvalonEdit.Highlighting.SimpleHighlightingBrush(color);
            }
        }

        // Force refresh
        TextEditor.SyntaxHighlighting = TextEditor.SyntaxHighlighting;
    }

    private static Dictionary<string, string> BuildSyntaxColorMap(SyntaxTheme syntax)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Universal
            ["Comment"] = syntax.Comment,
            ["String"] = syntax.String,
            ["Char"] = syntax.String,
            ["Character"] = syntax.String,
            ["NumberLiteral"] = syntax.Number,
            ["Digits"] = syntax.Number,
            ["Number"] = syntax.Number,
            ["Preprocessor"] = syntax.Preprocessor,
            ["MethodCall"] = syntax.Function,

            // C#
            ["ValueTypeKeywords"] = syntax.Keyword,
            ["ReferenceTypeKeywords"] = syntax.Type,
            ["Keywords"] = syntax.Keyword,
            ["GotoKeywords"] = syntax.Keyword,
            ["ContextKeywords"] = syntax.Keyword,
            ["ExceptionKeywords"] = syntax.Keyword,
            ["CheckedKeyword"] = syntax.Keyword,
            ["UnsafeKeywords"] = syntax.Keyword,
            ["OperatorKeywords"] = syntax.Operator,
            ["ParameterModifiers"] = syntax.Keyword,
            ["Modifiers"] = syntax.Keyword,
            ["Visibility"] = syntax.Keyword,
            ["NamespaceKeywords"] = syntax.Keyword,
            ["GetSetAddRemove"] = syntax.Keyword,
            ["TrueFalse"] = syntax.Constant,
            ["TypeKeywords"] = syntax.Type,
            ["SemanticKeywords"] = syntax.Keyword,
            ["NullOrValueKeywords"] = syntax.Constant,
            ["ThisOrBaseReference"] = syntax.Keyword,
            ["StringInterpolation"] = syntax.String,
            ["Punctuation"] = syntax.Operator,

            // JavaScript
            ["JavaScriptKeyWords"] = syntax.Keyword,
            ["JavaScriptIntrinsics"] = syntax.Type,
            ["JavaScriptLiterals"] = syntax.Constant,
            ["JavaScriptGlobalFunctions"] = syntax.Function,
            ["Regex"] = syntax.String,

            // JSON
            ["FieldName"] = syntax.Variable,
            ["Bool"] = syntax.Constant,
            ["Null"] = syntax.Constant,

            // Python
            // (Comment, String, MethodCall, NumberLiteral, Keywords already mapped)

            // XML / HTML
            ["XmlTag"] = syntax.Tag,
            ["HtmlTag"] = syntax.Tag,
            ["Tags"] = syntax.Tag,
            ["AttributeName"] = syntax.AttributeName,
            ["Attributes"] = syntax.AttributeName,
            ["AttributeValue"] = syntax.AttributeValue,
            ["CData"] = syntax.String,
            ["DocType"] = syntax.Preprocessor,
            ["XmlDeclaration"] = syntax.Preprocessor,
            ["Entity"] = syntax.Constant,
            ["BrokenEntity"] = syntax.Constant,
            ["EntityReference"] = syntax.Constant,
            ["Entities"] = syntax.Constant,
            ["ScriptTag"] = syntax.Tag,
            ["JavaScriptTag"] = syntax.Tag,
            ["JScriptTag"] = syntax.Tag,
            ["VBScriptTag"] = syntax.Tag,
            ["UnknownScriptTag"] = syntax.Tag,
            ["Slash"] = syntax.Tag,
            ["Assignment"] = syntax.Operator,
            ["UnknownAttribute"] = syntax.AttributeName,
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is EditorTabViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _currentVm = e.NewValue as EditorTabViewModel;

        if (_currentVm != null)
        {
            _currentVm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateEditor(_currentVm);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not EditorTabViewModel vm) return;

        if (e.PropertyName == nameof(EditorTabViewModel.Content))
        {
            if (IsTextMode(vm) && TextEditor.Text != vm.Content)
            {
                TextEditor.Text = vm.Content;
            }
        }
        else if (e.PropertyName == nameof(EditorTabViewModel.Mode))
        {
            UpdateEditor(vm);
        }
    }

    private void ApplyEditorThemeBrushes()
    {
        // Wire selection brushes
        if (TryFindResource("EditorSelectionBackgroundBrush") is Brush selBg)
            TextEditor.TextArea.SelectionBrush = selBg;
        if (TryFindResource("EditorSelectionForegroundBrush") is Brush selFg)
            TextEditor.TextArea.SelectionForeground = selFg;

        // Wire current-line highlight
        if (TryFindResource("EditorCurrentLineBackgroundBrush") is Brush lineBg)
        {
            TextEditor.TextArea.TextView.CurrentLineBackground = lineBg;
            TextEditor.TextArea.TextView.CurrentLineBorder = new Pen(lineBg, 0);
        }
    }

    private void ApplySearchPanelMarkerBrush()
    {
        if (_searchPanel == null) return;
        if (TryFindResource("AccentBrush") is SolidColorBrush accent)
        {
            var markerBrush = new SolidColorBrush(accent.Color) { Opacity = 0.3 };
            markerBrush.Freeze();
            _searchPanel.MarkerBrush = markerBrush;
        }
    }

    private void TextEditor_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            if (_mainViewModel == null) return;

            if (e.Delta > 0)
                _mainViewModel.ZoomInCommand.Execute(null);
            else
                _mainViewModel.ZoomOutCommand.Execute(null);

            e.Handled = true;
        }
    }

    private void UpdateEditor(EditorTabViewModel vm)
    {
        if (IsBinaryMode(vm))
            ShowBinaryEditor(vm);
        else if (!IsTextMode(vm))
            ShowNoEditor();
        else
            ShowTextEditor(vm);
    }

    private void ShowBinaryEditor(EditorTabViewModel vm)
    {
        TextEditor.Visibility = Visibility.Collapsed;
        HexEditor.Visibility = Visibility.Visible;
        HexEditor.DataContext = vm;

        Panel.SetZIndex(HexEditor, 1);
        Panel.SetZIndex(TextEditor, 0);
        DisableTextEditorFeatures();
    }

    private void ShowNoEditor()
    {
        TextEditor.Visibility = Visibility.Collapsed;
        HexEditor.Visibility = Visibility.Collapsed;

        Panel.SetZIndex(TextEditor, 0);
        Panel.SetZIndex(HexEditor, 0);
        DisableTextEditorFeatures();
    }

    private void DisableTextEditorFeatures()
    {
        UninstallFolding();
        DocumentMap.DetachEditor();
        _fileWatcher.StopWatching();
    }

    private void ShowTextEditor(EditorTabViewModel vm)
    {
        HexEditor.Visibility = Visibility.Collapsed;
        TextEditor.Visibility = Visibility.Visible;

        Panel.SetZIndex(TextEditor, 1);
        Panel.SetZIndex(HexEditor, 0);

        TextEditor.Text = vm.Content;
        ApplyIndentSettings(vm);
        AttachTextEditorHandlers();

        var featureGate = EditorFeatureGatePolicy.Create(vm.Mode, vm.FileSize);
        ApplyTextFeatureGate(vm, featureGate);
        UpdateAutoReloadForTab(vm);
        _bookmarks.Clear();
    }

    private void ApplyIndentSettings(EditorTabViewModel vm)
    {
        var indentResult = IndentDetector.Detect(vm.Content);
        TextEditor.Options.ConvertTabsToSpaces = !indentResult.UseTabs;
        TextEditor.Options.IndentationSize = indentResult.IndentSize;
        vm.IndentInfo = indentResult.UseTabs ? "Tabs" : $"Spaces: {indentResult.IndentSize}";
    }

    private void AttachTextEditorHandlers()
    {
        TextEditor.TextChanged -= TextEditor_TextChanged;
        TextEditor.TextChanged += TextEditor_TextChanged;
        TextEditor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        TextEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
    }

    private void ApplyTextFeatureGate(EditorTabViewModel vm, EditorFeatureGate featureGate)
    {
        TextEditor.SyntaxHighlighting = featureGate.SyntaxHighlightingEnabled
            ? GetHighlightingForLanguage(vm.SyntaxLanguage)
            : null;

        if (featureGate.SyntaxHighlightingEnabled && _themeService?.CurrentTheme != null)
            ApplySyntaxThemeColors(_themeService.CurrentTheme);

        ApplyEditorThemeBrushes();
        UpdateIndentGuides(_mainViewModel?.IsIndentGuidesEnabled == true);
        UpdateCodeFolding(featureGate);
        UpdateMinimapVisibility(_mainViewModel?.IsMinimapVisible == true && featureGate.MinimapEnabled);
        ClearDisabledFeatureRenderers(featureGate);
    }

    private void UpdateCodeFolding(EditorFeatureGate featureGate)
    {
        if (_mainViewModel?.IsFoldingEnabled == true && featureGate.FoldingEnabled)
            InstallFolding();
    }

    private void ClearDisabledFeatureRenderers(EditorFeatureGate featureGate)
    {
        if (!featureGate.OccurrenceHighlightingEnabled)
            _occurrenceRenderer?.Clear();

        if (!featureGate.BracketMatchingEnabled)
            _bracketRenderer?.SetHighlight(null);
    }

    private void UpdateAutoReloadForTab(EditorTabViewModel vm)
    {
        if (_mainViewModel?.IsAutoReloadEnabled == true && !string.IsNullOrEmpty(vm.FilePath))
            _fileWatcher.StartWatching(vm.FilePath);
        else
            _fileWatcher.StopWatching();
    }

    private void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        var vm = _currentVm;
        if (IsTextMode(vm))
        {
            vm.Content = TextEditor.Text;

            // Refresh filter cache if filters are active
            if (_lineFilterService?.HasActiveFilters == true)
            {
                RecomputeFilterCache();
                if (_isFilterFoldingActive)
                    ApplyFilterFolding();
            }
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.Line = TextEditor.TextArea.Caret.Line;
            _currentVm.Column = TextEditor.TextArea.Caret.Column;
        }

        // Debounce breadcrumb update
        _breadcrumbTimer?.Stop();
        _breadcrumbTimer?.Start();
    }

    // --- Occurrence Highlight ---
    private void OnSelectionChangedForOccurrences(object? sender, EventArgs e)
    {
        if (_occurrenceRenderer == null || !IsActiveEditorHost()) return;
        if (!GetCurrentFeatureGate().OccurrenceHighlightingEnabled)
        {
            _occurrenceRenderer.Clear();
            return;
        }

        var selectedText = TextEditor.SelectedText?.Trim();
        if (!string.IsNullOrEmpty(selectedText) && !selectedText.Contains('\n'))
        {
            _occurrenceRenderer.SetHighlightWord(selectedText, TextEditor.Document);
        }
        else
        {
            _occurrenceRenderer.Clear();
        }
    }

    // --- Auto-Complete ---
    private void OnShowCompletion()
    {
        var vm = _currentVm;
        if (vm == null || !CanUseTextFeature(gate => gate.CompletionEnabled)) return;

        var completions = CompletionHelper.GetCompletions(
            vm.SyntaxLanguage,
            TextEditor.Document,
            TextEditor.CaretOffset);

        if (completions.Count == 0) return;

        _completionWindow = new CompletionWindow(TextEditor.TextArea);
        foreach (var item in completions)
            _completionWindow.CompletionList.CompletionData.Add(item);

        _completionWindow.Show();
        _completionWindow.Closed += (s, e) => _completionWindow = null;
    }

    // --- Split View ---
    private void OnToggleSplitView()
    {
        if (!IsActiveEditorHost()) return;

        if (SplitEditor != null && SplitEditor.Visibility == Visibility.Visible)
        {
            SplitEditor.Visibility = Visibility.Collapsed;
            SplitSplitter.Visibility = Visibility.Collapsed;
            SplitRow.Height = new GridLength(0);
        }
        else if (SplitEditor != null)
        {
            // Share document between editors
            SplitEditor.Document = TextEditor.Document;
            SplitEditor.SyntaxHighlighting = TextEditor.SyntaxHighlighting;
            SplitEditor.FontSize = TextEditor.FontSize;
            SplitEditor.FontFamily = TextEditor.FontFamily;
            SplitEditor.ShowLineNumbers = true;
            SplitEditor.WordWrap = TextEditor.WordWrap;
            SplitEditor.Visibility = Visibility.Visible;
            SplitSplitter.Visibility = Visibility.Visible;
            SplitRow.Height = new GridLength(1, GridUnitType.Star);
        }
    }

    // --- Indent Guides ---
    private void UpdateIndentGuides(bool enabled)
    {
        if (_indentGuideRenderer == null) return;
        var effectiveEnabled = enabled && IsTextMode(_currentVm) && GetCurrentFeatureGate().IndentGuidesEnabled;

        if (effectiveEnabled)
        {
            if (!TextEditor.TextArea.TextView.BackgroundRenderers.Contains(_indentGuideRenderer))
                TextEditor.TextArea.TextView.BackgroundRenderers.Add(_indentGuideRenderer);
        }
        else
        {
            TextEditor.TextArea.TextView.BackgroundRenderers.Remove(_indentGuideRenderer);
        }
        TextEditor.TextArea.TextView.InvalidateLayer(_indentGuideRenderer.Layer);
    }

    // --- Breadcrumb Bar ---
    private void UpdateBreadcrumbs()
    {
        var vm = _currentVm;
        if (_mainViewModel?.IsBreadcrumbVisible != true || !IsTextMode(vm))
        {
            BreadcrumbBar.Visibility = Visibility.Collapsed;
            return;
        }

        var text = TextEditor.Text;
        var caretLine = TextEditor.TextArea.Caret.Line;
        var language = vm.SyntaxLanguage;

        var items = BreadcrumbHelper.GetBreadcrumbs(text, caretLine, language);

        if (items.Count == 0)
        {
            BreadcrumbBar.Visibility = Visibility.Collapsed;
            return;
        }

        var displayItems = new List<BreadcrumbDisplayItem>();
        for (int i = 0; i < items.Count; i++)
        {
            displayItems.Add(new BreadcrumbDisplayItem
            {
                Name = items[i].Name,
                Kind = items[i].Kind,
                Separator = "›",
                SeparatorVisibility = i == 0 ? Visibility.Collapsed : Visibility.Visible,
                Line = items[i].Line
            });
        }

        BreadcrumbItems.ItemsSource = displayItems;
        BreadcrumbBar.Visibility = Visibility.Visible;
    }

    private void UpdateBreadcrumbVisibility(bool visible)
    {
        if (visible)
            UpdateBreadcrumbs();
        else
            BreadcrumbBar.Visibility = Visibility.Collapsed;
    }

    private void Breadcrumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb && tb.DataContext is BreadcrumbDisplayItem item)
        {
            GoToLine(item.Line);
        }
    }

    // --- Session State Save ---
    public void SaveStateToViewModel()
    {
        if (_currentVm == null) return;
        _currentVm.CursorOffset = TextEditor.CaretOffset;
        _currentVm.ScrollOffset = TextEditor.VerticalOffset;
    }

    public void RestoreStateFromViewModel()
    {
        if (_currentVm == null) return;

        if (_currentVm.CursorOffset > 0 && _currentVm.CursorOffset <= TextEditor.Document.TextLength)
        {
            TextEditor.CaretOffset = _currentVm.CursorOffset;
        }
        if (_currentVm.ScrollOffset > 0)
        {
            TextEditor.ScrollToVerticalOffset(_currentVm.ScrollOffset);
        }
    }

    private static IHighlightingDefinition? GetHighlightingForLanguage(string language)
    {
        var definitionName = HighlightingDefinitionNameResolver.Resolve(language);
        return definitionName == null ? null : HighlightingManager.Instance.GetDefinition(definitionName);
    }
}
