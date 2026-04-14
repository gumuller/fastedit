using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Core.FileAnalysis;
using FastEdit.Core.HexEngine;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class EditorTabViewModel : ObservableObject, IDisposable
{
    private readonly IFileService _fileService;
    private VirtualizedByteBuffer? _byteBuffer;
    private bool _disposed;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isBinaryMode;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private string _encoding = "UTF-8";

    [ObservableProperty]
    private int _line = 1;

    [ObservableProperty]
    private int _column = 1;

    [ObservableProperty]
    private string _syntaxLanguage = string.Empty;

    // Hex editor properties
    [ObservableProperty]
    private long _hexOffset;

    [ObservableProperty]
    private int _bytesPerRow = 16;

    public VirtualizedByteBuffer? ByteBuffer => _byteBuffer;

    public EditorTabViewModel(IFileService fileService)
    {
        _fileService = fileService;
    }

    public async Task LoadFileAsync(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);

        var fileInfo = new FileInfo(filePath);
        FileSize = fileInfo.Length;

        // Detect if binary
        var detector = new BinaryDetector();
        var analysis = await detector.AnalyzeFileAsync(filePath);

        IsBinaryMode = analysis.IsBinary;
        Encoding = analysis.DetectedEncoding ?? "UTF-8";

        if (IsBinaryMode)
        {
            _byteBuffer = new VirtualizedByteBuffer(filePath);
            _byteBuffer.ModificationsChanged += (s, e) =>
            {
                IsModified = _byteBuffer.HasModifications;
            };
        }
        else
        {
            Content = await _fileService.ReadAllTextAsync(filePath);
            SyntaxLanguage = GetSyntaxLanguage(filePath);
        }

        IsModified = false;
    }

    [RelayCommand]
    private async Task<bool> SaveAsync()
    {
        if (IsBinaryMode)
        {
            if (_byteBuffer != null)
            {
                _byteBuffer.Save();
                IsModified = false;
                return true;
            }
            return false;
        }

        var savePath = FilePath;

        // If untitled (no file path), prompt for Save As
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = await _fileService.ShowSaveFileDialogAsync(FileName);
            if (string.IsNullOrEmpty(savePath))
                return false; // User cancelled
        }

        await _fileService.WriteAllTextAsync(savePath, Content);

        // Update metadata after successful save
        FilePath = savePath;
        FileName = Path.GetFileName(savePath);
        SyntaxLanguage = GetSyntaxLanguage(savePath);
        IsModified = false;
        return true;
    }

    [RelayCommand]
    private async Task<bool> SaveAsAsync()
    {
        var defaultName = string.IsNullOrEmpty(FilePath) ? FileName : Path.GetFileName(FilePath);
        var savePath = await _fileService.ShowSaveFileDialogAsync(defaultName);
        if (string.IsNullOrEmpty(savePath))
            return false;

        if (IsBinaryMode)
        {
            if (_byteBuffer != null)
            {
                _byteBuffer.Save(); // saves to original path
                // For Save As in binary mode, copy file to new location
                if (savePath != FilePath)
                    File.Copy(FilePath, savePath, overwrite: true);
            }
            else
            {
                return false;
            }
        }
        else
        {
            await _fileService.WriteAllTextAsync(savePath, Content);
        }

        FilePath = savePath;
        FileName = Path.GetFileName(savePath);
        SyntaxLanguage = GetSyntaxLanguage(savePath);
        IsModified = false;
        return true;
    }

    [RelayCommand]
    private async Task ToggleModeAsync()
    {
        // Only allow toggle for saved files
        if (string.IsNullOrEmpty(FilePath)) return;

        // Block toggle when there are unsaved edits
        if (IsModified) return;

        if (IsBinaryMode)
        {
            // Binary → Text: dispose buffer, reload as text
            _byteBuffer?.Dispose();
            _byteBuffer = null;
            IsBinaryMode = false;
            Content = await _fileService.ReadAllTextAsync(FilePath);
            SyntaxLanguage = GetSyntaxLanguage(FilePath);
        }
        else
        {
            // Text → Binary: create byte buffer
            IsBinaryMode = true;
            Content = string.Empty;
            SyntaxLanguage = string.Empty;
            _byteBuffer = new VirtualizedByteBuffer(FilePath);
            _byteBuffer.ModificationsChanged += (s, e) =>
            {
                IsModified = _byteBuffer.HasModifications;
            };
        }
    }

    partial void OnContentChanged(string value)
    {
        if (!string.IsNullOrEmpty(FilePath))
        {
            IsModified = true;
        }
    }

    private static string GetSyntaxLanguage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "C#",
            ".js" or ".mjs" or ".cjs" => "JavaScript",
            ".ts" or ".tsx" => "TypeScript",
            ".py" or ".pyw" => "Python",
            ".java" => "Java",
            ".cpp" or ".cxx" or ".cc" or ".hpp" or ".hxx" => "C++",
            ".c" or ".h" => "C",
            ".rs" => "Rust",
            ".go" => "Go",
            ".rb" or ".rake" => "Ruby",
            ".html" or ".htm" => "HTML",
            ".css" or ".scss" or ".less" => "CSS",
            ".xml" or ".xaml" or ".xsl" or ".xsd" => "XML",
            ".json" => "JSON",
            ".yaml" or ".yml" => "YAML",
            ".md" => "Markdown",
            ".sql" => "SQL",
            ".ps1" => "PowerShell",
            ".sh" or ".bash" => "Shell",
            ".bat" or ".cmd" => "Batch",
            _ => string.Empty
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _byteBuffer?.Dispose();
        Content = string.Empty;

        GC.SuppressFinalize(this);
    }
}
