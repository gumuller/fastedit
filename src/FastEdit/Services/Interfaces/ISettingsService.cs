namespace FastEdit.Services.Interfaces;

public interface ISettingsService
{
    string ThemeName { get; set; }
    string? LastOpenedFolder { get; set; }
    List<SessionFile> OpenFiles { get; set; }
    void Save();
}

public class SessionFile
{
    public string FilePath { get; set; } = string.Empty;
    public bool IsUntitled { get; set; }
    public string? TempFilePath { get; set; }
    public string? Content { get; set; }
}
