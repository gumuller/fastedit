using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Helpers;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;

namespace FastEdit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IEditorTabFactory _tabFactory;

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
    private bool _isCommandRunnerVisible;

    [ObservableProperty]
    private string _gitBranch = "";

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
    public event Action? ToggleSplitViewRequested;

    public MainViewModel(
        IFileService fileService,
        IThemeService themeService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory,
        FileTreeViewModel fileTree)
    {
        _fileService = fileService;
        _themeService = themeService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _fileSystemService = fileSystemService;
        _tabFactory = tabFactory;
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
    }

    private async void OnFileOpenRequested(object? sender, string filePath)
    {
        await OpenFileAsync(filePath);
    }

    [RelayCommand]
    private async Task OpenFileAsync(string? filePath = null)
    {
        filePath ??= _dialogService.ShowOpenFileDialog();
        if (string.IsNullOrEmpty(filePath)) return;

        // Check if already open
        var existingTab = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existingTab != null)
        {
            SelectedTab = existingTab;
            return;
        }

        try
        {
            var tab = _tabFactory.Create();
            await tab.LoadFileAsync(filePath);

            Tabs.Add(tab);
            SelectedTab = tab;

            AddToRecentFiles(filePath);
            StatusText = $"Opened: {Path.GetFileName(filePath)}";
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
    private void GoToLine()
    {
        if (SelectedTab == null || SelectedTab.IsBinaryMode) return;
        GoToLineRequested?.Invoke(SelectedTab.Line);
    }

    [RelayCommand]
    private void ToggleWordWrap()
    {
        IsWordWrapEnabled = !IsWordWrapEnabled;
        _settingsService.WordWrapEnabled = IsWordWrapEnabled;
        StatusText = IsWordWrapEnabled ? "Word Wrap: On" : "Word Wrap: Off";
    }

    [RelayCommand]
    private void ToggleWhitespace()
    {
        IsWhitespaceVisible = !IsWhitespaceVisible;
        _settingsService.ShowWhitespace = IsWhitespaceVisible;
        StatusText = IsWhitespaceVisible ? "Whitespace: Visible" : "Whitespace: Hidden";
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
    private void DuplicateLine() => DuplicateLineRequested?.Invoke();

    [RelayCommand]
    private void MoveLineUp() => MoveLineRequested?.Invoke(true);

    [RelayCommand]
    private void MoveLineDown() => MoveLineRequested?.Invoke(false);

    [RelayCommand]
    private void FormatDocument() => FormatDocumentRequested?.Invoke();

    [RelayCommand]
    private void MinifyDocument() => MinifyDocumentRequested?.Invoke();

    [RelayCommand]
    private void ToggleFolding()
    {
        IsFoldingEnabled = !IsFoldingEnabled;
        StatusText = IsFoldingEnabled ? "Code Folding: On" : "Code Folding: Off";
    }

    [RelayCommand]
    private void ToggleMinimap()
    {
        IsMinimapVisible = !IsMinimapVisible;
        StatusText = IsMinimapVisible ? "Minimap: Visible" : "Minimap: Hidden";
    }

    [RelayCommand]
    private void ToggleAutoReload()
    {
        IsAutoReloadEnabled = !IsAutoReloadEnabled;
        StatusText = IsAutoReloadEnabled ? "Auto-Reload: On" : "Auto-Reload: Off";
    }

    [RelayCommand]
    private void ToggleBookmark() => ToggleBookmarkRequested?.Invoke();

    [RelayCommand]
    private void NextBookmark() => NextBookmarkRequested?.Invoke();

    [RelayCommand]
    private void PrevBookmark() => PrevBookmarkRequested?.Invoke();

    [RelayCommand]
    private void FindInFiles() => FindInFilesRequested?.Invoke();

    [RelayCommand]
    private void CompareFiles() => CompareFilesRequested?.Invoke();

    [RelayCommand]
    private void ToggleIndentGuides()
    {
        IsIndentGuidesEnabled = !IsIndentGuidesEnabled;
        StatusText = IsIndentGuidesEnabled ? "Indent Guides: On" : "Indent Guides: Off";
    }

    [RelayCommand]
    private void CommandPalette() => CommandPaletteRequested?.Invoke();

    [RelayCommand]
    private void ShowCompletion() => ShowCompletionRequested?.Invoke();

    [RelayCommand]
    private void ToggleCommandRunner()
    {
        IsCommandRunnerVisible = !IsCommandRunnerVisible;
        StatusText = IsCommandRunnerVisible ? "Terminal: Visible" : "Terminal: Hidden";
    }

    [RelayCommand]
    private void ToggleSplitView() => ToggleSplitViewRequested?.Invoke();

    [RelayCommand]
    private void ConvertLineEndings(string? target)
    {
        if (SelectedTab == null || SelectedTab.IsBinaryMode || string.IsNullOrEmpty(target)) return;

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
                var saved = await tab.SaveCommand.ExecuteAsync(null)
                    .ContinueWith(_ => !tab.IsModified);
                if (!saved)
                    return; // Save was cancelled or failed
            }
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

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
            (e.PropertyName == nameof(EditorTabViewModel.IsBinaryMode) ||
             e.PropertyName == nameof(EditorTabViewModel.Line) ||
             e.PropertyName == nameof(EditorTabViewModel.Column)))
        {
            UpdateStatusForTab(tab);
        }
    }

    private void UpdateStatusForTab(EditorTabViewModel tab)
    {
        if (tab.IsBinaryMode)
        {
            StatusText = $"Hex Mode - {tab.FileSize:N0} bytes";
            LineEnding = "";
        }
        else
        {
            StatusText = $"Ln {tab.Line}, Col {tab.Column} | {tab.Encoding}";
            LineEnding = LineEndingHelper.ToDisplayString(LineEndingHelper.Detect(tab.Content));
        }
    }

    public async Task RestoreSessionAsync()
    {
        var openFiles = _settingsService.OpenFiles;
        if (openFiles == null || openFiles.Count == 0) return;

        foreach (var sessionFile in openFiles)
        {
            try
            {
                if (sessionFile.IsUntitled)
                {
                    // Skip untitled binary tabs — can't be meaningfully restored
                    if (sessionFile.IsBinaryMode) continue;

                    string content = string.Empty;

                    if (!string.IsNullOrEmpty(sessionFile.TempFilePath) && _fileSystemService.FileExists(sessionFile.TempFilePath))
                    {
                        content = await _fileSystemService.ReadAllTextAsync(sessionFile.TempFilePath);
                    }
                    else if (!string.IsNullOrEmpty(sessionFile.Content))
                    {
                        content = sessionFile.Content;
                    }

                    var tab = _tabFactory.CreateUntitled(content);
                    tab.FileName = Path.GetFileName(sessionFile.FilePath);
                    Tabs.Add(tab);
                }
                else if (_fileSystemService.FileExists(sessionFile.FilePath))
                {
                    var tab = _tabFactory.Create();
                    await tab.LoadFileAsync(sessionFile.FilePath);
                    Tabs.Add(tab);
                }
            }
            catch
            {
                // Skip files that can't be restored
            }
        }

        if (Tabs.Count > 0)
        {
            var index = Math.Clamp(_settingsService.ActiveTabIndex, 0, Tabs.Count - 1);
            SelectedTab = Tabs[index];
        }

        // Clean up temp files only (keep session data until next clean close)
        CleanupTempFiles();
    }

    private void CleanupTempFiles()
    {
        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Temp");

        if (_fileSystemService.DirectoryExists(tempDir))
        {
            try
            {
                foreach (var file in _fileSystemService.GetFiles(tempDir, "*.tmp"))
                {
                    try { _fileSystemService.DeleteFile(file); } catch { }
                }
            }
            catch { }
        }
    }

    public void SaveSession()
    {
        var sessionFiles = new List<SessionFile>();
        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Temp");
        _fileSystemService.CreateDirectory(tempDir);

        foreach (var tab in Tabs)
        {
            var sessionFile = new SessionFile
            {
                FilePath = string.IsNullOrEmpty(tab.FilePath) ? tab.FileName : tab.FilePath,
                IsUntitled = string.IsNullOrEmpty(tab.FilePath),
                IsBinaryMode = tab.IsBinaryMode,
                CursorOffset = tab.CursorOffset,
                ScrollOffset = tab.ScrollOffset
            };

            if (sessionFile.IsUntitled && !tab.IsBinaryMode)
            {
                var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{tab.FileName}.tmp");
                try
                {
                    _fileSystemService.WriteAllText(tempPath, tab.Content);
                    sessionFile.TempFilePath = tempPath;
                }
                catch
                {
                    sessionFile.Content = tab.Content;
                }
            }

            sessionFiles.Add(sessionFile);
        }

        _settingsService.OpenFiles = sessionFiles;
        _settingsService.ActiveTabIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : 0;
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

        if (result == Services.Interfaces.DialogResult.Yes)
        {
            foreach (var tab in unsavedTabs)
            {
                await tab.SaveCommand.ExecuteAsync(null);
                if (tab.IsModified)
                    return false; // Save was cancelled or failed
            }
        }

        return true;
    }
}
