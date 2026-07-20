namespace FastEdit.Services.Interfaces;

using FastEdit.ViewModels;

public interface ISettingsService
{
    event EventHandler? AutoSaveIntervalChanged;

    string ThemeName { get; set; }
    string? LastOpenedFolder { get; set; }
    List<SessionFile> OpenFiles { get; set; }
    int ActiveTabIndex { get; set; }
    List<string> RecentFiles { get; set; }
    bool WordWrapEnabled { get; set; }
    bool ShowWhitespace { get; set; }
    bool CheckForUpdatesOnStartup { get; set; }
    int AutoSaveIntervalSeconds { get; set; }
    int TabSize { get; set; }
    bool UseTabs { get; set; }
    string CursorStyle { get; set; }  // Line, Block, Underscore, LineThin
    double EditorFontSize { get; set; }
    double WindowLeft { get; set; }
    double WindowTop { get; set; }
    double WindowWidth { get; set; }
    double WindowHeight { get; set; }
    bool WindowMaximized { get; set; }
    void Save();
    void AddRecentFile(string filePath);
}

public class SessionFile
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? TabIdentity { get; set; }
    public bool IsUntitled { get; set; }
    public bool IsBinaryMode { get; set; }
    public FileOpenMode Mode { get; set; } = FileOpenMode.Text;
    public bool IsModified { get; set; }
    public bool IsActive { get; set; }
    public string? TempFilePath { get; set; }
    public string? Content { get; set; }
    public string? SnapshotGeneration { get; set; }
    public string? SnapshotFile { get; set; }
    public string? SnapshotFormat { get; set; }
    public string? SnapshotOwner { get; set; }
    public List<string> SnapshotGenerationFiles { get; set; } = new();
    public string? BaseContentHash { get; set; }
    public int EncodingCodePage { get; set; } = System.Text.Encoding.UTF8.CodePage;
    public bool HasBom { get; set; }
    public int CursorOffset { get; set; }
    public double ScrollOffset { get; set; }
    public long HexOffset { get; set; }
    public int BytesPerRow { get; set; } = 16;
    public long LargeFileTopLine { get; set; } = 1;
}
