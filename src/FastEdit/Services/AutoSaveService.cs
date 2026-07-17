using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public sealed class AutoSaveService : IAutoSaveService
{
    private readonly IFileSystemService _fileSystem;
    private readonly ISettingsService _settings;
    private readonly IDispatcherService _dispatcher;
    private readonly string _autoSaveDir;
    private readonly string _shutdownMarkerPath;
    private readonly string _resolvedEntriesPath;
    private readonly string _activeGenerationMarkerPath;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly object _timerSync = new();
    private readonly object _persistenceSync = new();
    private System.Timers.Timer? _timer;
    private Func<IEnumerable<AutoSaveEntry>>? _entryProvider;
    private int _intervalSeconds;
    private volatile bool _stopped = true;
    private readonly string _activeGenerationId;
    private readonly string _activeContentPrefix;
    private readonly string _activeManifestPath;
    private readonly string _activeCleanupIntentPath;
    private Dictionary<string, RecoveryOrigin> _lastRecoveryOrigins = new(StringComparer.Ordinal);
    private HashSet<string> _lastRecoveryManifestPaths = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _lastRecoveryContentPaths = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled { get; set; } = true;

    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            var normalizedValue = Math.Max(1, value);
            lock (_timerSync)
            {
                _intervalSeconds = normalizedValue;
                if (_timer != null)
                    _timer.Interval = normalizedValue * 1000d;
            }
        }
    }

    public AutoSaveService(
        IFileSystemService fileSystem,
        ISettingsService settings,
        IDispatcherService dispatcher)
    {
        _fileSystem = fileSystem;
        _settings = settings;
        _dispatcher = dispatcher;
        _intervalSeconds = Math.Max(1, settings.AutoSaveIntervalSeconds);
        _autoSaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "AutoSave");
        _shutdownMarkerPath = Path.Combine(_autoSaveDir, ".clean-shutdown");
        _resolvedEntriesPath = Path.Combine(_autoSaveDir, "resolved.json");
        _activeGenerationId = Guid.NewGuid().ToString("N");
        _activeContentPrefix = $"{_activeGenerationId}-";
        _activeManifestPath = Path.Combine(
            _autoSaveDir,
            $"manifest-{_activeGenerationId}.json");
        _activeGenerationMarkerPath = Path.Combine(
            _autoSaveDir,
            $"active-{_activeGenerationId}.lock");
        _activeCleanupIntentPath = GetCleanupIntentPath(_activeGenerationId);
        _settings.AutoSaveIntervalChanged += OnAutoSaveIntervalChanged;
    }

    public void Start()
    {
        lock (_timerSync)
        {
            StopTimer();
            IntervalSeconds = _settings.AutoSaveIntervalSeconds;
            _fileSystem.CreateDirectory(_autoSaveDir);
            RunStartupMaintenance();
            var wasCleanShutdown = _fileSystem.FileExists(_shutdownMarkerPath);
            if (wasCleanShutdown)
                _fileSystem.DeleteFile(_shutdownMarkerPath);
            using (var process = Process.GetCurrentProcess())
            {
                var marker = $"{process.Id}|{process.StartTime.ToUniversalTime().Ticks}";
                _fileSystem.WriteAllTextAtomic(_activeGenerationMarkerPath, marker);
            }

            _stopped = false;
            _timer = new System.Timers.Timer(IntervalSeconds * 1000d)
            {
                AutoReset = true
            };
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
        }
    }

    public void Stop()
    {
        lock (_timerSync)
        {
            _stopped = true;
            StopTimer();
        }
    }

    public void SetEntryProvider(Func<IEnumerable<AutoSaveEntry>> provider)
    {
        _entryProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    internal async Task RunAutoSaveAsync()
    {
        if (_stopped || !IsEnabled || _entryProvider == null)
            return;

        if (!await _runGate.WaitAsync(0))
            return;

        try
        {
            var entries = await _dispatcher.InvokeAsync(() => _entryProvider().ToArray());
            if (_stopped || entries.Length == 0)
                return;

            await Task.Run(() => SaveSnapshot(entries, requireRunning: true));
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Auto-save timer failed: {0}", ex);
        }
        finally
        {
            _runGate.Release();
        }
    }

    public void SaveNow(IEnumerable<AutoSaveEntry> entries)
    {
        SaveSnapshot(entries.ToArray(), requireRunning: false);
    }

    private void SaveSnapshot(
        AutoSaveEntry[] snapshot,
        bool requireRunning,
        bool verifyWrites = false)
    {
        lock (_persistenceSync)
        {
            if (requireRunning && _stopped)
                return;

            _fileSystem.CreateDirectory(_autoSaveDir);
            var manifest = new List<AutoSaveManifestEntry>(snapshot.Length);

            foreach (var entry in snapshot)
            {
                var contentFile = $"{_activeContentPrefix}{entry.Id}.txt";
                _fileSystem.WriteAllTextAtomic(
                    Path.Combine(_autoSaveDir, contentFile),
                    entry.Content);

                manifest.Add(new AutoSaveManifestEntry
                {
                    Id = entry.Id,
                    TabIdentity = entry.TabIdentity,
                    FileName = entry.FileName,
                    FilePath = entry.FilePath,
                    ContentFile = contentFile,
                    IsUntitled = entry.IsUntitled,
                    CursorOffset = entry.CursorOffset,
                    ScrollOffset = entry.ScrollOffset
                });
            }

            var json = JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions { WriteIndented = true });
            _fileSystem.WriteAllTextAtomic(
                _activeManifestPath,
                json);

            if (verifyWrites)
                VerifySnapshot(snapshot);
        }
    }

    private void VerifySnapshot(IReadOnlyCollection<AutoSaveEntry> snapshot)
    {
        var storedManifest = JsonSerializer.Deserialize<List<AutoSaveManifestEntry>>(
            _fileSystem.ReadAllText(_activeManifestPath))
            ?? throw new InvalidDataException("The replacement recovery manifest contains no entries.");
        if (storedManifest.Count != snapshot.Count)
            throw new InvalidDataException("The replacement recovery manifest is incomplete.");

        var storedById = storedManifest.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        foreach (var entry in snapshot)
        {
            if (!storedById.TryGetValue(entry.Id, out var stored) ||
                stored.FileName != entry.FileName ||
                stored.FilePath != entry.FilePath ||
                stored.IsUntitled != entry.IsUntitled ||
                stored.CursorOffset != entry.CursorOffset ||
                stored.ScrollOffset != entry.ScrollOffset)
            {
                throw new InvalidDataException(
                    $"The replacement recovery metadata for '{entry.FileName}' could not be verified.");
            }

            var storedContent = _fileSystem.ReadAllText(
                Path.Combine(_autoSaveDir, stored.ContentFile));
            if (!string.Equals(storedContent, entry.Content, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The replacement recovery content for '{entry.FileName}' could not be verified.");
            }
        }
    }

    public bool MarkCleanShutdown()
    {
        lock (_persistenceSync)
        {
            _fileSystem.CreateDirectory(_autoSaveDir);
            return ClearActiveGenerationCore();
        }
    }

    public bool HasRecoveryFiles()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir))
            return false;

        lock (_persistenceSync)
        {
            RunStartupMaintenance();
        }

        if (_fileSystem.FileExists(_shutdownMarkerPath))
            return false;

        return GetManifestPaths().Count > 0;
    }

    public RecoveryEntriesResult GetRecoveryEntries()
    {
        lock (_persistenceSync)
        {
            var manifestPaths = GetManifestPaths();
            if (manifestPaths.Count == 0)
                return new RecoveryEntriesResult(true, Array.Empty<AutoSaveEntry>());

            var entriesById = new Dictionary<string, AutoSaveEntry>(StringComparer.Ordinal);
            var errors = new List<string>();
            var resolvedEntries = LoadResolvedEntries(errors);
            _lastRecoveryOrigins = new Dictionary<string, RecoveryOrigin>(StringComparer.Ordinal);
            _lastRecoveryManifestPaths = manifestPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _lastRecoveryContentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var manifestPath in manifestPaths.OrderBy(_fileSystem.GetLastWriteTime))
                {
                    ReadRecoveryManifest(
                        manifestPath,
                        entriesById,
                        resolvedEntries,
                        errors);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to enumerate auto-save recovery manifests: {0}", ex);
                errors.Add($"Failed to enumerate recovery manifests: {ex.Message}");
            }

            return new RecoveryEntriesResult(
                errors.Count == 0,
                entriesById.Values.ToArray(),
                errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
        }
    }

    private void ReadRecoveryManifest(
        string manifestPath,
        IDictionary<string, AutoSaveEntry> entriesById,
        IReadOnlyDictionary<string, HashSet<string>> resolvedEntries,
        ICollection<string> errors)
    {
        try
        {
            var json = _fileSystem.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<List<AutoSaveManifestEntry>>(json)
                ?? throw new InvalidDataException("The auto-save manifest contains no entries.");

            foreach (var item in manifest)
            {
                var manifestName = Path.GetFileName(manifestPath);
                if (resolvedEntries.TryGetValue(manifestName, out var resolvedIds) &&
                    resolvedIds.Contains(item.Id))
                {
                    continue;
                }

                var contentPath = Path.Combine(_autoSaveDir, item.ContentFile);
                _lastRecoveryContentPaths.Add(contentPath);
                if (!_fileSystem.FileExists(contentPath))
                {
                    errors.Add($"Recovery content for '{item.FileName}' is missing.");
                    continue;
                }

                try
                {
                    var recoveryEntryId = $"{manifestName}:{item.Id}";
                    entriesById[recoveryEntryId] = new AutoSaveEntry(
                        recoveryEntryId,
                        item.FileName,
                        item.FilePath,
                        _fileSystem.ReadAllText(contentPath),
                        item.IsUntitled,
                        item.CursorOffset,
                        item.ScrollOffset,
                        item.TabIdentity);
                    _lastRecoveryOrigins[recoveryEntryId] =
                        new RecoveryOrigin(manifestName, item.Id);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to read recovery content for '{item.FileName}': {ex.Message}");
                }
            }

        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to read auto-save recovery manifest: {0}", ex);
            errors.Add($"Failed to read recovery manifest '{Path.GetFileName(manifestPath)}': {ex.Message}");
        }
    }

    public bool RecordRecoveredEntries(IEnumerable<string> entryIds)
    {
        lock (_persistenceSync)
        {
            try
            {
                RecordRecoveredEntriesCore(entryIds);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to record recovered auto-save entries: {0}", ex);
                return false;
            }
        }
    }

    public bool CompleteRecovery(
        IEnumerable<AutoSaveEntry> replacementEntries,
        IEnumerable<string> recoveredEntryIds,
        bool allEntriesRecovered)
    {
        lock (_persistenceSync)
        {
            try
            {
                var snapshot = replacementEntries.ToArray();
                if (snapshot.Length > 0)
                    SaveSnapshot(snapshot, requireRunning: false, verifyWrites: true);

                if (allEntriesRecovered)
                    return ClearRecoveryFilesCore();

                RecordRecoveredEntriesCore(recoveredEntryIds);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to complete auto-save recovery: {0}", ex);
                return false;
            }
        }
    }

    private void RecordRecoveredEntriesCore(IEnumerable<string> entryIds)
    {
        var errors = new List<string>();
        var resolvedEntries = LoadResolvedEntries(errors);
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));

        foreach (var entryId in entryIds.Distinct(StringComparer.Ordinal))
        {
            if (!_lastRecoveryOrigins.TryGetValue(entryId, out var origin))
                continue;

            if (!resolvedEntries.TryGetValue(origin.ManifestName, out var resolvedIds))
            {
                resolvedIds = new HashSet<string>(StringComparer.Ordinal);
                resolvedEntries[origin.ManifestName] = resolvedIds;
            }
            resolvedIds.Add(origin.EntryId);
        }

        SaveResolvedEntries(resolvedEntries);
    }

    private Dictionary<string, HashSet<string>> LoadResolvedEntries(ICollection<string> errors)
    {
        if (!_fileSystem.FileExists(_resolvedEntriesPath))
            return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = _fileSystem.ReadAllText(_resolvedEntriesPath);
            var stored = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json)
                ?? new Dictionary<string, string[]>();
            return stored.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToHashSet(StringComparer.Ordinal),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to read recovered-entry state: {0}", ex);
            errors.Add($"Failed to read recovered-entry state: {ex.Message}");
            return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool ClearRecoveryFiles()
    {
        lock (_persistenceSync)
        {
            return ClearRecoveryFilesCore();
        }
    }

    private bool ClearRecoveryFilesCore()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir))
            return true;

        try
        {
            var manifestPaths = _lastRecoveryManifestPaths.Count > 0
                ? _lastRecoveryManifestPaths.ToArray()
                : GetManifestPaths().ToArray();
            var errors = new List<string>();
            var resolvedEntries = LoadResolvedEntries(errors);
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            var allRetired = true;
            var retiredManifests = new List<string>();
            foreach (var manifestPath in manifestPaths)
            {
                var contentPaths = GetGenerationContentPaths(manifestPath);
                var markerPath = GetGenerationMarkerPath(manifestPath);
                if (RetireGeneration(
                    manifestPath,
                    contentPaths,
                    markerPath,
                    out var cleanupComplete))
                {
                    retiredManifests.Add(manifestPath);
                    if (!cleanupComplete)
                        allRetired = false;
                }
                else
                {
                    allRetired = false;
                }
            }

            foreach (var manifestPath in retiredManifests)
            {
                var manifestName = Path.GetFileName(manifestPath);
                if (!string.IsNullOrEmpty(manifestName))
                    resolvedEntries.Remove(manifestName);
            }

            SaveResolvedEntries(resolvedEntries);
            _lastRecoveryManifestPaths.ExceptWith(retiredManifests);
            _lastRecoveryContentPaths.Clear();
            _lastRecoveryOrigins.Clear();
            return allRetired;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to clear auto-save recovery files: {0}", ex);
            return false;
        }
    }

    private bool ClearActiveGenerationCore()
    {
        try
        {
            if (_fileSystem.FileExists(_activeManifestPath))
            {
                var contentPaths = GetGenerationContentPaths(_activeManifestPath);
                var retired = RetireGeneration(
                    _activeManifestPath,
                    contentPaths,
                    _activeGenerationMarkerPath,
                    out var cleanupComplete);
                return retired && cleanupComplete;
            }

            var manifestlessContentPaths = _fileSystem.GetFiles(
                _autoSaveDir,
                $"{_activeContentPrefix}*.txt");
            if (manifestlessContentPaths.Length > 0)
            {
                if (!TryWriteCleanupIntent(
                    _activeCleanupIntentPath,
                    _activeGenerationId,
                    manifestlessContentPaths))
                {
                    return false;
                }

                return CleanupIntendedGeneration(
                    _activeCleanupIntentPath,
                    manifestlessContentPaths,
                    _activeGenerationMarkerPath);
            }

            if (_fileSystem.FileExists(_activeGenerationMarkerPath))
                _fileSystem.DeleteFile(_activeGenerationMarkerPath);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to clear current auto-save generation: {0}", ex);
            return false;
        }
    }

    private string[] GetGenerationContentPaths(string manifestPath)
    {
        var contentPaths = CaptureRecoveryContentPaths(new[] { manifestPath })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddGenerationContentPaths(Path.GetFileName(manifestPath), contentPaths);
        return contentPaths.ToArray();
    }

    private void AddGenerationContentPaths(
        string manifestName,
        ISet<string> contentPaths)
    {
        var fileName = Path.GetFileNameWithoutExtension(manifestName);
        const string prefix = "manifest-";
        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var generationId = fileName[prefix.Length..];
            foreach (var path in _fileSystem.GetFiles(_autoSaveDir, $"{generationId}-*.txt"))
                contentPaths.Add(path);
        }
    }

    private void CleanupRetiredGenerations()
    {
        foreach (var retiredManifestPath in _fileSystem.GetFiles(_autoSaveDir, ".retired-*.json"))
        {
            var cleanupComplete = true;
            var originalManifestName = GetOriginalManifestName(retiredManifestPath);
            var contentPaths = CaptureRecoveryContentPaths(new[] { retiredManifestPath })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (originalManifestName != null)
                AddGenerationContentPaths(originalManifestName, contentPaths);

            foreach (var contentPath in contentPaths)
            {
                try
                {
                    if (_fileSystem.FileExists(contentPath))
                        _fileSystem.DeleteFile(contentPath);
                }
                catch (Exception ex)
                {
                    cleanupComplete = false;
                    Trace.TraceWarning(
                        "Failed to clean quarantined auto-save content '{0}': {1}",
                        contentPath,
                        ex);
                }
            }

            var markerPath = originalManifestName == null
                ? null
                : GetGenerationMarkerPath(Path.Combine(_autoSaveDir, originalManifestName));
            if (markerPath != null)
            {
                try
                {
                    if (_fileSystem.FileExists(markerPath))
                        _fileSystem.DeleteFile(markerPath);
                }
                catch (Exception ex)
                {
                    cleanupComplete = false;
                    Trace.TraceWarning(
                        "Failed to clean quarantined auto-save marker '{0}': {1}",
                        markerPath,
                        ex);
                }
            }

            if (!cleanupComplete)
                continue;

            try
            {
                _fileSystem.DeleteFile(retiredManifestPath);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to clean quarantined auto-save manifest '{0}': {1}",
                    retiredManifestPath,
                    ex);
            }
        }
    }

    private static string? GetOriginalManifestName(string retiredManifestPath)
    {
        var fileName = Path.GetFileName(retiredManifestPath);
        var manifestIndex = fileName.IndexOf("manifest", StringComparison.OrdinalIgnoreCase);
        return manifestIndex < 0 ? null : fileName[manifestIndex..];
    }

    private bool RetireGeneration(
        string manifestPath,
        IReadOnlyCollection<string> contentPaths,
        string? markerPath,
        out bool cleanupComplete)
    {
        cleanupComplete = false;
        var retiredManifestPath = Path.Combine(
            _autoSaveDir,
            $".retired-{Guid.NewGuid():N}-{Path.GetFileName(manifestPath)}");
        try
        {
            _fileSystem.MoveFile(manifestPath, retiredManifestPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to quarantine auto-save manifest '{0}': {1}",
                manifestPath,
                ex);
            return false;
        }

        cleanupComplete = true;
        foreach (var contentPath in contentPaths)
        {
            try
            {
                if (_fileSystem.FileExists(contentPath))
                    _fileSystem.DeleteFile(contentPath);
            }
            catch (Exception ex)
            {
                cleanupComplete = false;
                Trace.TraceWarning(
                    "Failed to delete retired auto-save content '{0}': {1}",
                    contentPath,
                    ex);
            }
        }

        if (markerPath != null)
        {
            try
            {
                if (_fileSystem.FileExists(markerPath))
                    _fileSystem.DeleteFile(markerPath);
            }
            catch (Exception ex)
            {
                cleanupComplete = false;
                Trace.TraceWarning(
                    "Failed to delete retired auto-save marker '{0}': {1}",
                    markerPath,
                    ex);
            }
        }

        if (cleanupComplete)
        {
            try
            {
                _fileSystem.DeleteFile(retiredManifestPath);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to delete quarantined auto-save manifest '{0}': {1}",
                    retiredManifestPath,
                    ex);
            }
        }

        return true;
    }

    private HashSet<string> CleanupIntendedManifestlessGenerations()
    {
        var intendedGenerations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var intentPath in _fileSystem.GetFiles(
            _autoSaveDir,
            ".cleanup-*.json"))
        {
            if (!TryReadCleanupIntent(
                intentPath,
                out var generationId,
                out var contentPaths))
            {
                continue;
            }

            intendedGenerations.Add(generationId);
            CleanupIntendedGeneration(
                intentPath,
                contentPaths,
                Path.Combine(_autoSaveDir, $"active-{generationId}.lock"));
        }
        return intendedGenerations;
    }

    private void RunStartupMaintenance()
    {
        CleanupRetiredGenerations();
        var intendedCleanupGenerations = CleanupIntendedManifestlessGenerations();
        PreserveAbandonedManifestlessGenerations(intendedCleanupGenerations);
    }

    private bool CleanupIntendedGeneration(
        string intentPath,
        IReadOnlyCollection<string> contentPaths,
        string markerPath)
    {
        var cleanupComplete = true;
        foreach (var contentPath in contentPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (_fileSystem.FileExists(contentPath))
                    _fileSystem.DeleteFile(contentPath);
            }
            catch (Exception ex)
            {
                cleanupComplete = false;
                Trace.TraceWarning(
                    "Failed to clean intended auto-save content '{0}': {1}",
                    contentPath,
                    ex);
            }
        }

        if (!cleanupComplete)
            return false;

        try
        {
            if (_fileSystem.FileExists(markerPath))
                _fileSystem.DeleteFile(markerPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to clean intended auto-save marker '{0}': {1}",
                markerPath,
                ex);
            return false;
        }

        try
        {
            _fileSystem.DeleteFile(intentPath);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to remove completed auto-save cleanup intent '{0}': {1}",
                intentPath,
                ex);
            return false;
        }
    }

    private bool TryWriteCleanupIntent(
        string intentPath,
        string generationId,
        IReadOnlyCollection<string> contentPaths)
    {
        try
        {
            var contentFiles = contentPaths
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(fileName =>
                    fileName.StartsWith(
                        $"{generationId}-",
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (contentFiles.Length != contentPaths.Count)
                throw new InvalidDataException("Cleanup content does not match its auto-save generation.");

            var json = JsonSerializer.Serialize(
                new CleanupIntent(generationId, contentFiles),
                new JsonSerializerOptions { WriteIndented = true });
            _fileSystem.WriteAllTextAtomic(intentPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to persist auto-save cleanup intent '{0}': {1}",
                intentPath,
                ex);
            return false;
        }
    }

    private bool TryReadCleanupIntent(
        string intentPath,
        out string generationId,
        out string[] contentPaths)
    {
        generationId = "";
        contentPaths = Array.Empty<string>();
        try
        {
            var intent = JsonSerializer.Deserialize<CleanupIntent>(
                _fileSystem.ReadAllText(intentPath));
            if (intent == null ||
                !Guid.TryParseExact(intent.GenerationId, "N", out _) ||
                !string.Equals(
                    intentPath,
                    GetCleanupIntentPath(intent.GenerationId),
                    StringComparison.OrdinalIgnoreCase) ||
                intent.ContentFiles.Any(fileName =>
                    string.IsNullOrEmpty(fileName) ||
                    Path.GetFileName(fileName) != fileName ||
                    !fileName.StartsWith(
                        $"{intent.GenerationId}-",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("The auto-save cleanup intent is invalid.");
            }

            generationId = intent.GenerationId;
            contentPaths = intent.ContentFiles
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(fileName => Path.Combine(_autoSaveDir, fileName))
                .ToArray();
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to read auto-save cleanup intent '{0}': {1}",
                intentPath,
                ex);
            return false;
        }
    }

    private void PreserveAbandonedManifestlessGenerations(
        IReadOnlySet<string> intendedCleanupGenerations)
    {
        var retiredManifestNames = _fileSystem
            .GetFiles(_autoSaveDir, ".retired-*.json")
            .Select(GetOriginalManifestName)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var markerPath in _fileSystem.GetFiles(_autoSaveDir, "active-*.lock"))
        {
            var markerName = Path.GetFileNameWithoutExtension(markerPath);
            const string markerPrefix = "active-";
            if (!markerName.StartsWith(markerPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var generationId = markerName[markerPrefix.Length..];
            var manifestName = $"manifest-{generationId}.json";
            var manifestPath = Path.Combine(_autoSaveDir, manifestName);
            if (_fileSystem.FileExists(manifestPath) ||
                intendedCleanupGenerations.Contains(generationId) ||
                retiredManifestNames.Contains(manifestName) ||
                IsActiveGeneration(manifestPath))
            {
                continue;
            }

            var contentPaths = _fileSystem.GetFiles(
                _autoSaveDir,
                $"{generationId}-*.txt");
            if (contentPaths.Length == 0)
            {
                TryDeleteAbandonedMarker(markerPath);
                continue;
            }

            TryWriteSyntheticManifest(manifestPath, contentPaths);
        }
    }

    private void TryDeleteAbandonedMarker(string markerPath)
    {
        try
        {
            if (_fileSystem.FileExists(markerPath))
                _fileSystem.DeleteFile(markerPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to delete abandoned auto-save marker '{0}': {1}",
                markerPath,
                ex);
        }
    }

    private bool TryWriteSyntheticManifest(
        string manifestPath,
        IReadOnlyCollection<string> contentPaths)
    {
        try
        {
            var manifest = contentPaths.Select(contentPath => new AutoSaveManifestEntry
            {
                Id = Path.GetFileNameWithoutExtension(contentPath),
                FileName = Path.GetFileName(contentPath),
                ContentFile = Path.GetFileName(contentPath),
                IsUntitled = true
            });
            var json = JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions { WriteIndented = true });
            _fileSystem.WriteAllTextAtomic(manifestPath, json);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to persist recovery metadata for manifest-less generation '{0}': {1}",
                manifestPath,
                ex);
            return false;
        }
    }

    private string GetCleanupIntentPath(string generationId) =>
        Path.Combine(_autoSaveDir, $".cleanup-{generationId}.json");

    private IReadOnlyList<string> GetManifestPaths()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir))
            return Array.Empty<string>();

        return _fileSystem.GetFiles(_autoSaveDir, "manifest*.json")
            .Where(path => !IsActiveGeneration(path))
            .ToArray();
    }

    private bool IsActiveGeneration(string manifestPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(manifestPath);
        const string prefix = "manifest-";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var markerPath = GetGenerationMarkerPath(manifestPath)!;
        if (!_fileSystem.FileExists(markerPath))
            return false;

        try
        {
            var parts = _fileSystem.ReadAllText(markerPath).Split('|');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out var processId) ||
                !long.TryParse(parts[1], out var startTimeTicks))
            {
                return false;
            }

            using var process = Process.GetProcessById(processId);
            return !process.HasExited &&
                process.StartTime.ToUniversalTime().Ticks == startTimeTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Trace.TraceWarning(
                "Could not inspect the process owning auto-save generation '{0}'; treating it as active.",
                manifestPath);
            return true;
        }
        catch (NotSupportedException)
        {
            Trace.TraceWarning(
                "Could not inspect the process owning auto-save generation '{0}'; treating it as active.",
                manifestPath);
            return true;
        }
    }

    private string? GetGenerationMarkerPath(string manifestPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(manifestPath);
        const string prefix = "manifest-";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var generationId = fileName[prefix.Length..];
        return Path.Combine(_autoSaveDir, $"active-{generationId}.lock");
    }

    private string[] CaptureRecoveryContentPaths(IEnumerable<string> manifestPaths)
    {
        var contentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifestPath in manifestPaths)
        {
            try
            {
                var json = _fileSystem.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<List<AutoSaveManifestEntry>>(json);
                if (manifest == null)
                    continue;

                foreach (var item in manifest)
                    contentPaths.Add(Path.Combine(_autoSaveDir, item.ContentFile));
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to enumerate content for recovery manifest '{0}': {1}",
                    manifestPath,
                    ex);
            }
        }
        return contentPaths.ToArray();
    }

    private void SaveResolvedEntries(Dictionary<string, HashSet<string>> resolvedEntries)
    {
        if (resolvedEntries.Count == 0)
        {
            if (_fileSystem.FileExists(_resolvedEntriesPath))
                _fileSystem.DeleteFile(_resolvedEntriesPath);
            return;
        }

        var serializable = resolvedEntries.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(id => id).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(
            serializable,
            new JsonSerializerOptions { WriteIndented = true });
        _fileSystem.WriteAllTextAtomic(_resolvedEntriesPath, json);
    }

    private async void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        await RunAutoSaveAsync();
    }

    private void OnAutoSaveIntervalChanged(object? sender, EventArgs e)
    {
        IntervalSeconds = _settings.AutoSaveIntervalSeconds;
    }

    private void StopTimer()
    {
        if (_timer == null)
            return;

        _timer.Stop();
        _timer.Elapsed -= OnTimerElapsed;
        _timer.Dispose();
        _timer = null;
    }

    private sealed class AutoSaveManifestEntry
    {
        public string Id { get; set; } = "";
        public string? TabIdentity { get; set; }
        public string FileName { get; set; } = "";
        public string? FilePath { get; set; }
        public string ContentFile { get; set; } = "";
        public bool IsUntitled { get; set; }
        public int CursorOffset { get; set; }
        public double ScrollOffset { get; set; }
    }

    private sealed record RecoveryOrigin(string ManifestName, string EntryId);
    private sealed record CleanupIntent(string GenerationId, string[] ContentFiles);
}
