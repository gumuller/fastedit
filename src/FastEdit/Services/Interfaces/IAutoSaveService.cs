namespace FastEdit.Services.Interfaces;

public interface IAutoSaveService
{
    bool IsEnabled { get; set; }
    int IntervalSeconds { get; set; }

    void Start();
    void Stop();
    void MarkCleanShutdown();
    bool HasRecoveryFiles();
    List<AutoSaveEntry> GetRecoveryEntries();
    void ClearRecoveryFiles();
    void SaveNow(IEnumerable<AutoSaveEntry> entries);
}

public record AutoSaveEntry(
    string Id,
    string FileName,
    string? FilePath,
    string Content,
    bool IsUntitled,
    int CursorOffset = 0,
    double ScrollOffset = 0);
