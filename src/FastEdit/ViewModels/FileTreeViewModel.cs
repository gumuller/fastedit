using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class FileTreeViewModel : ObservableObject
{
    private readonly IFileService _fileService;
    private readonly ISettingsService _settingsService;
    private readonly IDialogService _dialogService;
    private readonly IFileSystemService _fileSystemService;

    [ObservableProperty]
    private ObservableCollection<FileNodeViewModel> _rootNodes = new();

    [ObservableProperty]
    private FileNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string? _rootPath;

    private readonly List<string> _rootPaths = new();

    public IReadOnlyList<string> RootPaths => _rootPaths;

    public event EventHandler<string>? FileOpenRequested;

    public FileTreeViewModel(
        IFileService fileService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IFileSystemService fileSystemService)
    {
        _fileService = fileService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _fileSystemService = fileSystemService;

        var lastFolder = settingsService.LastOpenedFolder;
        if (!string.IsNullOrEmpty(lastFolder) && _fileSystemService.DirectoryExists(lastFolder))
        {
            SetRootFolder(lastFolder);
        }
        else
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            SetRootFolder(homeDir);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = _dialogService.ShowFolderBrowserDialog();
        if (!string.IsNullOrEmpty(folder))
        {
            SetRootFolder(folder);
        }
    }

    [RelayCommand]
    private void SetRootFolder(string path)
    {
        if (!_fileSystemService.DirectoryExists(path)) return;

        RootPath = path;
        _settingsService.LastOpenedFolder = path;
        _rootPaths.Clear();
        _rootPaths.Add(path);

        RootNodes.Clear();
        RootNodes.Add(new FileNodeViewModel(path, true, _fileSystemService) { IsExpanded = true });
    }

    public void AddRootFolder(string path)
    {
        if (!_fileSystemService.DirectoryExists(path)) return;
        if (_rootPaths.Contains(path)) return;

        _rootPaths.Add(path);
        if (RootPath == null) RootPath = path;
        RootNodes.Add(new FileNodeViewModel(path, true, _fileSystemService) { IsExpanded = true });
    }

    public void SetMultipleRoots(IEnumerable<string> paths)
    {
        RootNodes.Clear();
        _rootPaths.Clear();

        foreach (var path in paths)
        {
            if (!_fileSystemService.DirectoryExists(path)) continue;
            _rootPaths.Add(path);
            RootNodes.Add(new FileNodeViewModel(path, true, _fileSystemService) { IsExpanded = true });
        }

        RootPath = _rootPaths.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenSelectedFile()
    {
        if (SelectedNode?.IsDirectory == false)
        {
            FileOpenRequested?.Invoke(this, SelectedNode.FullPath);
        }
    }

    partial void OnSelectedNodeChanged(FileNodeViewModel? value)
    {
        if (value?.IsDirectory == false)
        {
            // Double-click behavior handled by view
        }
    }
}
