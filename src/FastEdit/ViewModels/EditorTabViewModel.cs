using System.IO;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FastEdit.Core.FileAnalysis;
using FastEdit.Core.HexEngine;
using FastEdit.Core.LargeFile;
using FastEdit.Infrastructure;
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
    private bool _isSettingContentBaseline;
    private bool _isApplyingBinaryBaseline;

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
    private long _hexScrollOffset;

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
    public string AutoSaveIdentity { get; } = Guid.NewGuid().ToString("N");
    public long UserMutationVersion { get; private set; }
    public long ChangeVersion => UserMutationVersion;

    public EditorTabViewModel(IFileService fileService, IFileSystemService fileSystemService, IDialogService dialogService)
    {
        _fileService = fileService;
        _fileSystemService = fileSystemService;
        _dialogService = dialogService;
    }

    public void RestoreTextSnapshot(
        string content,
        string fileName,
        string? filePath,
        int encodingCodePage,
        bool hasBom,
        bool isModified)
    {
        ReleaseLoadedResources();
        Mode = FileOpenMode.Text;
        FileName = fileName;
        FilePath = filePath ?? string.Empty;
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _fileEncoding = System.Text.Encoding.GetEncoding(encodingCodePage);
        _hasBom = hasBom;
        Encoding = GetEncodingDisplayName(_fileEncoding, hasBom);
        SyntaxLanguage = string.IsNullOrEmpty(filePath)
            ? string.Empty
            : SyntaxLanguageResolver.Resolve(filePath);
        SetContentBaseline(content, isModified);
    }

    public void RestoreBinarySnapshot(
        byte[] bytes,
        string fileName,
        string? filePath,
        bool isModified)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ReleaseLoadedResources();
        Mode = FileOpenMode.Binary;
        FileName = fileName;
        FilePath = filePath ?? string.Empty;
        FileSize = bytes.LongLength;
        Encoding = "Binary";
        SyntaxLanguage = string.Empty;
        _byteBuffer = VirtualizedByteBuffer.FromSnapshot(bytes);
        _byteBuffer.ModificationsChanged += OnByteBufferModificationsChanged;
        IsModified = isModified;
    }

    public void RestoreBinaryOverlay(
        string filePath,
        long expectedLength,
        string expectedSha256,
        IReadOnlyList<BinaryModification> modifications,
        bool isModified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(modifications);
        var restoredBuffer = new VirtualizedByteBuffer(filePath);
        if (!restoredBuffer.MatchesBaseIdentity(expectedLength, expectedSha256))
        {
            restoredBuffer.Dispose();
            throw new InvalidDataException(
                $"Binary file '{filePath}' changed after its session snapshot was created.");
        }

        foreach (var modification in modifications)
            restoredBuffer.SetByte(modification.Offset, modification.Value);

        ReleaseLoadedResources();
        Mode = FileOpenMode.Binary;
        FileName = Path.GetFileName(filePath);
        FilePath = filePath;
        FileSize = new FileInfo(filePath).Length;
        Encoding = "Binary";
        SyntaxLanguage = string.Empty;
        _byteBuffer = restoredBuffer;
        _byteBuffer.ModificationsChanged += OnByteBufferModificationsChanged;
        IsModified = isModified;
    }

    public async Task LoadFileAsync(string filePath, IProgress<double>? indexProgress = null)
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
            _byteBuffer.ModificationsChanged += OnByteBufferModificationsChanged;
        }
        else if (Mode == FileOpenMode.LargeText)
        {
            _largeFileDoc = new LargeFileDocument(filePath);
            Encoding = _largeFileDoc.EncodingDisplayName;
            SyntaxLanguage = string.Empty; // no highlighting for huge files
            await _largeFileDoc.BuildIndexAsync(indexProgress, CancellationToken.None);
        }
        else
        {
            Mode = FileOpenMode.Text;
            var result = await _fileService.ReadFileWithEncodingAsync(filePath);
            SetContentBaseline(result.Content, isModified: false);
            _fileEncoding = result.Encoding;
            _hasBom = result.HasBom;
            Encoding = GetEncodingDisplayName(result.Encoding, result.HasBom);
            SyntaxLanguage = SyntaxLanguageResolver.Resolve(filePath);
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
            if (_byteBuffer == null)
                return false;

            var binarySavePath = FilePath;
            if (string.IsNullOrEmpty(binarySavePath))
            {
                binarySavePath = _dialogService.ShowSaveFileDialog(
                    filter: FileDialogFilters,
                    defaultFileName: FileName);
                if (string.IsNullOrEmpty(binarySavePath))
                    return false;
            }

            SaveBinaryBuffer(binarySavePath);
            FilePath = binarySavePath;
            FileName = Path.GetFileName(binarySavePath);
            IsModified = false;
            return true;
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

        var changeVersion = UserMutationVersion;
        var content = Content;
        await _fileService.WriteFileWithEncodingAsync(savePath, content, _fileEncoding, _hasBom);

        // Update metadata after successful save
        FilePath = savePath;
        FileName = Path.GetFileName(savePath);
        SyntaxLanguage = Mode == FileOpenMode.Text ? SyntaxLanguageResolver.Resolve(savePath) : string.Empty;
        IsModified = UserMutationVersion != changeVersion;
        return !IsModified;
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
            if (_byteBuffer == null)
                return false;

            SaveBinaryBuffer(savePath);
        }
        else if (Mode == FileOpenMode.LargeText)
        {
            if (string.IsNullOrEmpty(FilePath))
                return false;

            if (!string.Equals(savePath, FilePath, StringComparison.Ordinal))
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
            var changeVersion = UserMutationVersion;
            var content = Content;
            await _fileService.WriteFileWithEncodingAsync(savePath, content, _fileEncoding, _hasBom);
            IsModified = UserMutationVersion != changeVersion;
        }

        FilePath = savePath;
        FileName = Path.GetFileName(savePath);
        SyntaxLanguage = SyntaxLanguageResolver.Resolve(savePath);
        if (Mode != FileOpenMode.Text)
            IsModified = false;
        return !IsModified;
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
            SetContentBaseline(result.Content, isModified: false);
            _fileEncoding = result.Encoding;
            _hasBom = result.HasBom;
            Encoding = GetEncodingDisplayName(result.Encoding, result.HasBom);
            SyntaxLanguage = SyntaxLanguageResolver.Resolve(FilePath);
        }
        else if (Mode == FileOpenMode.Text || Mode == FileOpenMode.LargeText)
        {
            // Text → Binary: create byte buffer
            _largeFileDoc?.Dispose();
            _largeFileDoc = null;
            Mode = FileOpenMode.Binary;
            SetContentBaseline(string.Empty, isModified: false);
            Encoding = "Binary";
            SyntaxLanguage = string.Empty;
            _byteBuffer = new VirtualizedByteBuffer(FilePath);
            _byteBuffer.ModificationsChanged += OnByteBufferModificationsChanged;
        }
    }

    private void OnByteBufferModificationsChanged(object? sender, EventArgs e)
    {
        if (!_isApplyingBinaryBaseline && _byteBuffer?.HasModifications == true)
            UserMutationVersion++;
        IsModified = _byteBuffer?.HasModifications == true;
    }

    private void SaveBinaryBuffer(string savePath)
    {
        if (_byteBuffer == null)
            return;

        _isApplyingBinaryBaseline = true;
        try
        {
            var sourceBuffer = _byteBuffer;
            try
            {
                sourceBuffer.SaveTo(savePath);
                if (sourceBuffer.IsBackedBy(savePath))
                    return;

                var replacement = new VirtualizedByteBuffer(savePath);
                sourceBuffer.DiscardModifications();
                sourceBuffer.ModificationsChanged -= OnByteBufferModificationsChanged;
                sourceBuffer.Dispose();
                _byteBuffer = replacement;
                OnPropertyChanged(nameof(ByteBuffer));
            }
            catch
            {
                IsModified = true;
                throw;
            }

            _byteBuffer.ModificationsChanged += OnByteBufferModificationsChanged;
        }
        finally
        {
            _isApplyingBinaryBaseline = false;
        }
    }

    partial void OnContentChanged(string value)
    {
        if (!_isSettingContentBaseline)
        {
            UserMutationVersion++;
            IsModified = true;
        }
    }

    public void SetContentBaseline(string content, bool isModified)
    {
        _isSettingContentBaseline = true;
        try
        {
            Content = content;
            IsModified = isModified;
        }
        finally
        {
            _isSettingContentBaseline = false;
        }
    }

    public void ReplaceContentFromDisk(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        SetContentBaseline(content, isModified: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ReleaseLoadedResources();
        SetContentBaseline(string.Empty, isModified: false);

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
