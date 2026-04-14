using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FastEdit.Helpers;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;
using FastEdit.ViewModels;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Extensions.DependencyInjection;

namespace FastEdit.Views.Controls;

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

    public EditorHost()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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

        // Install indent guide renderer
        _indentGuideRenderer = new IndentGuideRenderer(TextEditor.TextArea.TextView);
        TextEditor.TextArea.TextView.BackgroundRenderers.Add(_indentGuideRenderer);

        // Install occurrence highlight renderer
        _occurrenceRenderer = new OccurrenceHighlightRenderer(TextEditor.TextArea.TextView);
        TextEditor.TextArea.TextView.BackgroundRenderers.Add(_occurrenceRenderer);
        TextEditor.TextArea.SelectionChanged += OnSelectionChangedForOccurrences;

        // Folding update timer
        _foldingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _foldingTimer.Tick += (s, args) => UpdateFoldings();
        _foldingTimer.Start();

        // File watcher for auto-reload
        _fileWatcher.FileChanged += OnFileWatcherChanged;

        // Subscribe to theme changes
        var themeService = App.Services.GetService<IThemeService>();
        if (themeService != null)
        {
            themeService.ThemeChanged += OnThemeChanged;
        }

        // Subscribe to ViewModel events for editor actions
        var mainVm = App.Services.GetService<MainViewModel>();
        if (mainVm != null)
        {
            mainVm.FindRequested += OnFindRequested;
            mainVm.ReplaceRequested += OnReplaceRequested;
            mainVm.GoToLineRequested += OnGoToLineRequested;
            mainVm.DuplicateLineRequested += OnDuplicateLineRequested;
            mainVm.MoveLineRequested += OnMoveLineRequested;
            mainVm.FormatDocumentRequested += OnFormatDocumentRequested;
            mainVm.MinifyDocumentRequested += OnMinifyDocumentRequested;
            mainVm.ToggleBookmarkRequested += OnToggleBookmark;
            mainVm.NextBookmarkRequested += OnNextBookmark;
            mainVm.PrevBookmarkRequested += OnPrevBookmark;
            mainVm.ShowCompletionRequested += OnShowCompletion;
            mainVm.ToggleSplitViewRequested += OnToggleSplitView;
            mainVm.PropertyChanged += OnMainVmPropertyChanged;
        }

        if (_currentVm != null)
        {
            UpdateEditor(_currentVm);
        }
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
                if (vm.IsFoldingEnabled)
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
        }
    }

    private void ApplyEditorSettings()
    {
        var mainVm = App.Services.GetService<MainViewModel>();
        if (mainVm == null) return;

        TextEditor.WordWrap = mainVm.IsWordWrapEnabled;
        TextEditor.FontSize = mainVm.EditorFontSize;
        TextEditor.Options.ShowTabs = mainVm.IsWhitespaceVisible;
        TextEditor.Options.ShowSpaces = mainVm.IsWhitespaceVisible;
        TextEditor.Options.ShowEndOfLine = mainVm.IsWhitespaceVisible;
    }

    // --- Find & Replace ---
    private void OnFindRequested()
    {
        if (!IsActiveEditorHost()) return;
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

        var mainVm = App.Services.GetService<MainViewModel>();
        if (mainVm != null)
            mainVm.StatusText = $"Replaced {count} occurrence(s)";
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
        if (!IsActiveEditorHost() || _currentVm?.IsBinaryMode == true) return;
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
        if (!IsActiveEditorHost() || _currentVm?.IsBinaryMode == true) return;
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
        if (!IsActiveEditorHost() || _currentVm?.IsBinaryMode == true) return;
        var lang = _currentVm?.SyntaxLanguage ?? "";

        (string result, string? error) output;
        if (FormatHelper.IsJsonLanguage(lang))
            output = FormatHelper.PrettyPrintJson(TextEditor.Text);
        else if (FormatHelper.IsXmlLanguage(lang))
            output = FormatHelper.PrettyPrintXml(TextEditor.Text);
        else return;

        if (output.error != null)
        {
            var mainVm = App.Services.GetService<MainViewModel>();
            if (mainVm != null) mainVm.StatusText = output.error;
            return;
        }
        TextEditor.Document.Text = output.result;
    }

    private void OnMinifyDocumentRequested()
    {
        if (!IsActiveEditorHost() || _currentVm?.IsBinaryMode == true) return;
        var lang = _currentVm?.SyntaxLanguage ?? "";

        (string result, string? error) output;
        if (FormatHelper.IsJsonLanguage(lang))
            output = FormatHelper.MinifyJson(TextEditor.Text);
        else if (FormatHelper.IsXmlLanguage(lang))
            output = FormatHelper.MinifyXml(TextEditor.Text);
        else return;

        if (output.error != null)
        {
            var mainVm = App.Services.GetService<MainViewModel>();
            if (mainVm != null) mainVm.StatusText = output.error;
            return;
        }
        TextEditor.Document.Text = output.result;
    }

    // --- Code Folding ---
    private void InstallFolding()
    {
        if (_currentVm == null || _currentVm.IsBinaryMode) return;
        _foldingManager = FoldingHelper.Install(TextEditor, _currentVm.SyntaxLanguage);
    }

    private void UninstallFolding()
    {
        FoldingHelper.Uninstall(TextEditor);
        _foldingManager = null;
    }

    private void UpdateFoldings()
    {
        if (_foldingManager == null || _currentVm == null) return;
        FoldingHelper.Update(_foldingManager, _currentVm.SyntaxLanguage, TextEditor.Document);
    }

    // --- Bracket Matching ---
    private void OnCaretPositionChangedForBrackets(object? sender, EventArgs e)
    {
        if (_bracketRenderer == null) return;
        var result = BracketSearcher.FindMatchingBracket(TextEditor.Document, TextEditor.CaretOffset);
        _bracketRenderer.SetHighlight(result);
    }

    // --- Bookmarks ---
    private void OnToggleBookmark()
    {
        if (!IsActiveEditorHost() || _currentVm?.IsBinaryMode == true) return;
        int line = TextEditor.TextArea.Caret.Line;
        if (_bookmarks.Contains(line))
            _bookmarks.Remove(line);
        else
            _bookmarks.Add(line);

        _bookmarks.Sort();
        var mainVm = App.Services.GetService<MainViewModel>();
        if (mainVm != null)
            mainVm.StatusText = _bookmarks.Contains(line)
                ? $"Bookmark set at line {line}"
                : $"Bookmark removed from line {line}";
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
        if (visible && !_currentVm?.IsBinaryMode == true)
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
                var content = await System.IO.File.ReadAllTextAsync(filePath);
                TextEditor.Text = content;
                // Scroll to bottom (tail mode)
                TextEditor.ScrollToEnd();

                var mainVm = App.Services.GetService<MainViewModel>();
                if (mainVm != null)
                    mainVm.StatusText = $"Auto-reloaded: {System.IO.Path.GetFileName(filePath)}";
            }
            catch { /* file may be locked */ }
        });
    }

    private bool IsActiveEditorHost()
    {
        var mainVm = App.Services.GetService<MainViewModel>();
        return mainVm?.SelectedTab != null && mainVm.SelectedTab == _currentVm;
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
            if (!vm.IsBinaryMode && TextEditor.Text != vm.Content)
            {
                TextEditor.Text = vm.Content;
            }
        }
        else if (e.PropertyName == nameof(EditorTabViewModel.IsBinaryMode))
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
            var mainVm = App.Services.GetService<MainViewModel>();
            if (mainVm == null) return;

            if (e.Delta > 0)
                mainVm.ZoomInCommand.Execute(null);
            else
                mainVm.ZoomOutCommand.Execute(null);

            e.Handled = true;
        }
    }

    private void UpdateEditor(EditorTabViewModel vm)
    {
        if (vm.IsBinaryMode)
        {
            TextEditor.Visibility = Visibility.Collapsed;
            HexEditor.Visibility = Visibility.Visible;
            HexEditor.DataContext = vm;

            Panel.SetZIndex(HexEditor, 1);
            Panel.SetZIndex(TextEditor, 0);

            UninstallFolding();
            DocumentMap.DetachEditor();
            _fileWatcher.StopWatching();
        }
        else
        {
            HexEditor.Visibility = Visibility.Collapsed;
            TextEditor.Visibility = Visibility.Visible;

            Panel.SetZIndex(TextEditor, 1);
            Panel.SetZIndex(HexEditor, 0);

            TextEditor.Text = vm.Content;

            // Remove old handler to prevent duplicates
            TextEditor.TextChanged -= TextEditor_TextChanged;
            TextEditor.TextChanged += TextEditor_TextChanged;

            // Apply syntax highlighting
            var highlighting = GetHighlightingForLanguage(vm.SyntaxLanguage);
            TextEditor.SyntaxHighlighting = highlighting;

            // Apply theme syntax colors on top of default highlighting
            var themeService = App.Services.GetService<IThemeService>();
            if (themeService?.CurrentTheme != null)
            {
                ApplySyntaxThemeColors(themeService.CurrentTheme);
            }

            // Track caret position - remove old handler first
            TextEditor.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
            TextEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;

            ApplyEditorThemeBrushes();

            // Install code folding
            var mainVm = App.Services.GetService<MainViewModel>();
            if (mainVm?.IsFoldingEnabled == true)
                InstallFolding();

            // Minimap
            if (mainVm?.IsMinimapVisible == true)
                UpdateMinimapVisibility(true);

            // Auto-reload
            if (mainVm?.IsAutoReloadEnabled == true && !string.IsNullOrEmpty(vm.FilePath))
                _fileWatcher.StartWatching(vm.FilePath);
            else
                _fileWatcher.StopWatching();

            // Clear bookmarks on tab switch
            _bookmarks.Clear();
        }
    }

    private void TextEditor_TextChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null && !_currentVm.IsBinaryMode)
        {
            _currentVm.Content = TextEditor.Text;
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (_currentVm != null)
        {
            _currentVm.Line = TextEditor.TextArea.Caret.Line;
            _currentVm.Column = TextEditor.TextArea.Caret.Column;
        }
    }

    // --- Occurrence Highlight ---
    private void OnSelectionChangedForOccurrences(object? sender, EventArgs e)
    {
        if (_occurrenceRenderer == null || !IsActiveEditorHost()) return;

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
        if (!IsActiveEditorHost() || _currentVm == null) return;

        var completions = CompletionHelper.GetCompletions(
            _currentVm.SyntaxLanguage,
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

        if (enabled)
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
        if (string.IsNullOrEmpty(language))
            return null;

        return language switch
        {
            "C#" => HighlightingManager.Instance.GetDefinition("C#"),
            "JavaScript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            "TypeScript" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            "Python" => HighlightingManager.Instance.GetDefinition("Python"),
            "Java" => HighlightingManager.Instance.GetDefinition("Java"),
            "C++" => HighlightingManager.Instance.GetDefinition("C++"),
            "C" => HighlightingManager.Instance.GetDefinition("C++"),
            "HTML" => HighlightingManager.Instance.GetDefinition("HTML"),
            "CSS" => HighlightingManager.Instance.GetDefinition("CSS"),
            "XML" => HighlightingManager.Instance.GetDefinition("XML"),
            "JSON" => HighlightingManager.Instance.GetDefinition("Json"),
            "SQL" => HighlightingManager.Instance.GetDefinition("TSQL"),
            "PowerShell" => HighlightingManager.Instance.GetDefinition("PowerShell"),
            "Markdown" => HighlightingManager.Instance.GetDefinition("MarkDown"),
            // Custom .xshd definitions
            "YAML" => HighlightingManager.Instance.GetDefinition("YAML"),
            "Shell" => HighlightingManager.Instance.GetDefinition("Bash"),
            "Dockerfile" => HighlightingManager.Instance.GetDefinition("Dockerfile"),
            "Rust" => HighlightingManager.Instance.GetDefinition("Rust"),
            "Go" => HighlightingManager.Instance.GetDefinition("Go"),
            "TOML" => HighlightingManager.Instance.GetDefinition("TOML"),
            "INI" => HighlightingManager.Instance.GetDefinition("INI"),
            _ => null
        };
    }
}
