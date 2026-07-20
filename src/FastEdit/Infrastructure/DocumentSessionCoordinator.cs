using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public sealed class DocumentSessionCoordinator : IDisposable
{
    private const int MaxPersistedTabIdentityLength = 128;
    private const int MaxIdentityGenerationAttempts = 32;
    private const int TextSnapshotHeaderLength = 12;
    private const int MaxTextSnapshotCharacters = 128 * 1024 * 1024;
    private const long MaxLegacyTextSnapshotBytes =
        (long)MaxTextSnapshotCharacters * sizeof(char) * 2;
    private const string ExactTextSnapshotFormat = "utf16-code-units-v1";
    private static readonly byte[] TextSnapshotMagic =
        "FETXT001"u8.ToArray();
    private static readonly StringComparer StorageIdentityComparer =
        StringComparer.OrdinalIgnoreCase;
    private readonly ISettingsService _settingsService;
    private readonly IShutdownSessionStore _shutdownSessionStore;
    private readonly IFileSystemService _fileSystemService;
    private readonly IEditorTabFactory _tabFactory;
    private readonly Func<string> _tabIdentityGenerator;
    private readonly Func<string> _fallbackTabIdentityGenerator;
    private readonly Func<string> _shutdownGenerationGenerator;
    private readonly string _shutdownSnapshotRoot;
    private readonly string _shutdownOwner;
    private readonly HashSet<EditorTabViewModel> _shutdownDiscardedTabs = new();
    private readonly List<SessionFile> _unresolvedShutdownEntries = new();
    private readonly List<SessionFile> _pendingShutdownEntries = new();
    private readonly HashSet<string> _replacedShutdownOwners =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Stream> _shutdownGenerationLeases =
        new(StorageIdentityComparer);
    private readonly HashSet<string> _snapshotlessRetirementCandidates =
        new(StorageIdentityComparer);
    private readonly List<SessionFile> _retirementCandidates = new();
    private bool _shutdownRestoreStarted;
    private bool _shutdownRestoreFailed;

    public DocumentSessionCoordinator(
        ISettingsService settingsService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory)
        : this(
            settingsService,
            fileSystemService,
            tabFactory,
            new LegacyShutdownSessionStore(settingsService),
            () => Guid.NewGuid().ToString("N"),
            () => Guid.NewGuid().ToString("N"),
            () => Guid.NewGuid().ToString("N"),
            GetDefaultShutdownSnapshotRoot())
    {
    }

    public DocumentSessionCoordinator(
        ISettingsService settingsService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory,
        IShutdownSessionStore shutdownSessionStore)
        : this(
            settingsService,
            fileSystemService,
            tabFactory,
            shutdownSessionStore,
            () => Guid.NewGuid().ToString("N"),
            () => Guid.NewGuid().ToString("N"),
            () => Guid.NewGuid().ToString("N"),
            GetDefaultShutdownSnapshotRoot())
    {
    }

    internal DocumentSessionCoordinator(
        ISettingsService settingsService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory,
        Func<string> tabIdentityGenerator,
        Func<string>? fallbackTabIdentityGenerator = null)
        : this(
            settingsService,
            fileSystemService,
            tabFactory,
            new LegacyShutdownSessionStore(settingsService),
            tabIdentityGenerator,
            fallbackTabIdentityGenerator,
            () => Guid.NewGuid().ToString("N"),
            GetDefaultShutdownSnapshotRoot())
    {
    }

    internal DocumentSessionCoordinator(
        ISettingsService settingsService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory,
        IShutdownSessionStore shutdownSessionStore,
        Func<string> tabIdentityGenerator,
        Func<string>? fallbackTabIdentityGenerator,
        Func<string> shutdownGenerationGenerator,
        string shutdownSnapshotRoot,
        Func<string>? shutdownOwnerGenerator = null)
    {
        _settingsService = settingsService;
        _shutdownSessionStore = shutdownSessionStore;
        _fileSystemService = fileSystemService;
        _tabFactory = tabFactory;
        _tabIdentityGenerator = tabIdentityGenerator;
        _fallbackTabIdentityGenerator = fallbackTabIdentityGenerator ??
            (() => Guid.NewGuid().ToString("N"));
        _shutdownGenerationGenerator = shutdownGenerationGenerator;
        _shutdownSnapshotRoot = shutdownSnapshotRoot;
        _shutdownOwner = (shutdownOwnerGenerator ??
            (() => Guid.NewGuid().ToString("N")))();
        if (!HasValidPersistedIdentity(_shutdownOwner))
            throw new InvalidOperationException(
                "The shutdown snapshot owner is not storage-safe.");
    }

    public SessionData CreateNamedSession(
        IReadOnlyList<EditorTabViewModel> tabs,
        EditorTabViewModel? selectedTab)
    {
        var session = new SessionData();
        foreach (var tab in tabs)
        {
            session.Files.Add(new SessionFileEntry
            {
                FilePath = string.IsNullOrEmpty(tab.FilePath) ? tab.FileName : tab.FilePath,
                TabIdentity = tab.AutoSaveIdentity,
                IsUntitled = string.IsNullOrEmpty(tab.FilePath),
                Content = string.IsNullOrEmpty(tab.FilePath) ? tab.Content : null,
                CursorOffset = tab.CursorOffset,
                ScrollOffset = tab.ScrollOffset,
                HexOffset = tab.HexOffset,
                BytesPerRow = tab.BytesPerRow,
                LargeFileTopLine = tab.LargeFileTopLine
            });
        }

        session.ActiveTabIndex = 0;
        if (selectedTab != null)
        {
            session.ActiveTabIndex = -1;
            for (var index = 0; index < tabs.Count; index++)
            {
                if (tabs[index] != selectedTab)
                    continue;

                session.ActiveTabIndex = index;
                break;
            }
        }
        return session;
    }

    public async Task<StagedDocumentSession> StageNamedSessionAsync(SessionData session)
    {
        var stagedTabs = new List<EditorTabViewModel>(session.Files.Count);
        try
        {
            foreach (var entry in session.Files)
            {
                EditorTabViewModel tab;
                if (entry.IsUntitled)
                {
                    tab = _tabFactory.CreateUntitled(entry.Content ?? string.Empty);
                    tab.FileName = Path.GetFileName(entry.FilePath);
                    stagedTabs.Add(tab);
                }
                else
                {
                    if (!_fileSystemService.FileExists(entry.FilePath))
                        throw new FileNotFoundException("Session file was not found.", entry.FilePath);

                    tab = _tabFactory.Create();
                    stagedTabs.Add(tab);
                    await tab.LoadFileAsync(entry.FilePath);
                }

                tab.CursorOffset = entry.CursorOffset;
                tab.ScrollOffset = entry.ScrollOffset;
                tab.HexOffset = entry.HexOffset;
                tab.BytesPerRow = entry.BytesPerRow;
                tab.LargeFileTopLine = entry.LargeFileTopLine;
            }

            var descriptors = stagedTabs
                .Select((tab, index) => PersistedTabDescriptor.FromTab(
                    index,
                    session.Files[index].TabIdentity,
                    tab))
                .ToArray();
            var plan = PlanPersistedTabBatch(
                descriptors,
                Array.Empty<EditorTabViewModel>());
            var adoptedTabs = new List<EditorTabViewModel>(plan.Candidates.Count);
            var adoptedIndexes = new Dictionary<int, int>();
            foreach (var candidate in plan.Candidates)
            {
                var tab = stagedTabs[candidate.Descriptor.SourceIndex];
                tab.RestoreAutoSaveIdentity(candidate.AssignedIdentity);
                adoptedIndexes[candidate.Descriptor.SourceIndex] = adoptedTabs.Count;
                adoptedTabs.Add(tab);
            }

            foreach (var duplicate in plan.Duplicates)
                stagedTabs[duplicate.Descriptor.SourceIndex].Dispose();

            var activeTabIndex = ResolvePlannedActiveIndex(
                session.ActiveTabIndex,
                adoptedTabs.Count,
                adoptedIndexes,
                plan.Duplicates);
            return new StagedDocumentSession(adoptedTabs, activeTabIndex);
        }
        catch
        {
            foreach (var tab in stagedTabs)
                tab.Dispose();
            throw;
        }
    }

    public WorkspaceMutationSnapshot CaptureWorkspace(
        IReadOnlyList<EditorTabViewModel> tabs) =>
        new(tabs.ToDictionary(tab => tab, tab => tab.UserMutationVersion));

    public bool HasUnsavedChanges(IReadOnlyList<EditorTabViewModel> tabs) =>
        tabs.Any(tab => tab.IsModified);

    public IReadOnlyList<EditorTabViewModel> GetUnsavedTabs(
        IReadOnlyList<EditorTabViewModel> tabs) =>
        tabs.Where(tab => tab.IsModified).ToList();

    public void BeginShutdownPreparation() => _shutdownDiscardedTabs.Clear();

    public void CancelShutdownPreparation() => _shutdownDiscardedTabs.Clear();

    public ShutdownRestoreStatus ShutdownRestoreStatus =>
        new(
            _pendingShutdownEntries.Count,
            _unresolvedShutdownEntries.Count,
            _shutdownRestoreFailed);

    public async Task<UnsavedChangesPreparationResult> PrepareUnsavedChangesAsync(
        IReadOnlyList<EditorTabViewModel> tabs,
        IReadOnlyList<EditorTabViewModel> unsavedTabs,
        UnsavedChangesDecision decision,
        bool recordShutdownDiscards)
    {
        if (decision == UnsavedChangesDecision.Cancel)
            return new UnsavedChangesPreparationResult(false);

        if (decision == UnsavedChangesDecision.Discard)
        {
            if (recordShutdownDiscards)
            {
                foreach (var tab in unsavedTabs)
                    _shutdownDiscardedTabs.Add(tab);
            }

            return new UnsavedChangesPreparationResult(true);
        }

        var approvedSnapshot = CaptureWorkspace(tabs);
        foreach (var tab in unsavedTabs)
        {
            try
            {
                await tab.SaveCommand.ExecuteAsync(null);
                if (tab.IsModified)
                    return new UnsavedChangesPreparationResult(false);
            }
            catch (Exception ex)
            {
                return new UnsavedChangesPreparationResult(
                    false,
                    $"Error saving {tab.FileName}: {ex.Message}");
            }
        }

        if (approvedSnapshot.HasChanged(tabs))
        {
            return new UnsavedChangesPreparationResult(
                false,
                "Files changed while saving; close or load the session again.");
        }

        return new UnsavedChangesPreparationResult(true);
    }

    public async Task<RestoredDocumentSession> RestoreShutdownSessionAsync()
    {
        var restoredCandidates = new List<RestoredTabCandidate>();
        _shutdownRestoreStarted = true;
        _shutdownRestoreFailed = false;
        _unresolvedShutdownEntries.Clear();
        _pendingShutdownEntries.Clear();
        ShutdownSessionState persistedSession;
        try
        {
            persistedSession = _shutdownSessionStore.ReadShutdownSession(
                AcquireShutdownGenerationLeases);
        }
        catch
        {
            _shutdownRestoreFailed = true;
            throw;
        }
        var openFiles = persistedSession.Files;
        var activeTabIndex = -1;
        for (var index = 0; index < openFiles.Count; index++)
        {
            if (openFiles[index].IsActive)
            {
                activeTabIndex = index;
                break;
            }
        }
        if (activeTabIndex < 0)
            activeTabIndex = persistedSession.ActiveTabIndex;
        if (openFiles == null || openFiles.Count == 0)
            return new RestoredDocumentSession(
                restoredCandidates,
                activeTabIndex);

        for (var index = 0; index < openFiles.Count; index++)
        {
            var sessionFile = openFiles[index];
            try
            {
                var tab = await RestoreSessionFileAsync(sessionFile);
                if (tab != null)
                {
                    tab.CursorOffset = sessionFile.CursorOffset;
                    tab.ScrollOffset = sessionFile.ScrollOffset;
                    tab.HexOffset = sessionFile.HexOffset;
                    tab.BytesPerRow = sessionFile.BytesPerRow;
                    tab.LargeFileTopLine = sessionFile.LargeFileTopLine;
                    restoredCandidates.Add(
                        new RestoredTabCandidate(
                            tab,
                            index,
                            sessionFile.TabIdentity,
                            CloneSessionFile(sessionFile)));
                    TrackPendingShutdownEntry(sessionFile);
                }
                else
                    TrackUnresolvedShutdownEntry(sessionFile);
            }
            catch (Exception ex)
            {
                TrackUnresolvedShutdownEntry(sessionFile);
                Trace.TraceWarning(
                    "Failed to restore session file '{0}': {1}",
                    sessionFile.FilePath,
                    ex.Message);
            }
        }

        return new RestoredDocumentSession(
            restoredCandidates,
            activeTabIndex);
    }

    public RestoredSessionAdoptionResult AdoptRestoredTabs(
        RestoredDocumentSession restoredSession,
        IReadOnlyList<EditorTabViewModel> liveTabs,
        Action<EditorTabViewModel> adoptTab)
    {
        var candidates = restoredSession.TakeCandidates();
        var descriptors = candidates
            .Select((candidate, index) => PersistedTabDescriptor.FromTab(
                index,
                candidate.TabIdentity,
                candidate.Tab))
            .ToArray();
        PersistedTabBatchPlan plan;
        try
        {
            plan = PlanPersistedTabBatch(descriptors, liveTabs);
        }
        catch
        {
            foreach (var candidate in candidates)
            {
                if (candidate.SourceEntry != null)
                    TrackUnresolvedShutdownEntry(candidate.SourceEntry);
            }
            foreach (var candidate in candidates)
                candidate.Tab.Dispose();
            throw;
        }
        var plannedBySource = plan.Candidates.ToDictionary(
            candidate => candidate.Descriptor.SourceIndex);
        var duplicatesBySource = plan.Duplicates.ToDictionary(
            duplicate => duplicate.Descriptor.SourceIndex);
        var adoptedTabs = new List<EditorTabViewModel>(candidates.Count);
        var discardedDuplicates = new List<EditorTabViewModel>();
        var ownersBySessionIndex = new Dictionary<int, EditorTabViewModel>();
        EditorTabViewModel? selectedTab = null;

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (duplicatesBySource.TryGetValue(index, out var duplicate))
            {
                candidate.Tab.Dispose();
                discardedDuplicates.Add(candidate.Tab);
                TrackConsumedShutdownEntry(candidate.SourceEntry);
                var duplicateOwner = duplicate.Owner.LiveTab ??
                    candidates[duplicate.Owner.PlannedSourceIndex!.Value].Tab;
                ownersBySessionIndex[candidate.SessionIndex] = duplicateOwner;
                if (candidate.SessionIndex == restoredSession.ActiveTabIndex)
                    selectedTab = duplicateOwner;
                continue;
            }

            var planned = plannedBySource[index];
            candidate.Tab.RestoreAutoSaveIdentity(planned.AssignedIdentity);
            if (string.IsNullOrEmpty(candidate.Tab.FilePath))
            {
                candidate.Tab.FileName = UntitledTabNameAllocator.Allocate(
                    liveTabs,
                    candidate.Tab.FileName);
            }

            try
            {
                adoptTab(candidate.Tab);
                adoptedTabs.Add(candidate.Tab);
                TrackConsumedShutdownEntry(candidate.SourceEntry);
                ownersBySessionIndex[candidate.SessionIndex] = candidate.Tab;
                if (candidate.SessionIndex == restoredSession.ActiveTabIndex)
                    selectedTab = candidate.Tab;
            }
            catch
            {
                if (!liveTabs.Contains(candidate.Tab) &&
                    candidate.SourceEntry != null)
                {
                    TrackUnresolvedShutdownEntry(candidate.SourceEntry);
                }
                else if (liveTabs.Contains(candidate.Tab))
                {
                    TrackConsumedShutdownEntry(candidate.SourceEntry);
                }
                foreach (var remaining in candidates.Skip(index + 1))
                {
                    if (remaining.SourceEntry != null)
                        TrackUnresolvedShutdownEntry(remaining.SourceEntry);
                }
                if (!liveTabs.Contains(candidate.Tab))
                    candidate.Tab.Dispose();
                foreach (var remaining in candidates.Skip(index + 1))
                    remaining.Tab.Dispose();
                throw;
            }
        }

        if (selectedTab == null && ownersBySessionIndex.Count > 0)
        {
            selectedTab = ownersBySessionIndex
                .OrderBy(item => Math.Abs(
                    (long)item.Key - restoredSession.ActiveTabIndex))
                .ThenBy(item => item.Key)
                .First()
                .Value;
        }
        else if (selectedTab == null && liveTabs.Count > 0)
        {
            selectedTab = liveTabs[
                Math.Clamp(restoredSession.ActiveTabIndex, 0, liveTabs.Count - 1)];
        }

        return new RestoredSessionAdoptionResult(
            adoptedTabs,
            discardedDuplicates,
            selectedTab);
    }

    public void PersistShutdownSession(
        IReadOnlyList<EditorTabViewModel> tabs,
        EditorTabViewModel? selectedTab)
    {
        var mutationSnapshot = CaptureWorkspace(tabs);
        var generation = _shutdownGenerationGenerator();
        if (!HasValidPersistedIdentity(generation))
        {
            throw new InvalidOperationException(
                "The shutdown snapshot generation is not storage-safe.");
        }

        var generationDirectory = GetShutdownGenerationDirectory(generation);
        var createdSnapshotPaths = new List<string>();
        var binaryRebases = new List<PreparedBinaryRebase>();
        var discardedBinaryTabs = new List<EditorTabViewModel>();
        var published = false;
        var binaryHandoffCompleted = false;
        try
        {
            _fileSystemService.CreateDirectory(generationDirectory);
            RejectReparsePoint(_shutdownSnapshotRoot);
            RejectReparsePoint(generationDirectory);
            var leaseMarkerPath = GetShutdownLeaseMarkerPath(generation);
            _fileSystemService.WriteStreamAtomic(leaseMarkerPath, _ => { });
            createdSnapshotPaths.Add(leaseMarkerPath);
            var carryForwardDurableSession =
                !_shutdownRestoreStarted || _shutdownRestoreFailed;
            var previousSession = _shutdownSessionStore.ReadShutdownSession(
                carryForwardDurableSession
                    ? AcquireShutdownGenerationLeases
                    : null);
            var sessionFiles = _unresolvedShutdownEntries
                .Concat(_pendingShutdownEntries)
                .Concat(
                    carryForwardDurableSession
                        ? previousSession.Files
                        : Array.Empty<SessionFile>())
                .DistinctBy(file => (
                    file.SnapshotGeneration,
                    file.SnapshotFile,
                    file.TabIdentity,
                    file.FilePath))
                .Select(CloneSessionFile)
                .ToList();
            var persistedActiveTabIndex = sessionFiles.Count == 0
                ? 0
                : Math.Clamp(
                    previousSession.ActiveTabIndex,
                    0,
                    sessionFiles.Count - 1);
            var usedIdentities = new HashSet<string>(StorageIdentityComparer);
            if (selectedTab != null)
            {
                foreach (var carriedEntry in sessionFiles)
                    carriedEntry.IsActive = false;
            }

            foreach (var tab in tabs)
            {
                var explicitlyDiscarded = _shutdownDiscardedTabs.Contains(tab);
                if (explicitlyDiscarded && string.IsNullOrEmpty(tab.FilePath))
                    continue;

                if (tab == selectedTab)
                    persistedActiveTabIndex = sessionFiles.Count;

                if (!HasValidPersistedIdentity(tab.AutoSaveIdentity) ||
                    !usedIdentities.Add(tab.AutoSaveIdentity))
                {
                    throw new InvalidOperationException(
                        $"Tab '{tab.FileName}' does not have a unique storage-safe identity.");
                }

                var sessionFile = CreateShutdownSessionFile(tab);
                if (explicitlyDiscarded)
                {
                    sessionFile.IsModified = false;
                    if (sessionFile.Mode == FileOpenMode.Binary)
                        discardedBinaryTabs.Add(tab);
                }
                sessionFile.IsActive = tab == selectedTab;
                if (!explicitlyDiscarded &&
                    RequiresShutdownSnapshot(sessionFile))
                {
                    var snapshotPath = PersistShutdownSnapshot(
                        tab,
                        sessionFile,
                        generation,
                        generationDirectory);
                    createdSnapshotPaths.Add(snapshotPath);
                    if (sessionFile.Mode == FileOpenMode.Binary)
                    {
                        binaryRebases.Add(new PreparedBinaryRebase(
                            tab,
                            snapshotPath,
                            sessionFile.IsModified,
                            tab.PrepareBinarySnapshot(snapshotPath)));
                    }
                }

                sessionFiles.Add(sessionFile);
            }

            var activeEntryIndex = sessionFiles.FindIndex(file => file.IsActive);
            if (activeEntryIndex >= 0)
                persistedActiveTabIndex = activeEntryIndex;
            var ownedGenerationFiles = sessionFiles
                .Where(file => string.Equals(
                    file.SnapshotGeneration,
                    generation,
                    StringComparison.Ordinal))
                .Select(file => file.SnapshotFile)
                .Where(file => !string.IsNullOrEmpty(file))
                .Cast<string>()
                .ToList();
            foreach (var sessionFile in sessionFiles.Where(file =>
                         string.Equals(
                             file.SnapshotGeneration,
                             generation,
                             StringComparison.Ordinal)))
            {
                sessionFile.SnapshotGenerationFiles = ownedGenerationFiles.ToList();
            }

            if (mutationSnapshot.HasChanged(tabs))
            {
                throw new InvalidOperationException(
                    "Files changed while the shutdown snapshot was being persisted.");
            }

            var publishedSession = new ShutdownSessionState(
                sessionFiles,
                persistedActiveTabIndex,
                _replacedShutdownOwners
                    .Append(_shutdownOwner)
                    .ToArray());
            _shutdownSessionStore.PublishShutdownSession(
                publishedSession,
                publication =>
                {
                    try
                    {
                        foreach (var rebase in binaryRebases)
                        {
                            rebase.Tab.RebaseBinarySnapshot(
                                rebase.PreparedBuffer,
                                rebase.Path,
                                rebase.IsModified);
                            rebase.MarkAdopted();
                        }
                        foreach (var discardedBinaryTab in discardedBinaryTabs)
                            discardedBinaryTab.ReleaseBinarySnapshotForShutdown();
                        binaryHandoffCompleted = true;
                        RetireUnreferencedShutdownGenerations(
                            publication.PreviousSession.Files
                                .Concat(_retirementCandidates)
                                .ToArray(),
                            publication.PublishedSession.Files);
                        RetireSnapshotlessShutdownGeneration(
                            generation,
                            publication.PublishedSession.Files);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning(
                            "Failed to complete shutdown snapshot handoff or retirement: {0}",
                            ex.Message);
                    }
                });
            published = true;
            if (!binaryHandoffCompleted)
            {
                throw new InvalidOperationException(
                    "The shutdown session was published, but binary snapshot handoff failed.");
            }
            if (ownedGenerationFiles.Count == 0)
            {
                DeleteUnpublishedShutdownGeneration(
                    generationDirectory,
                    createdSnapshotPaths);
            }
            _unresolvedShutdownEntries.Clear();
            _pendingShutdownEntries.Clear();
            _replacedShutdownOwners.Clear();
            _shutdownRestoreStarted = true;
            _shutdownRestoreFailed = false;
        }
        catch
        {
            foreach (var rebase in binaryRebases)
                rebase.Dispose();
            if (!published)
                DeleteUnpublishedShutdownGeneration(
                    generationDirectory,
                    createdSnapshotPaths);
            throw;
        }
    }

    public IReadOnlyList<AutoSaveEntry> CreateAutoSaveEntries(
        IReadOnlyList<EditorTabViewModel> tabs)
    {
        var entries = new List<AutoSaveEntry>();
        var usedIdentities = new HashSet<string>(StorageIdentityComparer);
        foreach (var tab in tabs)
        {
            if (!tab.IsModified && !string.IsNullOrEmpty(tab.FilePath))
                continue;

            if (!HasValidPersistedIdentity(tab.AutoSaveIdentity) ||
                !usedIdentities.Add(tab.AutoSaveIdentity))
            {
                throw new InvalidOperationException(
                    $"Tab '{tab.FileName}' does not have a unique storage-safe identity.");
            }

            entries.Add(new AutoSaveEntry(
                $"tab-{tab.AutoSaveIdentity}",
                tab.FileName,
                string.IsNullOrEmpty(tab.FilePath) ? null : NormalizeFilePath(tab.FilePath),
                tab.Content ?? string.Empty,
                string.IsNullOrEmpty(tab.FilePath),
                tab.CursorOffset,
                tab.ScrollOffset,
                tab.AutoSaveIdentity));
        }

        return entries;
    }

    public EditorTabViewModel CreateRecoveryTab(AutoSaveEntry entry)
    {
        var tab = _tabFactory.CreateUntitled(entry.Content);
        if (!string.IsNullOrEmpty(entry.TabIdentity))
            tab.RestoreAutoSaveIdentity(entry.TabIdentity);
        tab.FileName = entry.FileName;
        if (!entry.IsUntitled && !string.IsNullOrEmpty(entry.FilePath))
            tab.FilePath = entry.FilePath;
        tab.CursorOffset = entry.CursorOffset;
        tab.ScrollOffset = entry.ScrollOffset;
        tab.IsModified = true;
        return tab;
    }

    public RecoveryBatchPlan PlanRecoveryBatch(
        IReadOnlyList<AutoSaveEntry> entries,
        IReadOnlyList<EditorTabViewModel> liveTabs)
    {
        var descriptors = entries
            .Select((entry, index) => PersistedTabDescriptor.FromRecoveryEntry(
                index,
                entry))
            .ToArray();
        var plan = PlanPersistedTabBatch(descriptors, liveTabs);
        return new RecoveryBatchPlan(
            plan.Candidates
                .Select(candidate => new PlannedRecoveryEntry(
                    entries[candidate.Descriptor.SourceIndex],
                    candidate.AssignedIdentity))
                .ToArray(),
            plan.Duplicates
                .Select(duplicate => entries[duplicate.Descriptor.SourceIndex].Id)
                .ToArray());
    }

    public TabRecoveryResult RecoverTabs(
        IReadOnlyList<AutoSaveEntry> entries,
        IReadOnlyList<EditorTabViewModel> liveTabs)
    {
        var plan = PlanRecoveryBatch(entries, liveTabs);
        var recoveredTabs = new List<EditorTabViewModel>(entries.Count);
        var recoveredEntryIds = new List<string>(plan.DuplicateEntryIds);
        foreach (var candidate in plan.Candidates)
        {
            try
            {
                var entry = candidate.Entry with
                {
                    TabIdentity = candidate.AssignedIdentity
                };
                var tab = CreateRecoveryTab(entry);
                if (entry.IsUntitled)
                {
                    tab.FileName = UntitledTabNameAllocator.Allocate(
                        liveTabs.Concat(recoveredTabs),
                        tab.FileName);
                }

                recoveredTabs.Add(tab);
                recoveredEntryIds.Add(entry.Id);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to recover auto-save entry '{0}': {1}",
                    candidate.Entry.FileName,
                    ex);
            }
        }

        return new TabRecoveryResult(
            recoveredEntryIds.Count == entries.Count,
            recoveredEntryIds,
            recoveredTabs);
    }

    public TabRecoveryResult RecoverTabs(IReadOnlyList<AutoSaveEntry> entries) =>
        RecoverTabs(entries, Array.Empty<EditorTabViewModel>());

    internal static string NormalizeFilePath(string filePath) =>
        Path.GetFullPath(filePath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    internal static bool HasSameOpenIdentity(string firstPath, string secondPath) =>
        string.Equals(
            NormalizeFilePath(firstPath),
            NormalizeFilePath(secondPath),
            StringComparison.Ordinal);

    private async Task<EditorTabViewModel?> RestoreSessionFileAsync(
        SessionFile sessionFile)
    {
        if (!string.IsNullOrEmpty(sessionFile.SnapshotGeneration) ||
            !string.IsNullOrEmpty(sessionFile.SnapshotFile))
        {
            var snapshotPath = ResolveShutdownSnapshotPath(sessionFile);
            var snapshotTab = _tabFactory.Create();
            try
            {
                var isModified = HasNamedSnapshotBaseDrift(sessionFile);
                if (GetSessionMode(sessionFile) == FileOpenMode.Binary)
                {
                    snapshotTab.RestoreBinarySnapshot(
                        sessionFile.IsUntitled ? string.Empty : sessionFile.FilePath,
                        GetSessionFileName(sessionFile),
                        snapshotPath,
                        isModified,
                        sessionFile.HexOffset,
                        sessionFile.BytesPerRow);
                }
                else
                {
                    var content = await ReadTextSnapshotAsync(
                        snapshotPath,
                        sessionFile.SnapshotFormat);
                    snapshotTab.RestoreTextSnapshot(
                        sessionFile.IsUntitled ? string.Empty : sessionFile.FilePath,
                        GetSessionFileName(sessionFile),
                        content,
                        isModified,
                        sessionFile.EncodingCodePage,
                        sessionFile.HasBom);
                }

                if (!string.IsNullOrEmpty(sessionFile.TabIdentity))
                    snapshotTab.RestoreAutoSaveIdentity(sessionFile.TabIdentity);
                return snapshotTab;
            }
            catch
            {
                snapshotTab.Dispose();
                throw;
            }
        }

        if (sessionFile.IsUntitled)
        {
            if (GetSessionMode(sessionFile) == FileOpenMode.Binary)
                return null;

            var content = sessionFile.Content ?? string.Empty;
            if (!string.IsNullOrEmpty(sessionFile.TempFilePath) &&
                _fileSystemService.FileExists(sessionFile.TempFilePath))
            {
                content = await _fileSystemService.ReadAllTextAsync(sessionFile.TempFilePath);
            }

            var untitledTab = _tabFactory.CreateUntitled(content);
            untitledTab.FileName = GetSessionFileName(sessionFile);
            untitledTab.SetContentBaseline(content, sessionFile.IsModified);
            if (!string.IsNullOrEmpty(sessionFile.TabIdentity))
                untitledTab.RestoreAutoSaveIdentity(sessionFile.TabIdentity);
            return untitledTab;
        }

        if (sessionFile.IsModified)
            return null;
        if (!_fileSystemService.FileExists(sessionFile.FilePath))
            return null;
        if (!string.IsNullOrEmpty(sessionFile.BaseContentHash) &&
            !string.Equals(
                sessionFile.BaseContentHash,
                ComputeFileContentHash(sessionFile.FilePath),
                StringComparison.Ordinal))
        {
            return null;
        }

        var tab = _tabFactory.Create();
        try
        {
            await tab.LoadFileAsync(sessionFile.FilePath);
            if (!string.IsNullOrEmpty(sessionFile.TabIdentity))
                tab.RestoreAutoSaveIdentity(sessionFile.TabIdentity);
            return tab;
        }
        catch
        {
            tab.Dispose();
            throw;
        }
    }

    private static int ResolvePlannedActiveIndex(
        int requestedSourceIndex,
        int adoptedCount,
        IReadOnlyDictionary<int, int> adoptedIndexes,
        IReadOnlyList<DuplicatePersistedTab> duplicates)
    {
        if (adoptedIndexes.TryGetValue(requestedSourceIndex, out var adoptedIndex))
            return adoptedIndex;

        var duplicate = duplicates.FirstOrDefault(item =>
            item.Descriptor.SourceIndex == requestedSourceIndex);
        if (duplicate?.Owner.PlannedSourceIndex is int ownerSourceIndex &&
            adoptedIndexes.TryGetValue(ownerSourceIndex, out var ownerIndex))
        {
            return ownerIndex;
        }

        return adoptedCount == 0
            ? 0
            : Math.Clamp(requestedSourceIndex, 0, adoptedCount - 1);
    }

    private string GenerateUniqueTabIdentity(
        IReadOnlySet<string> usedIdentities,
        IReadOnlySet<string> reservedPersistedIdentities)
    {
        for (var attempt = 0; attempt < MaxIdentityGenerationAttempts; attempt++)
        {
            var identity = _tabIdentityGenerator();
            if (IsAvailableIdentity(
                    identity,
                    usedIdentities,
                    reservedPersistedIdentities))
                return identity;
        }

        for (var attempt = 0; attempt < MaxIdentityGenerationAttempts; attempt++)
        {
            var identity = _fallbackTabIdentityGenerator();
            if (IsAvailableIdentity(
                    identity,
                    usedIdentities,
                    reservedPersistedIdentities))
                return identity;
        }

        throw new InvalidOperationException(
            "Unable to generate a unique storage-safe tab identity.");
    }

    private static bool IsAvailableIdentity(
        string? identity,
        IReadOnlySet<string> usedIdentities,
        IReadOnlySet<string> reservedPersistedIdentities) =>
        HasValidPersistedIdentity(identity) &&
        !usedIdentities.Contains(identity!) &&
        !reservedPersistedIdentities.Contains(identity!);

    private PersistedTabBatchPlan PlanPersistedTabBatch(
        IReadOnlyList<PersistedTabDescriptor> descriptors,
        IReadOnlyList<EditorTabViewModel> liveTabs)
    {
        var usedIdentities = liveTabs
            .Select(tab => tab.AutoSaveIdentity)
            .ToHashSet(StorageIdentityComparer);
        var reservedPersistedIdentities = descriptors
            .Select(descriptor => descriptor.PersistedIdentity)
            .Where(HasValidPersistedIdentity)
            .Cast<string>()
            .ToHashSet(StorageIdentityComparer);
        var owners = liveTabs
            .Select(PersistedTabOwner.FromLiveTab)
            .ToList();
        var candidates = new List<PlannedPersistedTab>(descriptors.Count);
        var duplicates = new List<DuplicatePersistedTab>();

        foreach (var descriptor in descriptors)
        {
            var duplicateOwner = owners.FirstOrDefault(owner =>
                owner.IsExactDuplicateOf(descriptor));
            if (duplicateOwner != null)
            {
                if (HasValidPersistedIdentity(descriptor.PersistedIdentity) &&
                    duplicateOwner.PlannedSourceIndex is int ownerSourceIndex &&
                    !usedIdentities.Contains(descriptor.PersistedIdentity!))
                {
                    var candidateIndex = candidates.FindIndex(candidate =>
                        candidate.Descriptor.SourceIndex == ownerSourceIndex);
                    var ownerIndex = owners.IndexOf(duplicateOwner);
                    var retainedCandidate = candidates[candidateIndex];
                    if (!HasValidPersistedIdentity(duplicateOwner.ProofIdentity) ||
                        !string.Equals(
                            retainedCandidate.AssignedIdentity,
                            duplicateOwner.ProofIdentity,
                            StringComparison.Ordinal))
                    {
                        usedIdentities.Remove(retainedCandidate.AssignedIdentity);
                        usedIdentities.Add(descriptor.PersistedIdentity!);
                        candidates[candidateIndex] = retainedCandidate with
                        {
                            AssignedIdentity = descriptor.PersistedIdentity!
                        };
                        duplicateOwner = duplicateOwner with
                        {
                            ProofIdentity = descriptor.PersistedIdentity
                        };
                        owners[ownerIndex] = duplicateOwner;
                    }
                }

                duplicates.Add(new DuplicatePersistedTab(
                    descriptor,
                    duplicateOwner));
                continue;
            }

            var assignedIdentity = descriptor.PersistedIdentity;
            if (!HasValidPersistedIdentity(assignedIdentity) ||
                usedIdentities.Contains(assignedIdentity!))
            {
                assignedIdentity = GenerateUniqueTabIdentity(
                    usedIdentities,
                    reservedPersistedIdentities);
            }

            usedIdentities.Add(assignedIdentity!);
            var planned = new PlannedPersistedTab(
                descriptor,
                assignedIdentity!);
            candidates.Add(planned);
            owners.Add(PersistedTabOwner.FromPlanned(planned));
        }

        return new PersistedTabBatchPlan(candidates, duplicates);
    }

    internal static bool HasValidPersistedIdentity(string? identity) =>
        !string.IsNullOrWhiteSpace(identity) &&
        identity.Length <= MaxPersistedTabIdentityLength &&
        identity.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !identity.Contains(Path.DirectorySeparatorChar) &&
        !identity.Contains(Path.AltDirectorySeparatorChar);

    private static string GetDefaultShutdownSnapshotRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit",
            "ShutdownSessions");

    private SessionFile CreateShutdownSessionFile(EditorTabViewModel tab)
    {
        var sessionFile = new SessionFile
        {
            FilePath = string.IsNullOrEmpty(tab.FilePath)
                ? tab.FileName
                : tab.FilePath,
            FileName = tab.FileName,
            TabIdentity = tab.AutoSaveIdentity,
            SnapshotOwner = _shutdownOwner,
            IsUntitled = string.IsNullOrEmpty(tab.FilePath),
            IsBinaryMode = tab.Mode == FileOpenMode.Binary,
            Mode = tab.Mode,
            IsModified = tab.IsModified,
            EncodingCodePage = tab.FileEncoding.CodePage,
            HasBom = tab.HasBom,
            CursorOffset = tab.CursorOffset,
            ScrollOffset = tab.ScrollOffset,
            HexOffset = tab.HexOffset,
            BytesPerRow = tab.BytesPerRow,
            LargeFileTopLine = tab.LargeFileTopLine
        };
        if (!sessionFile.IsUntitled && !sessionFile.IsModified)
        {
            if (_fileSystemService.FileExists(tab.FilePath))
            {
                var sourceHash = ComputeFileContentHash(tab.FilePath);
                if (sessionFile.Mode == FileOpenMode.LargeText)
                {
                    sessionFile.BaseContentHash = sourceHash;
                }
                else if (string.Equals(
                        sourceHash,
                        ComputeTabContentHash(tab),
                        StringComparison.Ordinal))
                {
                    sessionFile.BaseContentHash = sourceHash;
                }
                else
                {
                    sessionFile.IsModified = true;
                }
            }
            else
                sessionFile.IsModified = true;
        }

        return sessionFile;
    }

    private static bool RequiresShutdownSnapshot(SessionFile sessionFile) =>
        sessionFile.Mode != FileOpenMode.LargeText &&
        (sessionFile.IsUntitled ||
         sessionFile.IsModified ||
         sessionFile.Mode is FileOpenMode.Text or FileOpenMode.Binary);

    private string PersistShutdownSnapshot(
        EditorTabViewModel tab,
        SessionFile sessionFile,
        string generation,
        string generationDirectory)
    {
        var extension = sessionFile.Mode == FileOpenMode.Binary
            ? ".bin"
            : ".txt";
        var snapshotFile = $"tab-{tab.AutoSaveIdentity}{extension}";
        var snapshotPath = Path.Combine(generationDirectory, snapshotFile);
        if (sessionFile.Mode == FileOpenMode.Binary)
        {
            _fileSystemService.WriteStreamAtomic(
                snapshotPath,
                tab.WriteBinarySnapshot);
        }
        else
        {
            _fileSystemService.WriteStreamAtomic(
                snapshotPath,
                stream => WriteTextSnapshot(stream, tab.Content));
        }

        sessionFile.SnapshotGeneration = generation;
        sessionFile.SnapshotFile = snapshotFile;
        sessionFile.SnapshotFormat = ExactTextSnapshotFormat;
        return snapshotPath;
    }

    private static void WriteTextSnapshot(Stream stream, string content)
    {
        if (content.Length > MaxTextSnapshotCharacters)
            throw new InvalidDataException("The text snapshot exceeds the supported size.");

        Span<byte> header = stackalloc byte[TextSnapshotHeaderLength];
        TextSnapshotMagic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(
            header[TextSnapshotMagic.Length..],
            content.Length);
        stream.Write(header);
        Span<byte> codeUnit = stackalloc byte[2];
        foreach (var character in content)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(codeUnit, character);
            stream.Write(codeUnit);
        }
    }

    private async Task<string> ReadTextSnapshotAsync(
        string snapshotPath,
        string? snapshotFormat)
    {
        if (string.Equals(
                snapshotFormat,
                ExactTextSnapshotFormat,
                StringComparison.Ordinal))
        {
            return ReadFramedTextSnapshot(snapshotPath);
        }

        if (!string.IsNullOrEmpty(snapshotFormat))
            throw new InvalidDataException("The text snapshot format is unsupported.");
        if (_fileSystemService.GetFileSize(snapshotPath) >
            MaxLegacyTextSnapshotBytes)
        {
            throw new InvalidDataException(
                "The legacy text snapshot exceeds the supported size.");
        }

        return await _fileSystemService.ReadAllTextAsync(snapshotPath);
    }

    private string ReadFramedTextSnapshot(string snapshotPath)
    {
        using var stream = _fileSystemService.OpenRead(snapshotPath);
        Span<byte> magic = stackalloc byte[TextSnapshotMagic.Length];
        var magicLength = ReadUpTo(stream, magic);
        if (magicLength != TextSnapshotMagic.Length ||
            !magic.SequenceEqual(TextSnapshotMagic))
        {
            throw new InvalidDataException(
                "The text snapshot frame has an invalid signature.");
        }

        Span<byte> lengthBytes = stackalloc byte[sizeof(int)];
        ReadExactly(stream, lengthBytes);
        var characterCount =
            BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (characterCount < 0 ||
            characterCount > MaxTextSnapshotCharacters ||
            stream.Length != TextSnapshotHeaderLength +
                (long)characterCount * sizeof(char))
        {
            throw new InvalidDataException(
                "The text snapshot frame is invalid.");
        }

        var characters = new char[characterCount];
        Span<byte> codeUnit = stackalloc byte[2];
        for (var index = 0; index < characters.Length; index++)
        {
            ReadExactly(stream, codeUnit);
            characters[index] = (char)
                BinaryPrimitives.ReadUInt16LittleEndian(codeUnit);
        }

        return new string(characters);
    }

    private static int ReadUpTo(Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                break;
            totalRead += read;
        }
        return totalRead;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        if (ReadUpTo(stream, buffer) != buffer.Length)
            throw new InvalidDataException("The text snapshot frame is truncated.");
    }

    private string ResolveShutdownSnapshotPath(SessionFile sessionFile)
    {
        var generation = sessionFile.SnapshotGeneration;
        var snapshotFile = sessionFile.SnapshotFile;
        if (!HasValidPersistedIdentity(generation) ||
            string.IsNullOrWhiteSpace(snapshotFile) ||
            Path.IsPathRooted(snapshotFile) ||
            !string.Equals(
                Path.GetFileName(snapshotFile),
                snapshotFile,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The shutdown snapshot reference is invalid.");
        }

        var expectedExtension = GetSessionMode(sessionFile) == FileOpenMode.Binary
            ? ".bin"
            : ".txt";
        var expectedFile = $"tab-{sessionFile.TabIdentity}{expectedExtension}";
        if (!HasValidPersistedIdentity(sessionFile.TabIdentity) ||
            !string.Equals(snapshotFile, expectedFile, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The shutdown snapshot filename is invalid.");
        }

        return ResolveOwnedShutdownSnapshotPath(generation!, snapshotFile);
    }

    private string ResolveOwnedShutdownSnapshotPath(
        string generation,
        string snapshotFile)
    {
        if (!HasValidPersistedIdentity(generation) ||
            string.IsNullOrWhiteSpace(snapshotFile) ||
            Path.IsPathRooted(snapshotFile) ||
            !string.Equals(
                Path.GetFileName(snapshotFile),
                snapshotFile,
                StringComparison.Ordinal) ||
            !snapshotFile.StartsWith("tab-", StringComparison.Ordinal) ||
            !(snapshotFile.EndsWith(".txt", StringComparison.Ordinal) ||
              snapshotFile.EndsWith(".bin", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The owned shutdown snapshot reference is invalid.");
        }

        var generationDirectory = GetShutdownGenerationDirectory(generation);
        var snapshotPath = Path.GetFullPath(Path.Combine(
            generationDirectory,
            snapshotFile));
        var rootPrefix = Path.GetFullPath(_shutdownSnapshotRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!snapshotPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            !_fileSystemService.FileExists(snapshotPath))
        {
            throw new InvalidDataException("The shutdown snapshot is unavailable.");
        }

        RejectReparsePoint(_shutdownSnapshotRoot);
        RejectReparsePoint(generationDirectory);
        RejectReparsePoint(snapshotPath);
        return snapshotPath;
    }

    private string GetShutdownGenerationDirectory(string generation)
    {
        var root = Path.GetFullPath(_shutdownSnapshotRoot);
        var generationDirectory = Path.GetFullPath(Path.Combine(root, generation));
        var rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!generationDirectory.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The shutdown snapshot generation is invalid.");
        }

        return generationDirectory;
    }

    private string GetShutdownLeaseMarkerPath(string generation) =>
        Path.Combine(GetShutdownGenerationDirectory(generation), ".lease");

    private void AcquireShutdownGenerationLeases(ShutdownSessionState session)
    {
        foreach (var generation in session.Files
                     .Select(file => file.SnapshotGeneration)
                     .Where(HasValidPersistedIdentity)
                     .Cast<string>()
                     .Distinct(StorageIdentityComparer))
        {
            if (_shutdownGenerationLeases.ContainsKey(generation))
                continue;

            var generationDirectory = GetShutdownGenerationDirectory(generation);
            var markerPath = GetShutdownLeaseMarkerPath(generation);
            if (!_fileSystemService.FileExists(markerPath))
                continue;

            RejectReparsePoint(_shutdownSnapshotRoot);
            RejectReparsePoint(generationDirectory);
            RejectReparsePoint(markerPath);
            _shutdownGenerationLeases.Add(
                generation,
                _fileSystemService.OpenFile(
                    markerPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read));
        }
    }

    private void RejectReparsePoint(string path)
    {
        if ((_fileSystemService.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Shutdown snapshots cannot use reparse points.");
    }

    private void RetireUnreferencedShutdownGenerations(
        IReadOnlyList<SessionFile> previousFiles,
        IReadOnlyList<SessionFile> publishedFiles)
    {
        var failedRetirements = new List<SessionFile>();
        var retainedGenerations = publishedFiles
            .Select(file => file.SnapshotGeneration)
            .Where(HasValidPersistedIdentity)
            .ToHashSet(StorageIdentityComparer);
        foreach (var generationGroup in previousFiles
                     .Where(file => HasValidPersistedIdentity(file.SnapshotGeneration))
                     .GroupBy(file => file.SnapshotGeneration!, StorageIdentityComparer))
        {
            if (retainedGenerations.Contains(generationGroup.Key))
                continue;

            try
            {
                if (_shutdownGenerationLeases.Remove(
                        generationGroup.Key,
                        out var ownedLease))
                {
                    ownedLease.Dispose();
                }

                var leaseMarkerPath =
                    GetShutdownLeaseMarkerPath(generationGroup.Key);
                if (!_fileSystemService.FileExists(leaseMarkerPath))
                    continue;
                using (var exclusiveLease = _fileSystemService.OpenFile(
                           leaseMarkerPath,
                           FileMode.Open,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                var ownedFiles = generationGroup
                    .SelectMany(file =>
                        file.SnapshotGenerationFiles ?? new List<string>())
                    .Concat(generationGroup
                        .Select(file => file.SnapshotFile)
                        .Where(file => !string.IsNullOrEmpty(file))
                        .Cast<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var resolvedPaths = ownedFiles
                    .Select(file => ResolveOwnedShutdownSnapshotPath(
                        generationGroup.Key,
                        file))
                    .ToArray();
                foreach (var path in resolvedPaths)
                    _fileSystemService.DeleteFile(path);
                }

                _fileSystemService.DeleteFile(leaseMarkerPath);

                var generationDirectory =
                    GetShutdownGenerationDirectory(generationGroup.Key);
                if (_fileSystemService.GetFiles(generationDirectory, "*").Length == 0 &&
                    _fileSystemService.GetDirectories(generationDirectory).Length == 0)
                {
                    _fileSystemService.DeleteDirectory(generationDirectory);
                }
            }

            catch (Exception ex)
            {
                failedRetirements.AddRange(
                    generationGroup.Select(CloneSessionFile));
                Trace.TraceWarning(
                    "Failed to retire shutdown snapshot generation '{0}': {1}",
                    generationGroup.Key,
                    ex.Message);
            }
        }
        _retirementCandidates.Clear();
        _retirementCandidates.AddRange(failedRetirements);
    }

    private void RetireSnapshotlessShutdownGeneration(
        string generation,
        IReadOnlyList<SessionFile> publishedFiles)
    {
        if (!publishedFiles.Any(file => string.Equals(
                file.SnapshotGeneration,
                generation,
                StringComparison.OrdinalIgnoreCase)))
        {
            _snapshotlessRetirementCandidates.Add(generation);
        }
        foreach (var candidate in _snapshotlessRetirementCandidates.ToArray())
        {
            if (publishedFiles.Any(file => string.Equals(
                    file.SnapshotGeneration,
                    candidate,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (TryRetireSnapshotlessShutdownGeneration(candidate))
                _snapshotlessRetirementCandidates.Remove(candidate);
        }
    }

    private bool TryRetireSnapshotlessShutdownGeneration(string generation)
    {
        try
        {
            var leaseMarkerPath = GetShutdownLeaseMarkerPath(generation);
            if (!_fileSystemService.FileExists(leaseMarkerPath))
                return true;
            using (var exclusiveLease = _fileSystemService.OpenFile(
                       leaseMarkerPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
            }
            var generationDirectory = GetShutdownGenerationDirectory(generation);
            var remainingFiles = _fileSystemService
                .GetFiles(generationDirectory, "*")
                .Where(path => !HasSameOpenIdentity(path, leaseMarkerPath))
                .ToArray();
            if (remainingFiles.Length != 0 ||
                _fileSystemService.GetDirectories(generationDirectory).Length != 0)
                return false;

            _fileSystemService.DeleteFile(leaseMarkerPath);
            _fileSystemService.DeleteDirectory(generationDirectory);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to retire snapshotless shutdown generation '{0}': {1}",
                generation,
                ex.Message);
            return false;
        }
    }

    private void DeleteUnpublishedShutdownGeneration(
        string generationDirectory,
        IReadOnlyList<string> createdSnapshotPaths)
    {
        foreach (var path in createdSnapshotPaths)
        {
            try
            {
                _fileSystemService.DeleteFile(path);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to delete unpublished shutdown snapshot '{0}': {1}",
                    path,
                    ex.Message);
            }
        }

        try
        {
            if (_fileSystemService.DirectoryExists(generationDirectory) &&
                _fileSystemService.GetFiles(generationDirectory, "*").Length == 0 &&
                _fileSystemService.GetDirectories(generationDirectory).Length == 0)
            {
                _fileSystemService.DeleteDirectory(generationDirectory);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to delete unpublished shutdown generation '{0}': {1}",
                generationDirectory,
                ex.Message);
        }
    }

    private void TrackUnresolvedShutdownEntry(SessionFile sessionFile)
    {
        RemovePendingShutdownEntry(sessionFile);
        if (_unresolvedShutdownEntries.Any(existing =>
                string.Equals(
                    existing.SnapshotGeneration,
                    sessionFile.SnapshotGeneration,
                    StringComparison.Ordinal) &&
                string.Equals(
                    existing.SnapshotFile,
                    sessionFile.SnapshotFile,
                    StringComparison.Ordinal) &&
                string.Equals(
                    existing.TabIdentity,
                    sessionFile.TabIdentity,
                    StringComparison.Ordinal)))
        {
            return;
        }

        _unresolvedShutdownEntries.Add(CloneSessionFile(sessionFile));
        TrackReplacedShutdownOwner(sessionFile);
    }

    private void TrackPendingShutdownEntry(SessionFile sessionFile)
    {
        if (_pendingShutdownEntries.Any(existing =>
                IsSameShutdownSource(existing, sessionFile)))
        {
            return;
        }

        _pendingShutdownEntries.Add(CloneSessionFile(sessionFile));
    }

    private void RemovePendingShutdownEntry(SessionFile? sessionFile)
    {
        if (sessionFile == null)
            return;
        _pendingShutdownEntries.RemoveAll(existing =>
            IsSameShutdownSource(existing, sessionFile));
    }

    private static bool IsSameShutdownSource(
        SessionFile first,
        SessionFile second) =>
        string.Equals(
            first.SnapshotGeneration,
            second.SnapshotGeneration,
            StringComparison.Ordinal) &&
        string.Equals(
            first.SnapshotFile,
            second.SnapshotFile,
            StringComparison.Ordinal) &&
        string.Equals(
            first.TabIdentity,
            second.TabIdentity,
            StringComparison.Ordinal) &&
        string.Equals(
            first.FilePath,
            second.FilePath,
            StringComparison.Ordinal);

    private void TrackReplacedShutdownOwner(SessionFile? sessionFile)
    {
        if (!string.IsNullOrWhiteSpace(sessionFile?.SnapshotOwner))
            _replacedShutdownOwners.Add(sessionFile.SnapshotOwner);
    }

    private void TrackConsumedShutdownEntry(SessionFile? sessionFile)
    {
        RemovePendingShutdownEntry(sessionFile);
        TrackReplacedShutdownOwner(sessionFile);
        if (sessionFile == null ||
            !HasValidPersistedIdentity(sessionFile.SnapshotGeneration) ||
            string.IsNullOrEmpty(sessionFile.SnapshotFile) ||
            _retirementCandidates.Any(existing =>
                string.Equals(
                    existing.SnapshotGeneration,
                    sessionFile.SnapshotGeneration,
                    StringComparison.Ordinal) &&
                string.Equals(
                    existing.SnapshotFile,
                    sessionFile.SnapshotFile,
                    StringComparison.Ordinal)))
        {
            return;
        }

        _retirementCandidates.Add(CloneSessionFile(sessionFile));
    }

    private static FileOpenMode GetSessionMode(SessionFile sessionFile) =>
        sessionFile.IsBinaryMode ? FileOpenMode.Binary : sessionFile.Mode;

    private bool HasNamedSnapshotBaseDrift(SessionFile sessionFile)
    {
        if (sessionFile.IsUntitled || sessionFile.IsModified)
            return sessionFile.IsModified;
        if (string.IsNullOrWhiteSpace(sessionFile.BaseContentHash) ||
            !_fileSystemService.FileExists(sessionFile.FilePath))
        {
            return true;
        }

        return !string.Equals(
            sessionFile.BaseContentHash,
            ComputeFileContentHash(sessionFile.FilePath),
            StringComparison.Ordinal);
    }

    private string ComputeFileContentHash(string filePath)
    {
        using var stream = _fileSystemService.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static string ComputeTabContentHash(EditorTabViewModel tab)
    {
        if (tab.Mode == FileOpenMode.Binary)
        {
            using var hash = SHA256.Create();
            using var hashingStream = new CryptoStream(
                Stream.Null,
                hash,
                CryptoStreamMode.Write);
            tab.WriteBinarySnapshot(hashingStream);
            hashingStream.FlushFinalBlock();
            return Convert.ToHexString(hash.Hash!);
        }

        using var incrementalHash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (tab.HasBom)
            incrementalHash.AppendData(tab.FileEncoding.GetPreamble());
        incrementalHash.AppendData(tab.FileEncoding.GetBytes(tab.Content));
        return Convert.ToHexString(incrementalHash.GetHashAndReset());
    }

    private static string GetSessionFileName(SessionFile sessionFile) =>
        !string.IsNullOrEmpty(sessionFile.FileName)
            ? sessionFile.FileName
            : Path.GetFileName(sessionFile.FilePath);

    private static SessionFile CloneSessionFile(SessionFile file) =>
        new()
        {
            FilePath = file.FilePath,
            FileName = file.FileName,
            TabIdentity = file.TabIdentity,
            IsUntitled = file.IsUntitled,
            IsBinaryMode = file.IsBinaryMode,
            Mode = file.Mode,
            IsModified = file.IsModified,
            IsActive = file.IsActive,
            TempFilePath = file.TempFilePath,
            Content = file.Content,
            SnapshotGeneration = file.SnapshotGeneration,
            SnapshotFile = file.SnapshotFile,
            SnapshotFormat = file.SnapshotFormat,
            SnapshotOwner = file.SnapshotOwner,
            SnapshotGenerationFiles =
                file.SnapshotGenerationFiles?.ToList() ?? new List<string>(),
            BaseContentHash = file.BaseContentHash,
            EncodingCodePage = file.EncodingCodePage,
            HasBom = file.HasBom,
            CursorOffset = file.CursorOffset,
            ScrollOffset = file.ScrollOffset,
            HexOffset = file.HexOffset,
            BytesPerRow = file.BytesPerRow,
            LargeFileTopLine = file.LargeFileTopLine
        };

    public void Dispose()
    {
        foreach (var lease in _shutdownGenerationLeases.Values)
            lease.Dispose();
        _shutdownGenerationLeases.Clear();
    }
}

public sealed class StagedDocumentSession : IDisposable
{
    private bool _adopted;

    internal StagedDocumentSession(
        IReadOnlyList<EditorTabViewModel> tabs,
        int activeTabIndex)
    {
        Tabs = tabs;
        ActiveTabIndex = activeTabIndex;
    }

    public IReadOnlyList<EditorTabViewModel> Tabs { get; }

    public int ActiveTabIndex { get; }

    public IReadOnlyList<EditorTabViewModel> AdoptTabs()
    {
        _adopted = true;
        return Tabs;
    }

    public void Dispose()
    {
        if (_adopted)
            return;

        foreach (var tab in Tabs)
            tab.Dispose();
    }
}

public sealed class WorkspaceMutationSnapshot
{
    private readonly IReadOnlyDictionary<EditorTabViewModel, long> _versions;

    internal WorkspaceMutationSnapshot(
        IReadOnlyDictionary<EditorTabViewModel, long> versions)
    {
        _versions = versions;
    }

    public bool HasChanged(IReadOnlyList<EditorTabViewModel> tabs) =>
        tabs.Count != _versions.Count ||
        tabs.Any(tab =>
            !_versions.TryGetValue(tab, out var version) ||
            tab.UserMutationVersion != version);
}

public enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel
}

public sealed record UnsavedChangesPreparationResult(
    bool CanContinue,
    string? FailureMessage = null);

public sealed class RestoredDocumentSession : IDisposable
{
    private IReadOnlyList<RestoredTabCandidate>? _candidates;

    internal RestoredDocumentSession(
        IReadOnlyList<RestoredTabCandidate> candidates,
        int activeTabIndex)
    {
        _candidates = candidates;
        ActiveTabIndex = activeTabIndex;
    }

    public IReadOnlyList<RestoredTabCandidate> Candidates =>
        _candidates ?? Array.Empty<RestoredTabCandidate>();

    public int ActiveTabIndex { get; }

    internal IReadOnlyList<RestoredTabCandidate> TakeCandidates()
    {
        var candidates = _candidates ??
            throw new InvalidOperationException("Restored tabs have already been adopted.");
        _candidates = null;
        return candidates;
    }

    public void Dispose()
    {
        if (_candidates == null)
            return;

        foreach (var candidate in _candidates)
            candidate.Tab.Dispose();
        _candidates = null;
    }
}

public sealed record RestoredTabCandidate(
    EditorTabViewModel Tab,
    int SessionIndex,
    string? TabIdentity = null,
    SessionFile? SourceEntry = null);

public sealed record RestoredSessionAdoptionResult(
    IReadOnlyList<EditorTabViewModel> AdoptedTabs,
    IReadOnlyList<EditorTabViewModel> DiscardedDuplicateTabs,
    EditorTabViewModel? SelectedTab);

public sealed record ShutdownRestoreStatus(
    int PendingEntryCount,
    int UnresolvedEntryCount,
    bool RestoreFailed)
{
    public bool RequiresCarryForward =>
        RestoreFailed || PendingEntryCount > 0 || UnresolvedEntryCount > 0;
}

public sealed record TabRecoveryResult(
    bool Success,
    IReadOnlyList<string> RecoveredEntryIds,
    IReadOnlyList<EditorTabViewModel>? RecoveredTabs = null);

public sealed record RecoveryBatchPlan(
    IReadOnlyList<PlannedRecoveryEntry> Candidates,
    IReadOnlyList<string> DuplicateEntryIds);

public sealed record PlannedRecoveryEntry(
    AutoSaveEntry Entry,
    string AssignedIdentity);

internal sealed record PersistedTabDescriptor(
    int SourceIndex,
    string? PersistedIdentity,
    string Content,
    bool IsUntitled,
    string? FilePath,
    FileOpenMode Mode,
    bool IsModified,
    int? EncodingCodePage,
    bool? HasBom,
    int CursorOffset,
    double ScrollOffset,
    string? BinaryContentHash,
    long HexOffset,
    int BytesPerRow,
    long LargeFileTopLine)
{
    public static PersistedTabDescriptor FromTab(
        int sourceIndex,
        string? persistedIdentity,
        EditorTabViewModel tab) =>
        new(
            sourceIndex,
            persistedIdentity,
            tab.Content,
            string.IsNullOrEmpty(tab.FilePath),
            string.IsNullOrEmpty(tab.FilePath) ? null : tab.FilePath,
            tab.Mode,
            tab.IsModified,
            tab.Mode == FileOpenMode.Text ? tab.FileEncoding.CodePage : null,
            tab.Mode == FileOpenMode.Text ? tab.HasBom : null,
            tab.CursorOffset,
            tab.ScrollOffset,
            tab.Mode == FileOpenMode.Binary
                ? DocumentSessionCoordinator.ComputeTabContentHash(tab)
                : null,
            tab.HexOffset,
            tab.BytesPerRow,
            tab.LargeFileTopLine);

    public static PersistedTabDescriptor FromRecoveryEntry(
        int sourceIndex,
        AutoSaveEntry entry) =>
        new(
            sourceIndex,
            entry.TabIdentity,
            entry.Content,
            entry.IsUntitled,
            entry.FilePath,
            FileOpenMode.Text,
            true,
            null,
            null,
            entry.CursorOffset,
            entry.ScrollOffset,
            null,
            0,
            16,
            1);

    public bool HasEquivalentState(PersistedTabDescriptor other)
    {
        if (IsUntitled != other.IsUntitled ||
            Mode != other.Mode ||
            IsModified != other.IsModified ||
            !string.Equals(Content, other.Content, StringComparison.Ordinal) ||
            CursorOffset != other.CursorOffset ||
            ScrollOffset != other.ScrollOffset ||
            LargeFileTopLine != other.LargeFileTopLine)
        {
            return false;
        }

        if (Mode == FileOpenMode.Binary)
        {
            if (string.IsNullOrEmpty(BinaryContentHash) ||
                string.IsNullOrEmpty(other.BinaryContentHash) ||
                HexOffset != other.HexOffset ||
                BytesPerRow != other.BytesPerRow ||
                !string.Equals(
                    BinaryContentHash,
                    other.BinaryContentHash,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        if (EncodingCodePage.HasValue &&
            other.EncodingCodePage.HasValue &&
            EncodingCodePage != other.EncodingCodePage)
        {
            return false;
        }
        if (HasBom.HasValue &&
            other.HasBom.HasValue &&
            HasBom != other.HasBom)
        {
            return false;
        }

        if (IsUntitled)
            return true;

        return !string.IsNullOrEmpty(FilePath) &&
            !string.IsNullOrEmpty(other.FilePath) &&
            DocumentSessionCoordinator.HasSameOpenIdentity(
                FilePath,
                other.FilePath);
    }
}

internal sealed record PersistedTabOwner(
    string? ProofIdentity,
    PersistedTabDescriptor Descriptor,
    EditorTabViewModel? LiveTab,
    int? PlannedSourceIndex)
{
    public static PersistedTabOwner FromLiveTab(EditorTabViewModel tab) =>
        new(
            tab.AutoSaveIdentity,
            PersistedTabDescriptor.FromTab(-1, tab.AutoSaveIdentity, tab),
            tab,
            null);

    public static PersistedTabOwner FromPlanned(PlannedPersistedTab planned) =>
        new(
            planned.Descriptor.PersistedIdentity,
            planned.Descriptor,
            null,
            planned.Descriptor.SourceIndex);

    public bool IsExactDuplicateOf(PersistedTabDescriptor candidate)
    {
        if (!Descriptor.HasEquivalentState(candidate))
            return false;

        return
            DocumentSessionCoordinator.HasValidPersistedIdentity(
                candidate.PersistedIdentity) &&
            string.Equals(
                ProofIdentity,
                candidate.PersistedIdentity,
                StringComparison.Ordinal);
    }
}

internal sealed record PersistedTabBatchPlan(
    IReadOnlyList<PlannedPersistedTab> Candidates,
    IReadOnlyList<DuplicatePersistedTab> Duplicates);

internal sealed record PlannedPersistedTab(
    PersistedTabDescriptor Descriptor,
    string AssignedIdentity);

internal sealed record DuplicatePersistedTab(
    PersistedTabDescriptor Descriptor,
    PersistedTabOwner Owner);

internal sealed class LegacyShutdownSessionStore : IShutdownSessionStore
{
    private readonly ISettingsService _settingsService;

    public LegacyShutdownSessionStore(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public ShutdownSessionState ReadShutdownSession(
        Action<ShutdownSessionState>? whileLocked = null)
    {
        if (_settingsService is IShutdownSessionStore shutdownSessionStore)
            return shutdownSessionStore.ReadShutdownSession(whileLocked);

        throw new InvalidOperationException(
            "Shutdown session restoration requires an atomic session store.");
    }

    public ShutdownSessionPublication PublishShutdownSession(
        ShutdownSessionState session,
        Action<ShutdownSessionPublication>? whileLocked = null)
    {
        if (_settingsService is not IShutdownSessionStore shutdownSessionStore)
        {
            throw new InvalidOperationException(
                "Shutdown session publication requires an atomic session store.");
        }

        return shutdownSessionStore.PublishShutdownSession(session, whileLocked);
    }
}

internal sealed class PreparedBinaryRebase : IDisposable
{
    private bool _adopted;

    public PreparedBinaryRebase(
        EditorTabViewModel tab,
        string path,
        bool isModified,
        FastEdit.Core.HexEngine.VirtualizedByteBuffer preparedBuffer)
    {
        Tab = tab;
        Path = path;
        IsModified = isModified;
        PreparedBuffer = preparedBuffer;
    }

    public EditorTabViewModel Tab { get; }

    public string Path { get; }

    public bool IsModified { get; }

    public FastEdit.Core.HexEngine.VirtualizedByteBuffer PreparedBuffer { get; }

    public void MarkAdopted() => _adopted = true;

    public void Dispose()
    {
        if (!_adopted)
            PreparedBuffer.Dispose();
    }
}
