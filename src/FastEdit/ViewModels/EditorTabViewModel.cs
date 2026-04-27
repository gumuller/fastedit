using System.IO;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Core.FileAnalysis;
using FastEdit.Core.HexEngine;
using FastEdit.Core.LargeFile;
using FastEdit.Services.Interfaces;

namespace FastEdit.ViewModels;

public partial class EditorTabViewModel : ObservableObject, IDisposable
{
    /// <summary>Files at or above this size open in LargeText mode (MMF-backed read-only viewer).</summary>
    public const long LargeFileThresholdBytes = 100L * 1024 * 1024;

    private readonly IFileService _fileService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IDialogService _dialogService;
    private VirtualizedByteBuffer? _byteBuffer;
    private LargeFileDocument? _largeFileDoc;
    private bool _disposed;

    private Encoding _fileEncoding = new UTF8Encoding(false);
    private bool _hasBom;

    internal const string FileDialogFilters =
        "All Files (*.*)|*.*|" +
        "Text Files (*.txt)|*.txt|" +
        "C# Files (*.cs)|*.cs|" +
        "XML Files (*.xml)|*.xml|" +
        "JSON Files (*.json)|*.json|" +
        "JavaScript (*.js)|*.js|" +
        "TypeScript (*.ts;*.tsx)|*.ts;*.tsx|" +
        "HTML Files (*.html;*.htm)|*.html;*.htm|" +
        "CSS Files (*.css)|*.css|" +
        "Python Files (*.py)|*.py|" +
        "Markdown (*.md)|*.md|" +
        "YAML (*.yml;*.yaml)|*.yml;*.yaml|" +
        "SQL Files (*.sql)|*.sql|" +
        "PowerShell (*.ps1)|*.ps1|" +
        "Batch Files (*.bat;*.cmd)|*.bat;*.cmd|" +
        "Log Files (*.log)|*.log|" +
        "INI Files (*.ini;*.cfg)|*.ini;*.cfg|" +
        "CSV Files (*.csv)|*.csv";

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private FileOpenMode _mode = FileOpenMode.Text;

    public bool IsBinaryMode => Mode == FileOpenMode.Binary;
    public bool IsLargeFileMode => Mode == FileOpenMode.LargeText;

    partial void OnModeChanged(FileOpenMode value)
    {
        OnPropertyChanged(nameof(IsBinaryMode));
        OnPropertyChanged(nameof(IsLargeFileMode));
    }

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

    [ObservableProperty]
    private string _indentInfo = "Spaces: 4";

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
    public LargeFileDocument? LargeFileDoc => _largeFileDoc;

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
        ReleaseLoadedResources();

        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Content = string.Empty;
        SyntaxLanguage = string.Empty;

        var fileInfo = new FileInfo(filePath);
        FileSize = fileInfo.Length;

        // Detect if binary
        var detector = new BinaryDetector();
        var analysis = await detector.AnalyzeFileAsync(filePath);

        Mode = FileOpenModeRouter.SelectOpenMode(FileSize, analysis);

        if (Mode == FileOpenMode.Binary)
        {
            Encoding = analysis.DetectedEncoding ?? "Binary";
            _byteBuffer = new VirtualizedByteBuffer(filePath);
            _byteBuffer.ModificationsChanged += (s, e) =>
            {
                IsModified = _byteBuffer.HasModifications;
            };
        }
        else if (Mode == FileOpenMode.LargeText)
        {
            _largeFileDoc = new LargeFileDocument(filePath);
            Encoding = _largeFileDoc.EncodingDisplayName;
            SyntaxLanguage = string.Empty; // no highlighting for huge files
            await _largeFileDoc.BuildIndexAsync(null, CancellationToken.None);
        }
        else
        {
            Mode = FileOpenMode.Text;
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
        if (Mode == FileOpenMode.Binary)
        {
            if (_byteBuffer != null)
            {
                _byteBuffer.Save();
                IsModified = false;
                return true;
            }
            return false;
        }

        if (Mode == FileOpenMode.LargeText)
        {
            return true;
        }

        var savePath = FilePath;

        // If untitled (no file path), prompt for Save As
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = _dialogService.ShowSaveFileDialog(
                filter: FileDialogFilters,
                defaultFileName: FileName);
            if (string.IsNullOrEmpty(savePath))
                return false; // User cancelled
        }

        await _fileService.WriteFileWithEncodingAsync(savePath, Content, _fileEncoding, _hasBom);

        // Update metadata after successful save
        FilePath = savePath;
        FileName = Path.GetFileName(savePath);
        SyntaxLanguage = Mode == FileOpenMode.Text ? GetSyntaxLanguage(savePath) : string.Empty;
        IsModified = false;
        return true;
    }

    [RelayCommand]
    private async Task<bool> SaveAsAsync()
    {
        var defaultName = string.IsNullOrEmpty(FilePath) ? FileName : Path.GetFileName(FilePath);
        var savePath = _dialogService.ShowSaveFileDialog(
            filter: FileDialogFilters,
            defaultFileName: defaultName);
        if (string.IsNullOrEmpty(savePath))
            return false;

        if (Mode == FileOpenMode.Binary)
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
        else if (Mode == FileOpenMode.LargeText)
        {
            if (string.IsNullOrEmpty(FilePath))
                return false;

            if (!string.Equals(savePath, FilePath, StringComparison.OrdinalIgnoreCase))
            {
                _fileSystemService.CopyFile(FilePath, savePath, overwrite: true);

                _largeFileDoc?.Dispose();
                _largeFileDoc = new LargeFileDocument(savePath);
                Encoding = _largeFileDoc.EncodingDisplayName;
                await _largeFileDoc.BuildIndexAsync(null, CancellationToken.None);
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

        if (Mode == FileOpenMode.Binary)
        {
            // Binary → Text: dispose buffer, reload as text
            _byteBuffer?.Dispose();
            _byteBuffer = null;

            _largeFileDoc?.Dispose();
            _largeFileDoc = null;

            Mode = FileOpenModeRouter.SelectTextMode(FileSize);
            if (Mode == FileOpenMode.LargeText)
            {
                _largeFileDoc = new LargeFileDocument(FilePath);
                Encoding = _largeFileDoc.EncodingDisplayName;
                SyntaxLanguage = string.Empty;
                await _largeFileDoc.BuildIndexAsync(null, CancellationToken.None);
                return;
            }

            var result = await _fileService.ReadFileWithEncodingAsync(FilePath);
            Content = result.Content;
            _fileEncoding = result.Encoding;
            _hasBom = result.HasBom;
            Encoding = GetEncodingDisplayName(result.Encoding, result.HasBom);
            SyntaxLanguage = GetSyntaxLanguage(FilePath);
        }
        else if (Mode == FileOpenMode.Text || Mode == FileOpenMode.LargeText)
        {
            // Text → Binary: create byte buffer
            _largeFileDoc?.Dispose();
            _largeFileDoc = null;
            Mode = FileOpenMode.Binary;
            Content = string.Empty;
            Encoding = "Binary";
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

        ReleaseLoadedResources();
        Content = string.Empty;

        GC.SuppressFinalize(this);
    }

    private void ReleaseLoadedResources()
    {
        _byteBuffer?.Dispose();
        _byteBuffer = null;

        _largeFileDoc?.Dispose();
        _largeFileDoc = null;
    }
}
