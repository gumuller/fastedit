namespace FastEdit.Services.Interfaces;

public interface ISettingsService
{
    event EventHandler? AutoSaveIntervalChanged;

    string ThemeName { get; set; }
    string? LastOpenedFolder { get; set; }
    List<SessionFile> OpenFiles { get; set; }
    int ActiveTabIndex { get; set; }
    string? ActiveSessionEntryId { get; set; }
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
    public string EntryId { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = string.Empty;
    public bool IsUntitled { get; set; }
    public bool IsBinaryMode { get; set; }
    public string? TempFilePath { get; set; }
    public string? Content { get; set; }
    public string? BinaryContentBase64 { get; set; }
    public long? BinaryBaseLength { get; set; }
    public string? BinaryBaseSha256 { get; set; }
    public List<BinaryModification>? BinaryModifications { get; set; }
    public int EncodingCodePage { get; set; } = 65001;
    public bool HasBom { get; set; }
    public int SnapshotVersion { get; set; }
    public bool IsModified { get; set; }
    public int CursorOffset { get; set; }
    public double ScrollOffset { get; set; }
    public long HexOffset { get; set; }
    public long HexScrollOffset { get; set; }
    public int BytesPerRow { get; set; } = 16;
}

public class BinaryModification
{
    public long Offset { get; set; }
    public byte Value { get; set; }
}
