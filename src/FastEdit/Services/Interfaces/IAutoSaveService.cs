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
    double ScrollOffset = 0)
{
    public int SnapshotVersion { get; init; }
    public string SessionEntryId { get; init; } = "";
    public string TabIdentity { get; init; } = "";
    public bool IsBinaryMode { get; init; }
    public bool IsModified { get; init; } = true;
    public int EncodingCodePage { get; init; } = 65001;
    public bool HasBom { get; init; }
    public string? TextContentBase64 { get; init; }
    public string? BinaryContentBase64 { get; init; }
    public long? BinaryBaseLength { get; init; }
    public string? BinaryBaseSha256 { get; init; }
    public List<BinaryModification>? BinaryModifications { get; init; }
    public long HexOffset { get; init; }
    public long HexScrollOffset { get; init; }
    public int BytesPerRow { get; init; } = 16;
    public long LargeFileTopLine { get; init; } = 1;
}

public record RecoveryEntriesResult(
    bool Success,
    IReadOnlyList<AutoSaveEntry> Entries,
    string? ErrorMessage = null);
