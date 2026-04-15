using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Core.FileAnalysis;
using FastEdit.Core.HexEngine;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class EditorTabViewModel : ObservableObject, IDisposable
{
    private readonly IFileService _fileService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private VirtualizedByteBuffer? _byteBuffer;
    private bool _disposed;

    private Encoding _fileEncoding = new UTF8Encoding(false);
    private bool _hasBom;

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

    // Session restore state
    [ObservableProperty]
    private int _cursorOffset;

    [ObservableProperty]
    private double _scrollOffset;

    public VirtualizedByteBuffer? ByteBuffer => _byteBuffer;

    public Encoding FileEncoding => _fileEncoding;
    public bool HasBom => _hasBom;

    public EditorTabViewModel(IFileService fileService, IFileSystemService fileSystemService, IDialogService dialogService)
    {
        _fileService = fileService;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
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

        if (IsBinaryMode)
        {
            Encoding = analysis.DetectedEncoding ?? "Binary";
            _byteBuffer = new VirtualizedByteBuffer(filePath);
            _byteBuffer.ModificationsChanged += (s, e) =>
            {
                IsModified = _byteBuffer.HasModifications;
            };
        }
        else
        {
            var result = await _fileService.ReadFileWithEncodingAsync(filePath);
            Content = result.Content;
            _fileEncoding = result.Encoding;
            _hasBom = result.HasBom;
            Encoding = GetEncodingDisplayName(result.Encoding, result.HasBom);
            SyntaxLanguage = GetSyntaxLanguage(filePath);
        }

        IsModified = false;
    }

    private static string GetEncodingDisplayName(System.Text.Encoding encoding, bool hasBom)
    {
        var name = encoding.WebName.ToUpperInvariant() switch
        {
            "UTF-8" => "UTF-8",
            "UTF-16" => "UTF-16 LE",
            "UTF-16BE" => "UTF-16 BE",
            "US-ASCII" => "ASCII",
            "ISO-8859-1" => "ISO 8859-1",
            "WINDOWS-1252" => "Windows-1252",
            _ => encoding.EncodingName
        };
        return hasBom ? $"{name} with BOM" : name;
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
            savePath = _dialogService.ShowSaveFileDialog(defaultFileName: FileName);
            if (string.IsNullOrEmpty(savePath))
                return false; // User cancelled
        }

        await _fileService.WriteFileWithEncodingAsync(savePath, Content, _fileEncoding, _hasBom);

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
        var savePath = _dialogService.ShowSaveFileDialog(defaultFileName: defaultName);
        if (string.IsNullOrEmpty(savePath))
            return false;

        if (IsBinaryMode)
        {
            if (_byteBuffer != null)
            {
                _byteBuffer.Save(); // saves to original path
                // For Save As in binary mode, copy file to new location
                if (savePath != FilePath)
                    _fileSystemService.CopyFile(FilePath, savePath, overwrite: true);
            }
            else
            {
                return false;
            }
        }
        else
        {
            await _fileService.WriteFileWithEncodingAsync(savePath, Content, _fileEncoding, _hasBom);
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
            var result = await _fileService.ReadFileWithEncodingAsync(FilePath);
            Content = result.Content;
            _fileEncoding = result.Encoding;
            _hasBom = result.HasBom;
            Encoding = GetEncodingDisplayName(result.Encoding, result.HasBom);
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
        // Check filename first for files without meaningful extensions
        var fileName = Path.GetFileName(filePath);
        var fileNameLower = fileName.ToLowerInvariant();

        var nameMatch = fileNameLower switch
        {
            "dockerfile" or "containerfile" => "Dockerfile",
            "makefile" or "gnumakefile" => "Makefile",
            ".gitignore" or ".gitattributes" or ".gitmodules" => "INI",
            ".editorconfig" => "INI",
            ".env" or ".env.local" or ".env.production" => "INI",
            "cmakelists.txt" => "CMake",
            _ => (string?)null
        };
        if (nameMatch != null) return nameMatch;

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
            ".xml" or ".xaml" or ".xsl" or ".xsd" or ".csproj" or ".fsproj" or ".vbproj" or ".props" or ".targets" => "XML",
            ".json" or ".jsonc" => "JSON",
            ".yaml" or ".yml" => "YAML",
            ".md" or ".markdown" => "Markdown",
            ".sql" => "SQL",
            ".ps1" or ".psm1" or ".psd1" => "PowerShell",
            ".sh" or ".bash" or ".zsh" or ".fish" => "Shell",
            ".bat" or ".cmd" => "Batch",
            ".toml" => "TOML",
            ".ini" or ".cfg" or ".conf" => "INI",
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
