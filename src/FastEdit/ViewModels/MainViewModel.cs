using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Services.Interfaces;
using FastEdit.Theming;

namespace FastEdit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly IThemeService _themeService;
    private readonly ISettingsService _settingsService;

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

    public MainViewModel(
        IFileService fileService,
        IThemeService themeService,
        ISettingsService settingsService,
        FileTreeViewModel fileTree)
    {
        _fileService = fileService;
        _themeService = themeService;
        _settingsService = settingsService;
        _fileTree = fileTree;
        _availableThemes = themeService.AvailableThemes;
        _currentThemeName = themeService.CurrentTheme?.Name ?? "Dark";

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
        filePath ??= await _fileService.ShowOpenFileDialogAsync();
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
            var tab = new EditorTabViewModel(_fileService);
            await tab.LoadFileAsync(filePath);

            Tabs.Add(tab);
            SelectedTab = tab;

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
    private void CloseTab(EditorTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        // Confirm if modified
        if (tab.IsModified)
        {
            var result = System.Windows.MessageBox.Show(
                $"Do you want to save changes to '{tab.FileName}'?",
                "FastEdit - Unsaved Changes",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Cancel)
                return;

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                tab.SaveCommand.Execute(null);
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
    private async Task OpenFolderAsync()
    {
        await _fileTree.OpenFolderCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void NewFile()
    {
        var tab = new EditorTabViewModel(_fileService)
        {
            FileName = $"Untitled-{Tabs.Count + 1}",
            Content = string.Empty
        };

        Tabs.Add(tab);
        SelectedTab = tab;
    }

    partial void OnSelectedTabChanged(EditorTabViewModel? value)
    {
        if (value != null)
        {
            StatusText = value.IsBinaryMode
                ? $"Hex Mode - {value.FileSize:N0} bytes"
                : $"Ln {value.Line}, Col {value.Column} | {value.Encoding}";
        }
        else
        {
            StatusText = "Ready";
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
                    string content = string.Empty;

                    // Try to load content from temp file first
                    if (!string.IsNullOrEmpty(sessionFile.TempFilePath) && File.Exists(sessionFile.TempFilePath))
                    {
                        content = await File.ReadAllTextAsync(sessionFile.TempFilePath);
                        // Clean up temp file after reading
                        try { File.Delete(sessionFile.TempFilePath); } catch { }
                    }
                    else if (!string.IsNullOrEmpty(sessionFile.Content))
                    {
                        content = sessionFile.Content;
                    }

                    // Only restore if there's actual content
                    if (!string.IsNullOrEmpty(content))
                    {
                        var tab = new EditorTabViewModel(_fileService)
                        {
                            FileName = Path.GetFileName(sessionFile.FilePath),
                            Content = content
                        };
                        Tabs.Add(tab);
                    }
                }
                else if (File.Exists(sessionFile.FilePath))
                {
                    var tab = new EditorTabViewModel(_fileService);
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
            SelectedTab = Tabs[0];
        }

        // Clear the stored session and clean up any remaining temp files
        _settingsService.OpenFiles = new List<SessionFile>();
        _settingsService.Save();
        CleanupTempFiles();
    }

    private void CleanupTempFiles()
    {
        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Temp");

        if (Directory.Exists(tempDir))
        {
            try
            {
                foreach (var file in Directory.GetFiles(tempDir, "*.tmp"))
                {
                    try { File.Delete(file); } catch { }
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
        Directory.CreateDirectory(tempDir);

        foreach (var tab in Tabs)
        {
            var sessionFile = new SessionFile
            {
                FilePath = string.IsNullOrEmpty(tab.FilePath) ? tab.FileName : tab.FilePath,
                IsUntitled = string.IsNullOrEmpty(tab.FilePath)
            };

            if (sessionFile.IsUntitled && !tab.IsBinaryMode)
            {
                // Save untitled content to temp file
                var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{tab.FileName}.tmp");
                try
                {
                    File.WriteAllText(tempPath, tab.Content);
                    sessionFile.TempFilePath = tempPath;
                }
                catch
                {
                    // Store content directly if temp save fails
                    sessionFile.Content = tab.Content;
                }
            }

            sessionFiles.Add(sessionFile);
        }

        _settingsService.OpenFiles = sessionFiles;
        _settingsService.Save();
    }

    public bool HasUnsavedChanges()
    {
        return Tabs.Any(t => t.IsModified);
    }

    public bool ConfirmExit()
    {
        var unsavedTabs = Tabs.Where(t => t.IsModified).ToList();
        if (unsavedTabs.Count == 0)
            return true;

        var fileNames = string.Join(", ", unsavedTabs.Select(t => t.FileName));
        var result = System.Windows.MessageBox.Show(
            $"Do you want to save changes to the following files?\n\n{fileNames}",
            "FastEdit - Unsaved Changes",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Cancel)
            return false;

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            foreach (var tab in unsavedTabs)
            {
                tab.SaveCommand.Execute(null);
            }
        }

        return true;
    }
}
