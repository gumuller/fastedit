using System.IO;
using FastEdit.Services.Interfaces;
using Microsoft.Win32;

namespace FastEdit.Services;

public class FileService : IFileService
{
    public Task<string?> ShowOpenFileDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "All Files (*.*)|*.*|Text Files (*.txt)|*.txt|Source Code|*.cs;*.js;*.ts;*.py;*.cpp;*.c;*.h;*.java;*.go;*.rs;*.rb",
            Title = "Open File"
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileName : null);
    }

    public Task<string?> ShowOpenFolderDialogAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder"
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FolderName : null);
    }

    public Task<string?> ShowSaveFileDialogAsync(string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "All Files (*.*)|*.*",
            Title = "Save File",
            FileName = defaultFileName ?? string.Empty
        };

        var result = dialog.ShowDialog();
        return Task.FromResult(result == true ? dialog.FileName : null);
    }

    public async Task<string> ReadAllTextAsync(string filePath)
    {
        return await File.ReadAllTextAsync(filePath);
    }

    public async Task WriteAllTextAsync(string filePath, string content)
    {
        await File.WriteAllTextAsync(filePath, content);
    }
}
