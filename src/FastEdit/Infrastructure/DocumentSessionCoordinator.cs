using System.Diagnostics;
using System.IO;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public sealed class DocumentSessionCoordinator
{
    private const int MaxPersistedTabIdentityLength = 128;
    private const int MaxIdentityGenerationAttempts = 32;
    private static readonly StringComparer StorageIdentityComparer =
        StringComparer.OrdinalIgnoreCase;
    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IEditorTabFactory _tabFactory;
    private readonly Func<string> _tabIdentityGenerator;
    private readonly Func<string> _fallbackTabIdentityGenerator;
    private readonly HashSet<EditorTabViewModel> _shutdownDiscardedTabs = new();

    public DocumentSessionCoordinator(
        ISettingsService settingsService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory)
        : this(
            settingsService,
            fileSystemService,
            tabFactory,
            () => Guid.NewGuid().ToString("N"),
            () => Guid.NewGuid().ToString("N"))
    {
    }

    internal DocumentSessionCoordinator(
        ISettingsService settingsService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory,
        Func<string> tabIdentityGenerator,
        Func<string>? fallbackTabIdentityGenerator = null)
    {
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
        _tabFactory = tabFactory;
        _tabIdentityGenerator = tabIdentityGenerator;
        _fallbackTabIdentityGenerator = fallbackTabIdentityGenerator ??
            (() => Guid.NewGuid().ToString("N"));
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
                ScrollOffset = tab.ScrollOffset
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
                foreach (var tab in unsavedTabs.Where(tab => string.IsNullOrEmpty(tab.FilePath)))
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
        var openFiles = _settingsService.OpenFiles;
        if (openFiles == null || openFiles.Count == 0)
            return new RestoredDocumentSession(
                restoredCandidates,
                _settingsService.ActiveTabIndex);

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
                    restoredCandidates.Add(
                        new RestoredTabCandidate(tab, index, sessionFile.TabIdentity));
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to restore session file '{0}': {1}",
                    sessionFile.FilePath,
                    ex.Message);
            }
        }

        return new RestoredDocumentSession(
            restoredCandidates,
            _settingsService.ActiveTabIndex);
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
                candidate.Tab.Dispose();
            throw;
        }
        var plannedBySource = plan.Candidates.ToDictionary(
            candidate => candidate.Descriptor.SourceIndex);
        var duplicatesBySource = plan.Duplicates.ToDictionary(
            duplicate => duplicate.Descriptor.SourceIndex);
        var adoptedTabs = new List<EditorTabViewModel>(candidates.Count);
        var discardedDuplicates = new List<EditorTabViewModel>();
        EditorTabViewModel? selectedTab = null;

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (duplicatesBySource.TryGetValue(index, out var duplicate))
            {
                candidate.Tab.Dispose();
                discardedDuplicates.Add(candidate.Tab);
                if (candidate.SessionIndex == restoredSession.ActiveTabIndex)
                {
                    selectedTab = duplicate.Owner.LiveTab ??
                        candidates[duplicate.Owner.PlannedSourceIndex!.Value].Tab;
                }
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
                if (candidate.SessionIndex == restoredSession.ActiveTabIndex)
                    selectedTab = candidate.Tab;
            }
            catch
            {
                if (!liveTabs.Contains(candidate.Tab))
                    candidate.Tab.Dispose();
                foreach (var remaining in candidates.Skip(index + 1))
                    remaining.Tab.Dispose();
                throw;
            }
        }

        if (selectedTab == null && liveTabs.Count > 0)
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
        var sessionFiles = new List<SessionFile>();
        var persistedActiveTabIndex = 0;
        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Temp");
        _fileSystemService.CreateDirectory(tempDir);

        foreach (var tab in tabs)
        {
            if (_shutdownDiscardedTabs.Contains(tab) && string.IsNullOrEmpty(tab.FilePath))
                continue;

            if (tab == selectedTab)
                persistedActiveTabIndex = sessionFiles.Count;

            var sessionFile = new SessionFile
            {
                FilePath = string.IsNullOrEmpty(tab.FilePath) ? tab.FileName : tab.FilePath,
                TabIdentity = tab.AutoSaveIdentity,
                IsUntitled = string.IsNullOrEmpty(tab.FilePath),
                IsBinaryMode = tab.Mode == FileOpenMode.Binary,
                CursorOffset = tab.CursorOffset,
                ScrollOffset = tab.ScrollOffset
            };

            if (sessionFile.IsUntitled && tab.Mode == FileOpenMode.Text)
                PersistUntitledContent(tab, sessionFile, tempDir);

            sessionFiles.Add(sessionFile);
        }

        _settingsService.OpenFiles = sessionFiles;
        _settingsService.ActiveTabIndex = persistedActiveTabIndex;
        _settingsService.Save();
        CleanupTempFiles(
            sessionFiles
                .Select(file => file.TempFilePath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Cast<string>()
                .ToArray());
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
        if (sessionFile.IsUntitled)
        {
            if (sessionFile.IsBinaryMode)
                return null;

            var content = sessionFile.Content ?? string.Empty;
            if (!string.IsNullOrEmpty(sessionFile.TempFilePath) &&
                _fileSystemService.FileExists(sessionFile.TempFilePath))
            {
                content = await _fileSystemService.ReadAllTextAsync(sessionFile.TempFilePath);
            }

            var untitledTab = _tabFactory.CreateUntitled(content);
            untitledTab.FileName = Path.GetFileName(sessionFile.FilePath);
            return untitledTab;
        }

        if (!_fileSystemService.FileExists(sessionFile.FilePath))
            return null;

        var tab = _tabFactory.Create();
        try
        {
            await tab.LoadFileAsync(sessionFile.FilePath);
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

    private void PersistUntitledContent(
        EditorTabViewModel tab,
        SessionFile sessionFile,
        string tempDir)
    {
        var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{tab.FileName}.tmp");
        try
        {
            _fileSystemService.WriteAllTextAtomic(tempPath, tab.Content);
            sessionFile.TempFilePath = tempPath;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to write temp session file '{0}'; storing content in settings instead: {1}",
                tempPath,
                ex.Message);
            sessionFile.Content = tab.Content;
        }
    }

    private void CleanupTempFiles(IReadOnlyCollection<string> preservedPaths)
    {
        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "Temp");
        if (!_fileSystemService.DirectoryExists(tempDir))
            return;

        try
        {
            foreach (var file in _fileSystemService.GetFiles(tempDir, "*.tmp"))
            {
                if (preservedPaths.Contains(file, StringComparer.OrdinalIgnoreCase))
                    continue;

                try
                {
                    _fileSystemService.DeleteFile(file);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Failed to delete temp session file '{0}': {1}", file, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to clean temp session files: {0}", ex.Message);
        }
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
    string? TabIdentity = null);

public sealed record RestoredSessionAdoptionResult(
    IReadOnlyList<EditorTabViewModel> AdoptedTabs,
    IReadOnlyList<EditorTabViewModel> DiscardedDuplicateTabs,
    EditorTabViewModel? SelectedTab);

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
    int CursorOffset,
    double ScrollOffset)
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
            tab.CursorOffset,
            tab.ScrollOffset);

    public static PersistedTabDescriptor FromRecoveryEntry(
        int sourceIndex,
        AutoSaveEntry entry) =>
        new(
            sourceIndex,
            entry.TabIdentity,
            entry.Content,
            entry.IsUntitled,
            entry.FilePath,
            entry.CursorOffset,
            entry.ScrollOffset);

    public bool HasEquivalentState(PersistedTabDescriptor other)
    {
        if (IsUntitled != other.IsUntitled ||
            !string.Equals(Content, other.Content, StringComparison.Ordinal) ||
            CursorOffset != other.CursorOffset ||
            ScrollOffset != other.ScrollOffset)
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

        var identitiesMatch =
            DocumentSessionCoordinator.HasValidPersistedIdentity(
                candidate.PersistedIdentity) &&
            string.Equals(
                ProofIdentity,
                candidate.PersistedIdentity,
                StringComparison.Ordinal);
        var savedPathsMatch =
            !Descriptor.IsUntitled &&
            !candidate.IsUntitled &&
            !string.IsNullOrEmpty(Descriptor.FilePath) &&
            !string.IsNullOrEmpty(candidate.FilePath) &&
            DocumentSessionCoordinator.HasSameOpenIdentity(
                Descriptor.FilePath,
                candidate.FilePath);
        return identitiesMatch || savedPathsMatch;
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
