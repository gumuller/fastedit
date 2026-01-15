namespace FastEdit.Services.Interfaces;

public interface IFileService
{
    Task<string?> ShowOpenFileDialogAsync();
    Task<string?> ShowOpenFolderDialogAsync();
    Task<string?> ShowSaveFileDialogAsync(string? defaultFileName = null);
    Task<string> ReadAllTextAsync(string filePath);
    Task WriteAllTextAsync(string filePath, string content);
}
