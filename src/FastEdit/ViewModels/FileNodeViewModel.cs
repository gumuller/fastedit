using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class FileNodeViewModel : ObservableObject
{
    private readonly IFileSystemService? _fileSystemService;

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

    public string Icon => IsDirectory ? "\uE8B7" : "\uE8A5";

    public FileNodeViewModel(string path, bool isDirectory, IFileSystemService? fileSystemService = null)
    {
        _fileSystemService = fileSystemService;
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

        try
        {
            var directories = (_fileSystemService?.GetDirectories(FullPath) ?? Directory.GetDirectories(FullPath))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var dir in directories)
            {
                try
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith('.') || name.StartsWith('$'))
                        continue;

                    Children.Add(new FileNodeViewModel(dir, true, _fileSystemService));
                }
                catch
                {
                }
            }

            var files = (_fileSystemService?.GetFiles(FullPath) ?? Directory.GetFiles(FullPath))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith('.'))
                        continue;

                    Children.Add(new FileNodeViewModel(file, false, _fileSystemService));
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        IsLoaded = true;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsLoaded)
        {
            LoadChildren();
        }
    }
}
