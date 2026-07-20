namespace FastEdit.Services.Interfaces;

using System.IO;
using FastEdit.ViewModels;

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
    bool CompleteRecovery(
        IEnumerable<AutoSaveEntry> replacementEntries,
        IEnumerable<string> recoveredEntryIds,
        bool allEntriesRecovered);
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
    double ScrollOffset = 0,
    string? TabIdentity = null,
    FileOpenMode Mode = FileOpenMode.Text,
    bool IsModified = true,
    int EncodingCodePage = 65001,
    bool HasBom = false,
    long HexOffset = 0,
    int BytesPerRow = 16,
    long LargeFileTopLine = 1,
    string? ContentFormat = null,
    string? ContentHash = null,
    long ContentLength = -1,
    string? ContentPath = null,
    Action<Stream>? WriteContent = null,
    Func<string, bool>? AdoptPersistedContent = null);

public record RecoveryEntriesResult(
    bool Success,
    IReadOnlyList<AutoSaveEntry> Entries,
    string? ErrorMessage = null);
