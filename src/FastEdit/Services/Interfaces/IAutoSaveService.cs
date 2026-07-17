namespace FastEdit.Services.Interfaces;

public interface IAutoSaveService
{
    bool IsEnabled { get; set; }
    int IntervalSeconds { get; set; }

    void Start();
    void Stop();
    bool MarkCleanShutdown();
    bool HasRecoveryFiles();
    RecoveryEntriesResult GetRecoveryEntries();
    bool RecordRecoveredEntries(IEnumerable<string> entryIds);
    bool ClearRecoveryFiles();
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

public record RecoveryEntriesResult(
    bool Success,
    IReadOnlyList<AutoSaveEntry> Entries,
    string? ErrorMessage = null);
