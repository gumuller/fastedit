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

    [ObservableProperty]
    private ObservableCollection<FileNodeViewModel> _rootNodes = new();

    [ObservableProperty]
    private FileNodeViewModel? _selectedNode;

    [ObservableProperty]
    private string? _rootPath;

    public event EventHandler<string>? FileOpenRequested;

    public FileTreeViewModel(IFileService fileService, ISettingsService settingsService)
    {
        _fileService = fileService;
        _settingsService = settingsService;

        // Load last opened folder or default to user's home directory
        var lastFolder = settingsService.LastOpenedFolder;
        if (!string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder))
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
    private async Task OpenFolderAsync()
    {
        var folder = await _fileService.ShowOpenFolderDialogAsync();
        if (!string.IsNullOrEmpty(folder))
        {
            SetRootFolder(folder);
        }
    }

    [RelayCommand]
    private void SetRootFolder(string path)
    {
        if (!Directory.Exists(path)) return;

        RootPath = path;
        _settingsService.LastOpenedFolder = path;

        RootNodes.Clear();
        RootNodes.Add(new FileNodeViewModel(path, true) { IsExpanded = true });
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
