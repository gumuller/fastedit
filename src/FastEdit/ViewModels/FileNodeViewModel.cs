using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class FileNodeViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private bool _isDirectory;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private ObservableCollection<FileNodeViewModel> _children = new();

    public ObservableCollection<string> LoadErrors { get; } = new();

    public string Icon => Helpers.FileIconHelper.GetIcon(Name, IsDirectory);

    public string? IconColor => IsDirectory ? null : Helpers.FileIconHelper.GetIconColor(Name);

    public FileNodeViewModel(string path, bool isDirectory, IFileSystemService fileSystemService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        FullPath = path;
        Name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name))
            Name = path;

        IsDirectory = isDirectory;

        if (isDirectory)
        {
            Children.Add(new FileNodeViewModel("", false, fileSystemService) { Name = "Loading..." });
        }
    }

    public void LoadChildren()
    {
        if (IsLoaded || !IsDirectory) return;

        Children.Clear();
        LoadErrors.Clear();

        LoadDirectoryChildren();
        LoadFileChildren();

        IsLoaded = true;
    }

    private void LoadDirectoryChildren()
    {
        try
        {
            var directories = _fileSystemService.GetDirectories(FullPath)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                AddChildIfVisible(directory, isDirectory: true);
            }
        }
        catch (Exception ex)
        {
            RecordLoadError($"Failed to load directories under '{FullPath}': {ex.Message}");
        }
    }

    private void LoadFileChildren()
    {
        try
        {
            var files = _fileSystemService.GetFiles(FullPath)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                AddChildIfVisible(file, isDirectory: false);
            }
        }
        catch (Exception ex)
        {
            RecordLoadError($"Failed to load files under '{FullPath}': {ex.Message}");
        }
    }

    private void AddChildIfVisible(string path, bool isDirectory)
    {
        try
        {
            var name = Path.GetFileName(path);
            if (ShouldSkip(name, isDirectory))
                return;

            Children.Add(new FileNodeViewModel(path, isDirectory, _fileSystemService));
        }
        catch (Exception ex)
        {
            RecordLoadError($"Failed to add file tree item '{path}': {ex.Message}");
        }
    }

    private static bool ShouldSkip(string name, bool isDirectory)
    {
        return string.IsNullOrEmpty(name)
            || name.StartsWith('.')
            || (isDirectory && name.StartsWith('$'));
    }

    private void RecordLoadError(string message)
    {
        LoadErrors.Add(message);
        Trace.TraceWarning(message);
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsLoaded)
        {
            LoadChildren();
        }
    }
}
