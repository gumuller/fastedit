using System.Diagnostics;
using System.IO;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public sealed class DocumentSessionCoordinator
{
    private readonly ISettingsService _settingsService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IEditorTabFactory _tabFactory;
    private readonly HashSet<EditorTabViewModel> _shutdownDiscardedTabs = new();

    public DocumentSessionCoordinator(
        ISettingsService settingsService,
        IFileSystemService fileSystemService,
        IEditorTabFactory tabFactory)
    {
        _settingsService = settingsService;
        _fileSystemService = fileSystemService;
        _tabFactory = tabFactory;
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

            return new StagedDocumentSession(stagedTabs, session.ActiveTabIndex);
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

    public async Task<RestoredDocumentSession> RestoreShutdownSessionAsync(
        IReadOnlyList<EditorTabViewModel> existingTabs)
    {
        var restoredTabs = new List<EditorTabViewModel>();
        var openFiles = _settingsService.OpenFiles;
        if (openFiles == null || openFiles.Count == 0)
            return new RestoredDocumentSession(restoredTabs, _settingsService.ActiveTabIndex);

        foreach (var sessionFile in openFiles)
        {
            try
            {
                var tab = await RestoreSessionFileAsync(existingTabs, restoredTabs, sessionFile);
                if (tab != null)
                    restoredTabs.Add(tab);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to restore session file '{0}': {1}",
                    sessionFile.FilePath,
                    ex.Message);
            }
        }

        return new RestoredDocumentSession(restoredTabs, _settingsService.ActiveTabIndex);
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
        foreach (var tab in tabs)
        {
            if (!tab.IsModified && !string.IsNullOrEmpty(tab.FilePath))
                continue;

            entries.Add(new AutoSaveEntry(
                $"tab-{tab.AutoSaveIdentity}",
                tab.FileName,
                string.IsNullOrEmpty(tab.FilePath) ? null : NormalizeFilePath(tab.FilePath),
                tab.Content ?? string.Empty,
                string.IsNullOrEmpty(tab.FilePath),
                tab.CursorOffset,
                tab.ScrollOffset));
        }

        return entries;
    }

    public EditorTabViewModel CreateRecoveryTab(AutoSaveEntry entry)
    {
        var tab = _tabFactory.CreateUntitled(entry.Content);
        tab.FileName = entry.FileName;
        if (!entry.IsUntitled && !string.IsNullOrEmpty(entry.FilePath))
            tab.FilePath = entry.FilePath;
        tab.CursorOffset = entry.CursorOffset;
        tab.ScrollOffset = entry.ScrollOffset;
        tab.IsModified = true;
        return tab;
    }

    public TabRecoveryResult RecoverTabs(IReadOnlyList<AutoSaveEntry> entries)
    {
        var recoveredTabs = new List<EditorTabViewModel>(entries.Count);
        var recoveredEntryIds = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            try
            {
                recoveredTabs.Add(CreateRecoveryTab(entry));
                recoveredEntryIds.Add(entry.Id);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to recover auto-save entry '{0}': {1}", entry.FileName, ex);
            }
        }

        return new TabRecoveryResult(
            recoveredTabs.Count == entries.Count,
            recoveredEntryIds,
            recoveredTabs);
    }

    internal static string NormalizeFilePath(string filePath) =>
        Path.GetFullPath(filePath)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    internal static bool HasSameOpenIdentity(string firstPath, string secondPath) =>
        string.Equals(
            NormalizeFilePath(firstPath),
            NormalizeFilePath(secondPath),
            StringComparison.Ordinal);

    private async Task<EditorTabViewModel?> RestoreSessionFileAsync(
        IReadOnlyList<EditorTabViewModel> existingTabs,
        IReadOnlyList<EditorTabViewModel> restoredTabs,
        SessionFile sessionFile)
    {
        if (sessionFile.IsUntitled)
        {
            if (sessionFile.IsBinaryMode)
                return null;

            var fileName = Path.GetFileName(sessionFile.FilePath);
            if (existingTabs.Concat(restoredTabs).Any(
                tab => string.IsNullOrEmpty(tab.FilePath) && tab.FileName == fileName))
            {
                return null;
            }

            var content = sessionFile.Content ?? string.Empty;
            if (!string.IsNullOrEmpty(sessionFile.TempFilePath) &&
                _fileSystemService.FileExists(sessionFile.TempFilePath))
            {
                content = await _fileSystemService.ReadAllTextAsync(sessionFile.TempFilePath);
            }

            var untitledTab = _tabFactory.CreateUntitled(content);
            untitledTab.FileName = fileName;
            return untitledTab;
        }

        if (!_fileSystemService.FileExists(sessionFile.FilePath) ||
            existingTabs.Concat(restoredTabs).Any(tab =>
                !string.IsNullOrEmpty(tab.FilePath) &&
                HasSameOpenIdentity(tab.FilePath, sessionFile.FilePath)))
        {
            return null;
        }

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

public sealed record RestoredDocumentSession(
    IReadOnlyList<EditorTabViewModel> Tabs,
    int ActiveTabIndex);

public sealed record TabRecoveryResult(
    bool Success,
    IReadOnlyList<string> RecoveredEntryIds,
    IReadOnlyList<EditorTabViewModel>? RecoveredTabs = null);
