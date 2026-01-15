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
    private async Task SaveAsync()
    {
        if (IsBinaryMode)
        {
            if (_byteBuffer != null)
            {
                _byteBuffer.Save();
                IsModified = false;
            }
            return;
        }

        await _fileService.WriteAllTextAsync(FilePath, Content);
        IsModified = false;
    }

    [RelayCommand]
    private void ToggleMode()
    {
        if (IsBinaryMode)
        {
            // Switching from binary to text
            _byteBuffer?.Dispose();
            _byteBuffer = null;
            // Would need to reload as text
        }
        else
        {
            // Switching from text to binary
            _byteBuffer = new VirtualizedByteBuffer(FilePath);
        }

        IsBinaryMode = !IsBinaryMode;
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
