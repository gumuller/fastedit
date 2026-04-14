using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FastEdit.ViewModels;

public partial class FileNodeViewModel : ObservableObject
{
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

    public FileNodeViewModel(string path, bool isDirectory)
    {
        FullPath = path;
        Name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(Name))
            Name = path; // For root drives

        IsDirectory = isDirectory;

        if (isDirectory)
        {
            // Add dummy child for expand arrow
            Children.Add(new FileNodeViewModel("", false) { Name = "Loading..." });
        }
    }

    public void LoadChildren()
    {
        if (IsLoaded || !IsDirectory) return;

        Children.Clear();

        try
        {
            // Add directories first
            var directories = Directory.GetDirectories(FullPath)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var dir in directories)
            {
                try
                {
                    var name = Path.GetFileName(dir);
                    // Skip hidden/system folders
                    if (name.StartsWith('.') || name.StartsWith('$'))
                        continue;

                    Children.Add(new FileNodeViewModel(dir, true));
                }
                catch
                {
                    // Skip inaccessible directories
                }
            }

            // Then add files
            var files = Directory.GetFiles(FullPath)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                try
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith('.'))
                        continue;

                    Children.Add(new FileNodeViewModel(file, false));
                }
                catch
                {
                    // Skip inaccessible files
                }
            }
        }
        catch
        {
            // Handle access denied etc
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
