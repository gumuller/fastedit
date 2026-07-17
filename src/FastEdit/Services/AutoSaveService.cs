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
    private readonly string _activeContentPrefix;
    private readonly string _activeManifestPath;
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
        var generationId = Guid.NewGuid().ToString("N");
        _activeContentPrefix = $"{generationId}-";
        _activeManifestPath = Path.Combine(_autoSaveDir, $"manifest-{generationId}.json");
        _activeGenerationMarkerPath = Path.Combine(_autoSaveDir, $"active-{generationId}.lock");
        _settings.AutoSaveIntervalChanged += OnAutoSaveIntervalChanged;
    }

    public void Start()
    {
        lock (_timerSync)
        {
            StopTimer();
            IntervalSeconds = _settings.AutoSaveIntervalSeconds;
            _fileSystem.CreateDirectory(_autoSaveDir);
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

    private void SaveSnapshot(AutoSaveEntry[] snapshot, bool requireRunning)
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
                        item.ScrollOffset);
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
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to record recovered auto-save entries: {0}", ex);
                return false;
            }
        }
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
            var contentPaths = _lastRecoveryContentPaths
                .Concat(CaptureRecoveryContentPaths(manifestPaths))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var errors = new List<string>();
            var resolvedEntries = LoadResolvedEntries(errors);
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            foreach (var file in contentPaths)
                _fileSystem.DeleteFile(file);

            foreach (var manifestPath in manifestPaths)
            {
                _fileSystem.DeleteFile(manifestPath);
                var markerPath = GetGenerationMarkerPath(manifestPath);
                if (markerPath != null && _fileSystem.FileExists(markerPath))
                    _fileSystem.DeleteFile(markerPath);
            }

            foreach (var manifestPath in manifestPaths)
            {
                var manifestName = Path.GetFileName(manifestPath);
                if (!string.IsNullOrEmpty(manifestName))
                    resolvedEntries.Remove(manifestName);
            }
            SaveResolvedEntries(resolvedEntries);
            _lastRecoveryManifestPaths.Clear();
            _lastRecoveryContentPaths.Clear();
            _lastRecoveryOrigins.Clear();
            return true;
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
            if (!string.IsNullOrEmpty(_activeContentPrefix))
            {
                foreach (var file in _fileSystem.GetFiles(_autoSaveDir, $"{_activeContentPrefix}*.txt"))
                    _fileSystem.DeleteFile(file);
            }

            if (_fileSystem.FileExists(_activeManifestPath))
                _fileSystem.DeleteFile(_activeManifestPath);
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
        public string FileName { get; set; } = "";
        public string? FilePath { get; set; }
        public string ContentFile { get; set; } = "";
        public bool IsUntitled { get; set; }
        public int CursorOffset { get; set; }
        public double ScrollOffset { get; set; }
    }

    private sealed record RecoveryOrigin(string ManifestName, string EntryId);
}
