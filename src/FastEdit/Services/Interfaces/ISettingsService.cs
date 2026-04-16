namespace FastEdit.Services.Interfaces;

public interface ISettingsService
{
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
    public bool IsUntitled { get; set; }
    public bool IsBinaryMode { get; set; }
    public string? TempFilePath { get; set; }
    public string? Content { get; set; }
    public int CursorOffset { get; set; }
    public double ScrollOffset { get; set; }
}
