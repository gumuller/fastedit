using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Core.HexEngine;
using FastEdit.Helpers;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;

namespace FastEdit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const int MaxPersistedBinaryModifications = 1_000_000;

    private readonly IFileService _fileService;
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IEditorTabFactory _tabFactory;
    private readonly IWorkspaceService _workspaceService;
    private readonly HashSet<EditorTabViewModel> _shutdownDiscardedTabs = new();
    private readonly List<SessionFile> _unresolvedSessionFiles = new();
    private string? _startupActiveSessionEntryId;

    public bool HasUnresolvedSessionEntries => _unresolvedSessionFiles.Count > 0;
    private bool _isInitializing = true;

    [ObservableProperty]
    private ObservableCollection<EditorTabViewModel> _tabs = new();

    [ObservableProperty]
    private EditorTabViewModel? _selectedTab;

    [ObservableProperty]
    private FileTreeViewModel _fileTree;

    [ObservableProperty]
    private string _currentThemeName = "Dark";

    [ObservableProperty]
    private IReadOnlyList<ThemeDefinition> _availableThemes;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ObservableCollection<string> _recentFiles = new();

    [ObservableProperty]
    private bool _isWordWrapEnabled;

    [ObservableProperty]
    private bool _isWhitespaceVisible;

    [ObservableProperty]
    private double _editorFontSize = 14;

    [ObservableProperty]
    private bool _isFoldingEnabled = true;

    [ObservableProperty]
    private bool _isMinimapVisible;

    [ObservableProperty]
    private bool _isAutoReloadEnabled;

    [ObservableProperty]
    private string _lineEnding = "CRLF";

    [ObservableProperty]
    private bool _isIndentGuidesEnabled = true;

    [ObservableProperty]
    private bool _isBreadcrumbVisible = true;

    [ObservableProperty]
    private bool _isCommandRunnerVisible;

    [ObservableProperty]
    private bool _isExplorerVisible = true;

    [ObservableProperty]
    private bool _isZenMode;

    [ObservableProperty]
    private string _gitBranch = "";

    [ObservableProperty]
    private EditorTabViewModel? _secondaryTab;

    [ObservableProperty]
    private bool _isSideBySideMode;

    [ObservableProperty]
    private bool _isFilterPanelVisible;

    // Events for editor-specific actions
    public event Action? FindRequested;
    public event Action? ReplaceRequested;
    public event Action<int>? GoToLineRequested;
    public event Action? DuplicateLineRequested;
    public event Action<bool>? MoveLineRequested; // true = up
    public event Action? FormatDocumentRequested;
    public event Action? MinifyDocumentRequested;
    public event Action? ToggleBookmarkRequested;
    public event Action? NextBookmarkRequested;
    public event Action? PrevBookmarkRequested;
    public event Action? FindInFilesRequested;
    public event Action? CompareFilesRequested;
    public event Action? CommandPaletteRequested;
    public event Action? ShowCompletionRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ToggleSplitViewRequested;
    public event Action<TextToolOperation>? TextToolRequested;
    public event Action? PrintRequested;
    public event Action? SelectNextOccurrenceRequested;
    public event Action? SelectAllOccurrencesRequested;
    public event Action? MacroStartRecordingRequested;
    public event Action? MacroStopRecordingRequested;
    public event Action<int>? MacroPlaybackRequested;
    public event Action? ToggleFilterPanelRequested;

    public MainViewModel(
        IFileService fileService,
        IThemeService themeService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory,
        IWorkspaceService workspaceService,
        FileTreeViewModel fileTree)
    {
        _fileService = fileService;
        _themeService = themeService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _fileSystemService = fileSystemService;
        _tabFactory = tabFactory;
        _workspaceService = workspaceService;
        _fileTree = fileTree;
        _availableThemes = themeService.AvailableThemes;
        _currentThemeName = themeService.CurrentTheme?.Name ?? "Dark";

        // Restore settings
        _isWordWrapEnabled = settingsService.WordWrapEnabled;
        _isWhitespaceVisible = settingsService.ShowWhitespace;
        _editorFontSize = settingsService.EditorFontSize > 0 ? settingsService.EditorFontSize : 14;

        foreach (var path in settingsService.RecentFiles)
            _recentFiles.Add(path);

        // Wire up file tree events
        FileTree.FileOpenRequested += OnFileOpenRequested;

        _isInitializing = false;
    }

    private async void OnFileOpenRequested(object? sender, string filePath)
    {
        await OpenFileAsync(filePath);
    }

    [RelayCommand]
    private async Task OpenFileAsync(string? filePath = null)
    {
        filePath ??= _dialogService.ShowOpenFileDialog(filter: EditorTabViewModel.FileDialogFilters);
        if (string.IsNullOrEmpty(filePath)) return;
        filePath = NormalizeFilePath(filePath);

        var existingTab = Tabs.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.FilePath) &&
            HasSameOpenIdentity(t.FilePath, filePath));
        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return;
        }

        try
        {
            var tab = _tabFactory.Create();
            var fileName = Path.GetFileName(filePath);
            var indexProgress = new Progress<double>(progress =>
                StatusText = EditorStatusFormatter.FormatLargeFileIndexingStatus(fileName, progress));
            await tab.LoadFileAsync(filePath, indexProgress);

            Tabs.Add(tab);
            SelectedTab = tab;

            AddToRecentFiles(filePath);
            StatusText = tab.Mode == FileOpenMode.LargeText
                ? EditorStatusFormatter.FormatTabStatus(tab)
                : $"Opened: {fileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedTab == null) return;

        try
        {
            if (SelectedTab.Mode == FileOpenMode.LargeText)
            {
                StatusText = "Large file viewer is read-only; original file unchanged.";
                return;
            }

            await SelectedTab.SaveCommand.ExecuteAsync(null);
            StatusText = $"Saved: {SelectedTab.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        if (SelectedTab == null) return;

        try
        {
            await SelectedTab.SaveAsCommand.ExecuteAsync(null);
            StatusText = $"Saved: {SelectedTab.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Error saving: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Find() => FindRequested?.Invoke();

    [RelayCommand]
    private void Replace() => ReplaceRequested?.Invoke();

    [RelayCommand]
    private void Print() => PrintRequested?.Invoke();

    [RelayCommand]
    private void SelectNextOccurrence()
    {
        if (!IsSelectedTabFeatureEnabled(gate => gate.OccurrenceHighlightingEnabled))
            return;

        SelectNextOccurrenceRequested?.Invoke();
    }

    [RelayCommand]
    private void SelectAllOccurrences()
    {
        if (!IsSelectedTabFeatureEnabled(gate => gate.OccurrenceHighlightingEnabled))
            return;

        SelectAllOccurrencesRequested?.Invoke();
    }

    [RelayCommand]
    private void MacroStartRecording() => MacroStartRecordingRequested?.Invoke();

    [RelayCommand]
    private void MacroStopRecording() => MacroStopRecordingRequested?.Invoke();

    [RelayCommand]
    private void MacroPlayback() => MacroPlaybackRequested?.Invoke(1);

    [RelayCommand]
    private void MacroPlaybackMultiple() => MacroPlaybackRequested?.Invoke(0); // 0 = prompt for count

    [RelayCommand]
    private void GoToLine()
    {
        if (SelectedTab == null) return;
        if (!IsTextCommandAvailable()) return;
        GoToLineRequested?.Invoke(SelectedTab.Line);
    }

    [RelayCommand]
    private void ToggleWordWrap() => IsWordWrapEnabled = !IsWordWrapEnabled;

    partial void OnIsWordWrapEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _settingsService.WordWrapEnabled = value;
        StatusText = value ? "Word Wrap: On" : "Word Wrap: Off";
    }

    [RelayCommand]
    private void ToggleWhitespace() => IsWhitespaceVisible = !IsWhitespaceVisible;

    partial void OnIsWhitespaceVisibleChanged(bool value)
    {
        if (_isInitializing) return;
        _settingsService.ShowWhitespace = value;
        StatusText = value ? "Whitespace: Visible" : "Whitespace: Hidden";
    }

    [RelayCommand]
    private void ZoomIn()
    {
        EditorFontSize = Math.Min(EditorFontSize + 2, 72);
        _settingsService.EditorFontSize = EditorFontSize;
        StatusText = $"Zoom: {EditorFontSize:0}pt";
    }

    [RelayCommand]
    private void ZoomOut()
    {
        EditorFontSize = Math.Max(EditorFontSize - 2, 8);
        _settingsService.EditorFontSize = EditorFontSize;
        StatusText = $"Zoom: {EditorFontSize:0}pt";
    }

    [RelayCommand]
    private void ResetZoom()
    {
        EditorFontSize = 14;
        _settingsService.EditorFontSize = 14;
        StatusText = "Zoom: Reset";
    }

    [RelayCommand]
    private void DuplicateLine()
    {
        if (IsTextCommandAvailable())
            DuplicateLineRequested?.Invoke();
    }

    [RelayCommand]
    private void MoveLineUp()
    {
        if (IsTextCommandAvailable())
            MoveLineRequested?.Invoke(true);
    }

    [RelayCommand]
    private void MoveLineDown()
    {
        if (IsTextCommandAvailable())
            MoveLineRequested?.Invoke(false);
    }

    [RelayCommand]
    private void FormatDocument()
    {
        if (IsTextCommandAvailable())
            FormatDocumentRequested?.Invoke();
    }

    [RelayCommand]
    private void MinifyDocument()
    {
        if (IsTextCommandAvailable())
            MinifyDocumentRequested?.Invoke();
    }

    [RelayCommand] private void TextToUpperCase() => RequestTextTool(TextToolOperation.UpperCase);
    [RelayCommand] private void TextToLowerCase() => RequestTextTool(TextToolOperation.LowerCase);
    [RelayCommand] private void TextToTitleCase() => RequestTextTool(TextToolOperation.TitleCase);
    [RelayCommand] private void TextInvertCase() => RequestTextTool(TextToolOperation.InvertCase);
    [RelayCommand] private void TextRemoveDuplicateLines() => RequestTextTool(TextToolOperation.RemoveDuplicateLines);
    [RelayCommand] private void TextSortLinesAsc() => RequestTextTool(TextToolOperation.SortLinesAsc);
    [RelayCommand] private void TextSortLinesDesc() => RequestTextTool(TextToolOperation.SortLinesDesc);
    [RelayCommand] private void TextTrimTrailing() => RequestTextTool(TextToolOperation.TrimTrailing);
    [RelayCommand] private void TextTrimLeading() => RequestTextTool(TextToolOperation.TrimLeading);
    [RelayCommand] private void TextTrimAll() => RequestTextTool(TextToolOperation.TrimAll);
    [RelayCommand] private void TextTabsToSpaces() => RequestTextTool(TextToolOperation.TabsToSpaces);
    [RelayCommand] private void TextSpacesToTabs() => RequestTextTool(TextToolOperation.SpacesToTabs);
    [RelayCommand] private void TextBase64Encode() => RequestTextTool(TextToolOperation.Base64Encode);
    [RelayCommand] private void TextBase64Decode() => RequestTextTool(TextToolOperation.Base64Decode);
    [RelayCommand] private void TextUrlEncode() => RequestTextTool(TextToolOperation.UrlEncode);
    [RelayCommand] private void TextUrlDecode() => RequestTextTool(TextToolOperation.UrlDecode);
    [RelayCommand] private void TextChecksumMd5() => RequestTextTool(TextToolOperation.ComputeMd5);
    [RelayCommand] private void TextChecksumSha1() => RequestTextTool(TextToolOperation.ComputeSha1);
    [RelayCommand] private void TextChecksumSha256() => RequestTextTool(TextToolOperation.ComputeSha256);
    [RelayCommand] private void TextChecksumSha512() => RequestTextTool(TextToolOperation.ComputeSha512);

    [RelayCommand]
    private void ToggleFolding() => IsFoldingEnabled = !IsFoldingEnabled;

    partial void OnIsFoldingEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        StatusText = value ? "Code Folding: On" : "Code Folding: Off";
    }

    [RelayCommand]
    private void ToggleMinimap() => IsMinimapVisible = !IsMinimapVisible;

    partial void OnIsMinimapVisibleChanged(bool value)
    {
        if (_isInitializing) return;
        StatusText = value ? "Minimap: Visible" : "Minimap: Hidden";
    }

    [RelayCommand]
    private void ToggleAutoReload() => IsAutoReloadEnabled = !IsAutoReloadEnabled;

    partial void OnIsAutoReloadEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        StatusText = value ? "Auto-Reload: On" : "Auto-Reload: Off";
    }

    [RelayCommand]
    private void ToggleBookmark()
    {
        if (IsTextCommandAvailable())
            ToggleBookmarkRequested?.Invoke();
    }

    [RelayCommand]
    private void NextBookmark()
    {
        if (IsTextCommandAvailable())
            NextBookmarkRequested?.Invoke();
    }

    [RelayCommand]
    private void PrevBookmark()
    {
        if (IsTextCommandAvailable())
            PrevBookmarkRequested?.Invoke();
    }

    [RelayCommand]
    private void FindInFiles() => FindInFilesRequested?.Invoke();

    [RelayCommand]
    private void CompareFiles() => CompareFilesRequested?.Invoke();

    [RelayCommand]
    private void ToggleIndentGuides() => IsIndentGuidesEnabled = !IsIndentGuidesEnabled;

    partial void OnIsIndentGuidesEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        StatusText = value ? "Indent Guides: On" : "Indent Guides: Off";
    }

    [RelayCommand]
    private void ToggleBreadcrumb() => IsBreadcrumbVisible = !IsBreadcrumbVisible;

    partial void OnIsBreadcrumbVisibleChanged(bool value)
    {
        if (_isInitializing) return;
        StatusText = value ? "Breadcrumb Bar: Visible" : "Breadcrumb Bar: Hidden";
    }

    [RelayCommand]
    private void CommandPalette() => CommandPaletteRequested?.Invoke();

    [RelayCommand]
    private void ShowCompletion()
    {
        if (!IsSelectedTabFeatureEnabled(gate => gate.CompletionEnabled))
            return;

        ShowCompletionRequested?.Invoke();
    }

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke();

    [RelayCommand]
    private void ToggleCommandRunner() => IsCommandRunnerVisible = !IsCommandRunnerVisible;

    [RelayCommand]
    private void ToggleZenMode() => IsZenMode = !IsZenMode;

    [RelayCommand]
    private void ToggleExplorer() => IsExplorerVisible = !IsExplorerVisible;

    [RelayCommand]
    private void ToggleFilterPanel() => IsFilterPanelVisible = !IsFilterPanelVisible;

    partial void OnIsFilterPanelVisibleChanged(bool value)
    {
        ToggleFilterPanelRequested?.Invoke();
    }

    partial void OnIsCommandRunnerVisibleChanged(bool value)
    {
        if (_isInitializing) return;
        StatusText = value ? "Terminal: Visible" : "Terminal: Hidden";
    }

    [RelayCommand]
    private void ToggleSplitView()
    {
        if (IsTextCommandAvailable())
            ToggleSplitViewRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleSideBySide()
    {
        if (IsSideBySideMode)
        {
            CloseSideBySide();
        }
        else if (Tabs.Count >= 2)
        {
            var selectedIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : 0;
            var secondaryIndex = (selectedIndex + 1) % Tabs.Count;
            SecondaryTab = Tabs[secondaryIndex];
            IsSideBySideMode = true;
        }
    }

    public void OpenInSplitView(EditorTabViewModel tab)
    {
        SecondaryTab = tab;
        IsSideBySideMode = true;
    }

    public void CloseSideBySide()
    {
        IsSideBySideMode = false;
        SecondaryTab = null;
    }

    [RelayCommand]
    private void ConvertLineEndings(string? target)
    {
        if (string.IsNullOrEmpty(target)) return;
        if (SelectedTab == null) return;
        if (!IsTextCommandAvailable()) return;

        var targetType = target switch
        {
            "CRLF" => LineEndingType.CRLF,
            "LF" => LineEndingType.LF,
            "CR" => LineEndingType.CR,
            _ => LineEndingType.CRLF
        };

        SelectedTab.Content = LineEndingHelper.Convert(SelectedTab.Content, targetType);
        LineEnding = LineEndingHelper.ToDisplayString(targetType);
        StatusText = $"Converted to {target}";
    }

    [RelayCommand]
    private async Task ChangeEncodingAsync(string? encodingName)
    {
        if (SelectedTab == null || string.IsNullOrEmpty(SelectedTab.FilePath) || string.IsNullOrEmpty(encodingName))
            return;

        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            var encoding = System.Text.Encoding.GetEncoding(encodingName);
            var bytes = await _fileSystemService.ReadAllBytesAsync(SelectedTab.FilePath);
            var content = encoding.GetString(bytes);
            SelectedTab.Content = content;
            SelectedTab.Encoding = encoding.EncodingName;
            StatusText = $"Re-read with {encoding.EncodingName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Encoding error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenRecentFileAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        if (!_fileSystemService.FileExists(filePath))
        {
            RecentFiles.Remove(filePath);
            _settingsService.RecentFiles = RecentFiles.ToList();
            StatusText = "File not found — removed from recent list";
            return;
        }
        await OpenFileAsync(filePath);
    }

    [RelayCommand]
    private async Task CloseTabAsync(EditorTabViewModel? tab)
    {
        await CloseTabCoreAsync(tab);
    }

    public async Task CloseTabCoreAsync(EditorTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        if (tab.IsModified)
        {
            var result = _dialogService.ShowMessage(
                $"Do you want to save changes to '{tab.FileName}'?",
                "FastEdit - Unsaved Changes",
                DialogButtons.YesNoCancel,
                DialogIcon.Warning);

            if (result == Services.Interfaces.DialogResult.Cancel)
                return;

            if (result == Services.Interfaces.DialogResult.Yes)
            {
                try
                {
                    await tab.SaveCommand.ExecuteAsync(null);
                    if (tab.IsModified)
                        return;
                }
                catch (Exception ex)
                {
                    StatusText = $"Error saving {tab.FileName}: {ex.Message}";
                    return;
                }
            }
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

        if (SecondaryTab == tab)
            CloseSideBySide();

        if (Tabs.Count > 0)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
        else
        {
            SelectedTab = null;
        }
    }

    [RelayCommand]
    private void ChangeTheme(string themeName)
    {
        _themeService.ApplyTheme(themeName);
        _settingsService.ThemeName = themeName;
        CurrentThemeName = themeName;
    }

    [RelayCommand]
    private void RefreshThemes()
    {
        _themeService.RefreshCustomThemes();
        AvailableThemes = _themeService.AvailableThemes;
        StatusText = "Themes refreshed";
    }

    [RelayCommand]
    private void OpenFolder()
    {
        FileTree.OpenFolderCommand.Execute(null);
    }

    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogService.ShowFolderBrowserDialog();
        if (!string.IsNullOrEmpty(folder))
            FileTree.AddRootFolder(folder);
    }

    [RelayCommand]
    private void SaveSessionAs()
    {
        var name = _dialogService.ShowInputDialog("Save Session As", "Session name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var session = BuildSessionData();
        session.Name = name;
        try
        {
            _workspaceService.SaveNamedSession(name, session);
            StatusText = $"Session saved: {name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save session '{name}': {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadSession(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        SessionData? session;
        try
        {
            session = _workspaceService.LoadNamedSession(name);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load session '{name}': {ex.Message}";
            return;
        }

        if (session == null)
        {
            StatusText = $"Session not found: {name}";
            return;
        }

        if (!await ConfirmExitAsync())
            return;

        var workspaceSnapshot = Tabs.ToDictionary(tab => tab, tab => tab.UserMutationVersion);
        try
        {
            var replacementTabs = await StageSessionTabsAsync(session);
            if (HasWorkspaceChanged(workspaceSnapshot) && !await ConfirmExitAsync())
            {
                foreach (var tab in replacementTabs)
                    tab.Dispose();
                return;
            }

            ReplaceTabs(replacementTabs, session.ActiveTabIndex);
            StatusText = $"Session loaded: {name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load session '{name}': {ex.Message}";
        }
    }

    private bool HasWorkspaceChanged(
        IReadOnlyDictionary<EditorTabViewModel, long> workspaceSnapshot)
    {
        return Tabs.Count != workspaceSnapshot.Count ||
            Tabs.Any(tab =>
                !workspaceSnapshot.TryGetValue(tab, out var changeVersion) ||
                tab.UserMutationVersion != changeVersion);
    }

    [RelayCommand]
    private void SaveWorkspace()
    {
        var path = _dialogService.ShowSaveFileDialog("FastEdit Workspace|*.fastedit-workspace");
        if (string.IsNullOrEmpty(path)) return;

        var workspace = new WorkspaceData
        {
            Name = Path.GetFileNameWithoutExtension(path),
            RootFolders = FileTree.RootPaths.ToList()
        };
        try
        {
            _workspaceService.SaveWorkspace(path, workspace);
            StatusText = $"Workspace saved: {workspace.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save workspace: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenWorkspace()
    {
        var path = _dialogService.ShowOpenFileDialog("FastEdit Workspace|*.fastedit-workspace");
        if (string.IsNullOrEmpty(path)) return;

        WorkspaceData? workspace;
        try
        {
            workspace = _workspaceService.LoadWorkspace(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load workspace: {ex.Message}";
            return;
        }

        if (workspace == null)
        {
            StatusText = "Failed to load workspace";
            return;
        }

        FileTree.SetMultipleRoots(workspace.RootFolders);
        StatusText = $"Workspace loaded: {workspace.Name} ({workspace.RootFolders.Count} folders)";
    }

    private SessionData BuildSessionData()
    {
        var session = new SessionData();
        foreach (var tab in Tabs)
        {
            session.Files.Add(new SessionFileEntry
            {
                FilePath = string.IsNullOrEmpty(tab.FilePath) ? tab.FileName : tab.FilePath,
                IsUntitled = string.IsNullOrEmpty(tab.FilePath),
                Content = string.IsNullOrEmpty(tab.FilePath) ? tab.Content : null,
                CursorOffset = tab.CursorOffset,
                ScrollOffset = tab.ScrollOffset
            });
        }
        session.ActiveTabIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : 0;
        return session;
    }

    private async Task<List<EditorTabViewModel>> StageSessionTabsAsync(SessionData session)
    {
        var stagedTabs = new List<EditorTabViewModel>(session.Files.Count);
        try
        {
            foreach (var entry in session.Files)
            {
                EditorTabViewModel tab;
                if (entry.IsUntitled)
                {
                    tab = _tabFactory.CreateUntitled(entry.Content ?? string.Empty);
                    tab.FileName = Path.GetFileName(entry.FilePath);
                    stagedTabs.Add(tab);
                }
                else
                {
                    if (!_fileSystemService.FileExists(entry.FilePath))
                        throw new FileNotFoundException("Session file was not found.", entry.FilePath);

                    tab = _tabFactory.Create();
                    stagedTabs.Add(tab);
                    await tab.LoadFileAsync(entry.FilePath);
                }

                tab.CursorOffset = entry.CursorOffset;
                tab.ScrollOffset = entry.ScrollOffset;
            }

            return stagedTabs;
        }
        catch
        {
            foreach (var tab in stagedTabs)
                tab.Dispose();
            throw;
        }
    }

    private void ReplaceTabs(IReadOnlyList<EditorTabViewModel> replacementTabs, int activeTabIndex)
    {
        var previousTabs = Tabs.ToList();
        CloseSideBySide();
        SelectedTab = null;
        Tabs.Clear();
        foreach (var tab in replacementTabs)
            Tabs.Add(tab);

        if (Tabs.Count > 0)
            SelectedTab = Tabs[Math.Clamp(activeTabIndex, 0, Tabs.Count - 1)];

        foreach (var tab in previousTabs)
            tab.Dispose();
    }

    [RelayCommand]
    private void NewFile()
    {
        var tab = _tabFactory.CreateUntitled();
        tab.FileName = $"Untitled-{Tabs.Count + 1}";

        Tabs.Add(tab);
        SelectedTab = tab;
    }

    partial void OnSelectedTabChanged(EditorTabViewModel? oldValue, EditorTabViewModel? newValue)
    {
        if (oldValue != null)
            oldValue.PropertyChanged -= OnSelectedTabPropertyChanged;

        if (newValue != null)
        {
            newValue.PropertyChanged += OnSelectedTabPropertyChanged;
            UpdateStatusForTab(newValue);
            _ = UpdateGitBranchAsync(newValue.FilePath);
        }
        else
        {
            StatusText = "Ready";
            GitBranch = "";
        }
    }

    private void OnSelectedTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is EditorTabViewModel tab &&
            (e.PropertyName == nameof(EditorTabViewModel.Mode) ||
             e.PropertyName == nameof(EditorTabViewModel.FileSize) ||
             e.PropertyName == nameof(EditorTabViewModel.Line) ||
             e.PropertyName == nameof(EditorTabViewModel.Column)))
        {
            UpdateStatusForTab(tab);
        }
    }

    private void UpdateStatusForTab(EditorTabViewModel tab)
    {
        StatusText = EditorStatusFormatter.FormatTabStatus(tab);
        if (tab.Mode == FileOpenMode.Text)
        {
            LineEnding = LineEndingHelper.ToDisplayString(LineEndingHelper.Detect(tab.Content));
            return;
        }

        LineEnding = "";
    }

    private void RequestTextTool(TextToolOperation operation)
    {
        if (IsTextCommandAvailable())
            TextToolRequested?.Invoke(operation);
    }

    private bool IsTextCommandAvailable()
    {
        if (SelectedTab == null)
            return true;

        if (SelectedTab.Mode == FileOpenMode.Text)
            return true;

        StatusText = EditorStatusFormatter.FormatTextCommandUnavailable(SelectedTab.Mode);
        return false;
    }

    private bool IsSelectedTabFeatureEnabled(Func<EditorFeatureGate, bool> isEnabled)
    {
        if (SelectedTab == null)
            return false;

        var gate = EditorFeatureGatePolicy.Create(SelectedTab.Mode, SelectedTab.FileSize);
        if (isEnabled(gate))
            return true;

        if (gate.StatusMessage != null)
            StatusText = gate.StatusMessage;

        return false;
    }

    public async Task RestoreSessionAsync()
    {
        var openFiles = _settingsService.OpenFiles;
        if (openFiles == null || openFiles.Count == 0) return;

        _unresolvedSessionFiles.Clear();
        var activeSourceEntry = GetActiveSourceEntry(openFiles);
        var usedEntryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sessionFile in openFiles)
        {
            if (string.IsNullOrEmpty(sessionFile.EntryId) ||
                !usedEntryIds.Add(sessionFile.EntryId))
            {
                sessionFile.EntryId = Guid.NewGuid().ToString("N");
                usedEntryIds.Add(sessionFile.EntryId);
            }
        }

        _startupActiveSessionEntryId = activeSourceEntry?.EntryId;
        var restoredByEntryId = new Dictionary<string, EditorTabViewModel>();
        foreach (var sessionFile in openFiles)
        {
            var restored = await RestoreSessionFileAsync(sessionFile);
            if (restored == null)
                _unresolvedSessionFiles.Add(sessionFile);
            else
                restoredByEntryId[sessionFile.EntryId] = restored;
        }

        SelectRestoredActiveTab(openFiles, activeSourceEntry, restoredByEntryId);
    }

    private async Task<EditorTabViewModel?> RestoreSessionFileAsync(SessionFile sessionFile)
    {
        try
        {
            if (sessionFile.IsUntitled)
                return await RestoreUntitledSessionFileAsync(sessionFile);
            return await RestoreExistingSessionFileAsync(sessionFile);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to restore session file '{0}': {1}", sessionFile.FilePath, ex.Message);
            return null;
        }
    }

    private async Task<EditorTabViewModel?> RestoreUntitledSessionFileAsync(SessionFile sessionFile)
    {
        var fileName = Path.GetFileName(sessionFile.FilePath);
        var existing = Tabs.FirstOrDefault(
            tab => string.IsNullOrEmpty(tab.FilePath) && tab.FileName == fileName);
        if (existing != null)
            return existing;

        if (sessionFile.IsBinaryMode)
        {
            if (sessionFile.BinaryContentBase64 == null)
                return null;
            var tab = _tabFactory.CreateUntitled(null);
            tab.RestoreBinarySnapshot(
                Convert.FromBase64String(sessionFile.BinaryContentBase64),
                fileName,
                filePath: null,
                GetRestoredModifiedState(sessionFile, hasSnapshot: true));
            ApplySessionPosition(tab, sessionFile);
            Tabs.Add(tab);
            return tab;
        }

        var content = await GetSessionContentAsync(sessionFile);
        var textTab = _tabFactory.CreateUntitled(content);
        textTab.RestoreTextSnapshot(
            content,
            fileName,
            filePath: null,
            sessionFile.EncodingCodePage,
            sessionFile.HasBom,
            GetRestoredModifiedState(sessionFile, hasSnapshot: true));
        ApplySessionPosition(textTab, sessionFile);
        Tabs.Add(textTab);
        return textTab;
    }

    private async Task<string> GetSessionContentAsync(SessionFile sessionFile)
    {
        if (!string.IsNullOrEmpty(sessionFile.TempFilePath) && _fileSystemService.FileExists(sessionFile.TempFilePath))
            return await _fileSystemService.ReadAllTextAsync(sessionFile.TempFilePath);

        return sessionFile.Content ?? string.Empty;
    }

    private async Task<EditorTabViewModel?> RestoreExistingSessionFileAsync(SessionFile sessionFile)
    {
        var hasSnapshot = sessionFile.Content != null ||
            sessionFile.BinaryContentBase64 != null ||
            (!string.IsNullOrEmpty(sessionFile.TempFilePath) &&
             _fileSystemService.FileExists(sessionFile.TempFilePath));
        var hasBinaryOverlay = sessionFile.BinaryBaseLength.HasValue ||
            sessionFile.BinaryBaseSha256 != null ||
            sessionFile.BinaryModifications != null;
        var existing = FindOpenFile(sessionFile.FilePath);
        if (existing != null)
            return existing;

        EditorTabViewModel tab;
        if (sessionFile.IsBinaryMode && sessionFile.BinaryContentBase64 != null)
        {
            tab = _tabFactory.CreateUntitled(null);
            tab.RestoreBinarySnapshot(
                Convert.FromBase64String(sessionFile.BinaryContentBase64),
                Path.GetFileName(sessionFile.FilePath),
                sessionFile.FilePath,
                GetRestoredModifiedState(sessionFile, hasSnapshot: true));
        }
        else if (sessionFile.IsBinaryMode && hasBinaryOverlay)
        {
            if (!sessionFile.BinaryBaseLength.HasValue ||
                string.IsNullOrEmpty(sessionFile.BinaryBaseSha256) ||
                sessionFile.BinaryModifications == null ||
                sessionFile.BinaryModifications.Count > MaxPersistedBinaryModifications)
            {
                return null;
            }

            if (!_fileSystemService.FileExists(sessionFile.FilePath) ||
                _fileSystemService.GetFileSize(sessionFile.FilePath) != sessionFile.BinaryBaseLength)
            {
                return null;
            }

            if (sessionFile.BinaryModifications.Any(modification =>
                    modification.Offset < 0 ||
                    modification.Offset >= sessionFile.BinaryBaseLength))
            {
                return null;
            }

            tab = _tabFactory.CreateUntitled(null);
            tab.RestoreBinaryOverlay(
                sessionFile.FilePath,
                sessionFile.BinaryBaseLength.Value,
                sessionFile.BinaryBaseSha256,
                sessionFile.BinaryModifications,
                GetRestoredModifiedState(sessionFile, hasSnapshot: true));
        }
        else if (hasSnapshot && !sessionFile.IsBinaryMode)
        {
            var content = await GetSessionContentAsync(sessionFile);
            tab = _tabFactory.CreateUntitled(content);
            tab.RestoreTextSnapshot(
                content,
                Path.GetFileName(sessionFile.FilePath),
                sessionFile.FilePath,
                sessionFile.EncodingCodePage,
                sessionFile.HasBom,
                GetRestoredModifiedState(sessionFile, hasSnapshot: true));
        }
        else if (_fileSystemService.FileExists(sessionFile.FilePath))
        {
            tab = _tabFactory.Create();
            await tab.LoadFileAsync(sessionFile.FilePath);
        }
        else
        {
            return null;
        }

        ApplySessionPosition(tab, sessionFile);
        Tabs.Add(tab);
        return tab;
    }

    private static bool GetRestoredModifiedState(SessionFile sessionFile, bool hasSnapshot)
    {
        // Older manifests did not persist this flag. Their payloads represented unsaved buffers.
        return sessionFile.SnapshotVersion > 0 ? sessionFile.IsModified : hasSnapshot;
    }

    private EditorTabViewModel? FindOpenFile(string filePath)
    {
        var normalizedPath = NormalizeFilePath(filePath);
        return Tabs.FirstOrDefault(t =>
            !string.IsNullOrEmpty(t.FilePath) &&
            HasSameOpenIdentity(t.FilePath, normalizedPath));
    }

    private SessionFile? GetActiveSourceEntry(IReadOnlyList<SessionFile> sourceEntries)
    {
        if (sourceEntries.Count == 0)
            return null;

        var sourceIndex = Math.Clamp(
            _settingsService.ActiveTabIndex,
            0,
            sourceEntries.Count - 1);
        var indexedEntry = sourceEntries[sourceIndex];
        var activeEntryId = _settingsService.ActiveSessionEntryId;
        if (string.IsNullOrEmpty(activeEntryId))
            return indexedEntry;

        if (indexedEntry.EntryId == activeEntryId)
            return indexedEntry;

        return sourceEntries.FirstOrDefault(entry => entry.EntryId == activeEntryId) ??
            indexedEntry;
    }

    private void SelectRestoredActiveTab(
        IReadOnlyList<SessionFile> sourceEntries,
        SessionFile? activeSourceEntry,
        IReadOnlyDictionary<string, EditorTabViewModel> restoredByEntryId)
    {
        if (Tabs.Count == 0)
            return;

        if (activeSourceEntry != null &&
            restoredByEntryId.TryGetValue(activeSourceEntry.EntryId, out var restoredActive))
        {
            SelectedTab = restoredActive;
        }
        else if (activeSourceEntry != null)
        {
            var activeIndex = sourceEntries.ToList().IndexOf(activeSourceEntry);
            var fallback = sourceEntries
                .Skip(activeIndex + 1)
                .Concat(sourceEntries.Take(activeIndex).Reverse())
                .Select(entry => restoredByEntryId.GetValueOrDefault(entry.EntryId))
                .FirstOrDefault(tab => tab != null);
            SelectedTab = fallback ?? SelectedTab ?? Tabs[0];
        }
        else if (SelectedTab == null)
        {
            SelectedTab = Tabs[0];
        }
    }

    private static void ApplySessionPosition(
        EditorTabViewModel tab,
        SessionFile sessionFile)
    {
        tab.CursorOffset = sessionFile.CursorOffset;
        tab.ScrollOffset = sessionFile.ScrollOffset;
        tab.HexOffset = sessionFile.HexOffset;
        tab.HexScrollOffset = sessionFile.HexScrollOffset;
        tab.BytesPerRow = sessionFile.BytesPerRow;
    }

    public void SaveSession()
    {
        var sessionFiles = new List<SessionFile>();
        var persistedActiveTabIndex = 0;
        string? activeEntryId = null;

        foreach (var tab in Tabs)
        {
            if (_shutdownDiscardedTabs.Contains(tab) && string.IsNullOrEmpty(tab.FilePath))
                continue;

            if (tab == SelectedTab)
                persistedActiveTabIndex = sessionFiles.Count;

            var sessionFile = new SessionFile
            {
                EntryId = tab.AutoSaveIdentity,
                SnapshotVersion = 1,
                FilePath = string.IsNullOrEmpty(tab.FilePath) ? tab.FileName : tab.FilePath,
                IsUntitled = string.IsNullOrEmpty(tab.FilePath),
                IsBinaryMode = tab.Mode == FileOpenMode.Binary,
                IsModified = tab.IsModified,
                EncodingCodePage = tab.FileEncoding.CodePage,
                HasBom = tab.HasBom,
                CursorOffset = tab.CursorOffset,
                ScrollOffset = tab.ScrollOffset,
                HexOffset = tab.HexOffset,
                HexScrollOffset = tab.HexScrollOffset,
                BytesPerRow = tab.BytesPerRow
            };

            if (tab.Mode == FileOpenMode.Binary)
            {
                var buffer = tab.ByteBuffer ??
                    throw new InvalidOperationException(
                        $"Binary tab '{tab.FileName}' has no active byte buffer.");
                if (!buffer.IsSnapshotBacked && !string.IsNullOrEmpty(tab.FilePath))
                {
                    var modifications = buffer.GetModifications();
                    if (modifications.Count > MaxPersistedBinaryModifications)
                    {
                        throw new InvalidOperationException(
                            $"Binary tab '{tab.FileName}' has too many unsaved byte edits to snapshot safely.");
                    }

                    sessionFile.BinaryBaseLength = buffer.Length;
                    sessionFile.BinaryBaseSha256 = buffer.ComputeBaseSha256();
                    sessionFile.BinaryModifications = modifications
                        .Select(modification => new BinaryModification
                        {
                            Offset = modification.Key,
                            Value = modification.Value
                        })
                        .ToList();
                }
                else
                {
                    sessionFile.BinaryContentBase64 = Convert.ToBase64String(
                        buffer.CreateSnapshot());
                }
            }
            else if (tab.Mode == FileOpenMode.Text)
                sessionFile.Content = tab.Content;

            sessionFiles.Add(sessionFile);
            if (tab == SelectedTab)
                activeEntryId = sessionFile.EntryId;
        }

        var currentEntryIds = sessionFiles
            .Select(file => file.EntryId)
            .ToHashSet(StringComparer.Ordinal);
        sessionFiles.AddRange(_unresolvedSessionFiles.Where(
            file => !currentEntryIds.Contains(file.EntryId)));

        _settingsService.OpenFiles = sessionFiles;
        _settingsService.ActiveTabIndex = persistedActiveTabIndex;
        _settingsService.ActiveSessionEntryId = activeEntryId ??
            _startupActiveSessionEntryId ??
            _settingsService.ActiveSessionEntryId;
        _settingsService.Save();
    }

    private async Task UpdateGitBranchAsync(string? filePath)
    {
        try
        {
            var branch = await GitHelper.GetBranchNameAsync(filePath);
            GitBranch = branch ?? "";
        }
        catch
        {
            GitBranch = "";
        }
    }

    private void AddToRecentFiles(string filePath)
    {
        RecentFiles.Remove(filePath);
        RecentFiles.Insert(0, filePath);
        while (RecentFiles.Count > 10)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        _settingsService.AddRecentFile(filePath);
    }

    public bool HasUnsavedChanges()
    {
        return Tabs.Any(t => t.IsModified);
    }

    public async Task<bool> ConfirmExitAsync()
    {
        return await ConfirmUnsavedChangesAsync(recordShutdownDiscards: false);
    }

    public async Task<bool> PrepareForExitAsync()
    {
        _shutdownDiscardedTabs.Clear();
        return await ConfirmUnsavedChangesAsync(recordShutdownDiscards: true);
    }

    public void CancelExitPreparation()
    {
        _shutdownDiscardedTabs.Clear();
    }

    private async Task<bool> ConfirmUnsavedChangesAsync(bool recordShutdownDiscards)
    {
        var unsavedTabs = Tabs.Where(t => t.IsModified).ToList();
        if (unsavedTabs.Count == 0)
            return true;

        var fileNames = string.Join(", ", unsavedTabs.Select(t => t.FileName));
        var result = _dialogService.ShowMessage(
            $"Do you want to save changes to the following files?\n\n{fileNames}",
            "FastEdit - Unsaved Changes",
            DialogButtons.YesNoCancel,
            DialogIcon.Warning);

        if (result == Services.Interfaces.DialogResult.Cancel)
            return false;

        if (result == Services.Interfaces.DialogResult.No && recordShutdownDiscards)
        {
            foreach (var tab in unsavedTabs.Where(tab => string.IsNullOrEmpty(tab.FilePath)))
                _shutdownDiscardedTabs.Add(tab);
        }

        if (result == Services.Interfaces.DialogResult.Yes)
        {
            var approvedVersions = Tabs.ToDictionary(tab => tab, tab => tab.UserMutationVersion);
            foreach (var tab in unsavedTabs)
            {
                try
                {
                    await tab.SaveCommand.ExecuteAsync(null);
                    if (tab.IsModified)
                        return false;
                }
                catch (Exception ex)
                {
                    StatusText = $"Error saving {tab.FileName}: {ex.Message}";
                    return false;
                }
            }

            if (HasWorkspaceChanged(approvedVersions))
            {
                StatusText = "Files changed while saving; close or load the session again.";
                return false;
            }
        }

        return true;
    }

    public IReadOnlyList<AutoSaveEntry> GetAutoSaveEntries()
    {
        var entries = new List<AutoSaveEntry>();
        foreach (var tab in Tabs)
        {
            if (!tab.IsModified && !string.IsNullOrEmpty(tab.FilePath)) continue;

            entries.Add(new AutoSaveEntry(
                $"tab-{tab.AutoSaveIdentity}",
                tab.FileName,
                string.IsNullOrEmpty(tab.FilePath) ? null : NormalizeFilePath(tab.FilePath),
                tab.Content ?? "",
                string.IsNullOrEmpty(tab.FilePath),
                tab.CursorOffset,
                tab.ScrollOffset));
        }

        return entries;
    }

    internal static string NormalizeFilePath(string filePath) =>
        Path.GetFullPath(filePath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    internal static bool HasSameOpenIdentity(string firstPath, string secondPath) =>
        string.Equals(
            NormalizeFilePath(firstPath),
            NormalizeFilePath(secondPath),
            StringComparison.Ordinal);

    public EditorTabViewModel RecoverTab(AutoSaveEntry entry)
    {
        var tab = _tabFactory.CreateUntitled(entry.Content);
        tab.FileName = entry.FileName;
        if (!entry.IsUntitled && !string.IsNullOrEmpty(entry.FilePath))
            tab.FilePath = entry.FilePath;
        tab.CursorOffset = entry.CursorOffset;
        tab.ScrollOffset = entry.ScrollOffset;
        tab.IsModified = true;
        return tab;
    }

    public TabRecoveryResult RecoverTabs(IReadOnlyList<AutoSaveEntry> entries)
    {
        var recoveredTabs = new List<EditorTabViewModel>(entries.Count);
        var recoveredEntryIds = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            try
            {
                recoveredTabs.Add(RecoverTab(entry));
                recoveredEntryIds.Add(entry.Id);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to recover auto-save entry '{0}': {1}", entry.FileName, ex);
            }
        }

        foreach (var tab in recoveredTabs)
            Tabs.Add(tab);

        if (recoveredTabs.Count > 0)
            SelectedTab = recoveredTabs[0];

        if (recoveredTabs.Count != entries.Count)
        {
            StatusText = $"Recovered {recoveredTabs.Count} of {entries.Count} files; recovery files were retained.";
            return new TabRecoveryResult(false, recoveredEntryIds);
        }

        return new TabRecoveryResult(true, recoveredEntryIds);
    }
}

public record TabRecoveryResult(
    bool Success,
    IReadOnlyList<string> RecoveredEntryIds);
