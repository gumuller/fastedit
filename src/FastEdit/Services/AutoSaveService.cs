using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;

namespace FastEdit.Services;

public sealed class AutoSaveService : IAutoSaveService
{
    private const string BinaryContentFormat = "binary-v1";
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
    private readonly HashSet<string> _pendingAdoptionContentPaths =
        new(StringComparer.OrdinalIgnoreCase);
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
        : this(
            fileSystem,
            settings,
            dispatcher,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FastEdit",
                "AutoSave"),
            Guid.NewGuid().ToString("N"))
    {
    }

    internal AutoSaveService(
        IFileSystemService fileSystem,
        ISettingsService settings,
        IDispatcherService dispatcher,
        string autoSaveDir,
        string activeGenerationId)
    {
        _fileSystem = fileSystem;
        _settings = settings;
        _dispatcher = dispatcher;
        _intervalSeconds = Math.Max(1, settings.AutoSaveIntervalSeconds);
        _autoSaveDir = Path.GetFullPath(autoSaveDir);
        _shutdownMarkerPath = Path.Combine(_autoSaveDir, ".clean-shutdown");
        _resolvedEntriesPath = Path.Combine(_autoSaveDir, "resolved.json");
        if (!Guid.TryParseExact(activeGenerationId, "N", out _))
            throw new ArgumentException(
                "The auto-save generation identity is invalid.",
                nameof(activeGenerationId));
        _activeGenerationId = activeGenerationId;
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
        List<(AutoSaveEntry Entry, string Path)> payloadAdoptions;
        HashSet<string> previousContentPaths;
        List<string> createdContentPaths;
        lock (_persistenceSync)
        {
            if (requireRunning && _stopped)
                return;

            _fileSystem.CreateDirectory(_autoSaveDir);
            previousContentPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (_fileSystem.FileExists(_activeManifestPath) &&
                !TryCaptureRecoveryContentPaths(
                    new[] { _activeManifestPath },
                    previousContentPaths))
            {
                throw new InvalidDataException(
                    "The previous auto-save manifest could not be preserved safely.");
            }
            var manifest = new List<AutoSaveManifestEntry>(snapshot.Length);
            var contentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            payloadAdoptions = new List<(AutoSaveEntry Entry, string Path)>();
            createdContentPaths = new List<string>(snapshot.Length);
            var manifestPublished = false;
            var snapshotIdentity = Guid.NewGuid().ToString("N");
            var requiresVerification = verifyWrites ||
                snapshot.Any(entry =>
                    entry.Mode == FileOpenMode.Binary ||
                    !string.IsNullOrEmpty(entry.ContentFormat));
            var writingManifestPath = requiresVerification
                ? Path.Combine(
                    _autoSaveDir,
                    $"manifest-{_activeGenerationId}.writing")
                : null;
            var candidateManifestPath = requiresVerification
                ? Path.Combine(
                    _autoSaveDir,
                    $"manifest-{_activeGenerationId}.candidate")
                : null;

            try
            {
                if (writingManifestPath is not null)
                {
                    _fileSystem.WriteAllTextAtomic(
                        writingManifestPath,
                        """{"State":"Writing"}""");
                }

                foreach (var entry in snapshot)
                {
                    var contentFile =
                        $"{_activeContentPrefix}{snapshotIdentity}-{entry.Id}.txt";
                    if (!TryResolveContentPath(
                            _activeManifestPath,
                            contentFile,
                            out var contentPath) ||
                        !contentFiles.Add(contentFile))
                    {
                        throw new InvalidDataException(
                            $"The auto-save identity for '{entry.FileName}' is not storage-safe and unique.");
                    }

                    var contentFormat = WriteSnapshotContent(entry, contentPath);
                    createdContentPaths.Add(contentPath);
                    string? contentHash = null;
                    long contentLength = -1;
                    if (!string.IsNullOrEmpty(contentFormat))
                    {
                        (contentHash, contentLength) =
                            ComputeContentFingerprint(contentPath);
                        if ((!string.IsNullOrEmpty(entry.ContentHash) &&
                             !string.Equals(
                                 entry.ContentHash,
                                 contentHash,
                                 StringComparison.Ordinal)) ||
                            (entry.ContentLength >= 0 &&
                             entry.ContentLength != contentLength))
                        {
                            throw new InvalidDataException(
                                $"The auto-save payload for '{entry.FileName}' changed while it was being persisted.");
                        }
                    }

                    manifest.Add(new AutoSaveManifestEntry
                    {
                        Id = entry.Id,
                        TabIdentity = entry.TabIdentity,
                        FileName = entry.FileName,
                        FilePath = entry.FilePath,
                        ContentFile = contentFile,
                        IsUntitled = entry.IsUntitled,
                        CursorOffset = entry.CursorOffset,
                        ScrollOffset = entry.ScrollOffset,
                        Mode = entry.Mode,
                        IsModified = entry.IsModified,
                        EncodingCodePage = entry.EncodingCodePage,
                        HasBom = entry.HasBom,
                        HexOffset = entry.HexOffset,
                        BytesPerRow = entry.BytesPerRow,
                        LargeFileTopLine = entry.LargeFileTopLine,
                        ContentFormat = contentFormat,
                        ContentHash = contentHash,
                        ContentLength = contentLength
                    });
                    if (entry.AdoptPersistedContent != null)
                        payloadAdoptions.Add((entry, contentPath));
                }

                var json = JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions { WriteIndented = true });
                if (requiresVerification)
                {
                    _fileSystem.WriteAllTextAtomic(
                        writingManifestPath!,
                        json);
                    VerifySnapshot(
                        snapshot,
                        manifest,
                        writingManifestPath!);
                    _fileSystem.MoveFile(
                        writingManifestPath!,
                        candidateManifestPath!,
                        overwrite: true);
                    _fileSystem.MoveFile(
                        candidateManifestPath!,
                        _activeManifestPath,
                        overwrite: true);
                }
                else
                {
                    _fileSystem.WriteAllTextAtomic(
                        _activeManifestPath,
                        json);
                }
                manifestPublished = true;
                _pendingAdoptionContentPaths.UnionWith(
                    payloadAdoptions.Select(adoption => adoption.Path));
            }
            catch
            {
                if (!manifestPublished)
                    DeleteUnpublishedContent(createdContentPaths);
                if (!string.IsNullOrEmpty(writingManifestPath))
                    DeleteStagedManifest(writingManifestPath);
                if (!string.IsNullOrEmpty(candidateManifestPath))
                    DeleteStagedManifest(candidateManifestPath);
                throw;
            }
        }

        try
        {
            if (payloadAdoptions.Count > 0)
            {
                _dispatcher.Invoke(() =>
                {
                    foreach (var adoption in payloadAdoptions)
                    {
                        if (!adoption.Entry.AdoptPersistedContent!(adoption.Path))
                        {
                            throw new InvalidOperationException(
                                $"The persisted payload for '{adoption.Entry.FileName}' could not be adopted.");
                        }
                    }
                });
            }
        }
        finally
        {
            lock (_persistenceSync)
            {
                ReleasePersistedContentPins(
                    payloadAdoptions.Select(adoption => adoption.Path));
            }
        }

        lock (_persistenceSync)
        {
            RetireReplacedActiveContent(
                previousContentPaths,
                createdContentPaths);
        }
    }

    private void DeleteStagedManifest(string manifestPath)
    {
        try
        {
            if (_fileSystem.FileExists(manifestPath))
                _fileSystem.DeleteFile(manifestPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning(
                "Failed to remove staged auto-save manifest '{0}': {1}",
                manifestPath,
                ex.Message);
        }
    }

    private void ReleasePersistedContentPins(IEnumerable<string> contentPaths)
    {
        var releasedPaths = contentPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _pendingAdoptionContentPaths.ExceptWith(releasedPaths);
        var activeContentPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        if (_fileSystem.FileExists(_activeManifestPath) &&
            !TryCaptureRecoveryContentPaths(
                new[] { _activeManifestPath },
                activeContentPaths))
        {
            return;
        }

        foreach (var contentPath in releasedPaths)
        {
            if (_pendingAdoptionContentPaths.Contains(contentPath) ||
                activeContentPaths.Contains(contentPath))
            {
                continue;
            }

            try
            {
                if (_fileSystem.FileExists(contentPath))
                    _fileSystem.DeleteFile(contentPath);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to retire superseded adopted content '{0}': {1}",
                    contentPath,
                    ex.Message);
            }
        }
    }

    private void RetireReplacedActiveContent(
        IEnumerable<string> previousContentPaths,
        IReadOnlyCollection<string> publishedContentPaths)
    {
        var retainedPaths = publishedContentPaths.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (var previousPath in previousContentPaths)
        {
            if (retainedPaths.Contains(previousPath))
                continue;
            if (_pendingAdoptionContentPaths.Contains(previousPath))
                continue;
            try
            {
                if (_fileSystem.FileExists(previousPath))
                    _fileSystem.DeleteFile(previousPath);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to retire replaced auto-save content '{0}': {1}",
                    previousPath,
                    ex.Message);
            }
        }
    }

    private void DeleteUnpublishedContent(IEnumerable<string> contentPaths)
    {
        foreach (var contentPath in contentPaths)
        {
            try
            {
                if (_fileSystem.FileExists(contentPath))
                    _fileSystem.DeleteFile(contentPath);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to remove unpublished auto-save content '{0}': {1}",
                    contentPath,
                    ex.Message);
            }
        }
    }

    private string? WriteSnapshotContent(
        AutoSaveEntry entry,
        string contentPath)
    {
        if (entry.Mode == FileOpenMode.Binary)
        {
            if (entry.WriteContent == null)
                throw new InvalidDataException("Binary auto-save content is unavailable.");
            _fileSystem.WriteStreamAtomic(contentPath, entry.WriteContent);
            return BinaryContentFormat;
        }

        if (string.Equals(
                entry.ContentFormat,
                LosslessTextSnapshotCodec.Format,
                StringComparison.Ordinal))
        {
            _fileSystem.WriteStreamAtomic(
                contentPath,
                stream => LosslessTextSnapshotCodec.Write(stream, entry.Content));
            return LosslessTextSnapshotCodec.Format;
        }
        if (!string.IsNullOrEmpty(entry.ContentFormat))
            throw new InvalidDataException("The auto-save content format is unsupported.");

        _fileSystem.WriteAllTextAtomic(contentPath, entry.Content);
        return null;
    }

    private (string Hash, long Length) ComputeContentFingerprint(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        return (
            Convert.ToHexString(SHA256.HashData(stream)),
            stream.Length);
    }

    private void VerifySnapshot(
        IReadOnlyCollection<AutoSaveEntry> snapshot,
        IReadOnlyCollection<AutoSaveManifestEntry> expectedManifest,
        string manifestPath)
    {
        var storedManifest = JsonSerializer.Deserialize<List<AutoSaveManifestEntry>>(
            _fileSystem.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("The replacement recovery manifest contains no entries.");
        if (storedManifest.Count != snapshot.Count)
            throw new InvalidDataException("The replacement recovery manifest is incomplete.");

        var storedById = storedManifest.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var expectedById = expectedManifest.ToDictionary(
            entry => entry.Id,
            StringComparer.Ordinal);
        foreach (var entry in snapshot)
        {
            if (!storedById.TryGetValue(entry.Id, out var stored) ||
                !expectedById.TryGetValue(entry.Id, out var expected) ||
                !string.Equals(
                    stored.TabIdentity,
                    entry.TabIdentity,
                    StringComparison.Ordinal) ||
                stored.FileName != entry.FileName ||
                stored.FilePath != entry.FilePath ||
                !TryResolveContentPath(
                    _activeManifestPath,
                    stored.ContentFile,
                    out var storedContentPath) ||
                !string.Equals(
                    stored.ContentFile,
                    expected.ContentFile,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stored.ContentHash,
                    expected.ContentHash,
                    StringComparison.Ordinal) ||
                stored.ContentLength != expected.ContentLength ||
                stored.IsUntitled != entry.IsUntitled ||
                stored.CursorOffset != entry.CursorOffset ||
                stored.ScrollOffset != entry.ScrollOffset ||
                stored.Mode != entry.Mode ||
                stored.IsModified != entry.IsModified ||
                stored.EncodingCodePage != entry.EncodingCodePage ||
                stored.HasBom != entry.HasBom ||
                stored.HexOffset != entry.HexOffset ||
                stored.BytesPerRow != entry.BytesPerRow ||
                stored.LargeFileTopLine != entry.LargeFileTopLine ||
                !string.Equals(
                    stored.ContentFormat,
                    GetExpectedContentFormat(entry),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The replacement recovery metadata for '{entry.FileName}' could not be verified.");
            }

            if (!string.IsNullOrEmpty(stored.ContentFormat))
            {
                var (contentHash, contentLength) =
                    ComputeContentFingerprint(storedContentPath);
                if (!string.Equals(
                        stored.ContentHash,
                        contentHash,
                        StringComparison.Ordinal) ||
                    stored.ContentLength != contentLength)
                {
                    throw new InvalidDataException(
                        $"The replacement recovery payload for '{entry.FileName}' could not be verified.");
                }
            }

            if (entry.Mode != FileOpenMode.Binary)
            {
                var storedContent = string.Equals(
                    stored.ContentFormat,
                    LosslessTextSnapshotCodec.Format,
                    StringComparison.Ordinal)
                    ? LosslessTextSnapshotCodec
                        .ReadAsync(
                            _fileSystem,
                            storedContentPath,
                            stored.ContentFormat)
                        .GetAwaiter()
                        .GetResult()
                    : _fileSystem.ReadAllText(storedContentPath);
                if (!string.Equals(
                        storedContent,
                        entry.Content,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"The replacement recovery content for '{entry.FileName}' could not be verified.");
                }
            }
        }
    }

    private static string? GetExpectedContentFormat(AutoSaveEntry entry) =>
        entry.Mode == FileOpenMode.Binary
            ? BinaryContentFormat
            : entry.ContentFormat;

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
        if (manifestPath.EndsWith(
                ".writing",
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Recovery manifest '{Path.GetFileName(manifestPath)}' was not fully verified and was preserved.");
            return;
        }

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

                if (!TryResolveContentPath(
                        manifestPath,
                        item.ContentFile,
                        out var contentPath,
                        item.Id))
                {
                    errors.Add(
                        $"Recovery content path for '{item.FileName}' is invalid.");
                    continue;
                }

                _lastRecoveryContentPaths.Add(contentPath);
                if (!_fileSystem.FileExists(contentPath))
                {
                    errors.Add($"Recovery content for '{item.FileName}' is missing.");
                    continue;
                }

                try
                {
                    var recoveryEntryId = $"{manifestName}:{item.Id}";
                    if (!string.IsNullOrEmpty(item.ContentFormat))
                    {
                        var (contentHash, contentLength) =
                            ComputeContentFingerprint(contentPath);
                        if (!string.Equals(
                                item.ContentHash,
                                contentHash,
                                StringComparison.Ordinal) ||
                            item.ContentLength != contentLength)
                        {
                            throw new InvalidDataException(
                                "Recovery content fingerprint does not match its manifest.");
                        }
                    }
                    var content = item.Mode == FileOpenMode.Binary
                        ? string.Empty
                        : string.IsNullOrEmpty(item.ContentFormat)
                            ? _fileSystem.ReadAllText(contentPath)
                            : LosslessTextSnapshotCodec
                                .ReadAsync(
                                    _fileSystem,
                                    contentPath,
                                    item.ContentFormat)
                                .GetAwaiter()
                                .GetResult();
                    entriesById[recoveryEntryId] = new AutoSaveEntry(
                        recoveryEntryId,
                        item.FileName,
                        item.FilePath,
                        content,
                        item.IsUntitled,
                        item.CursorOffset,
                        item.ScrollOffset,
                        item.TabIdentity,
                        item.Mode,
                        item.IsModified,
                        item.EncodingCodePage,
                        item.HasBom,
                        item.HexOffset,
                        item.BytesPerRow,
                        item.LargeFileTopLine,
                        item.ContentFormat,
                        item.ContentHash,
                        item.ContentLength,
                        contentPath);
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
        try
        {
            var snapshot = replacementEntries.ToArray();
            if (snapshot.Length > 0)
                SaveSnapshot(snapshot, requireRunning: false, verifyWrites: true);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to persist replacement recovery snapshot: {0}", ex);
            return false;
        }

        lock (_persistenceSync)
        {
            try
            {
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
                if (!TryGetGenerationContentPaths(
                        manifestPath,
                        out var contentPaths))
                {
                    allRetired = false;
                    continue;
                }
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
            if (_pendingAdoptionContentPaths.Count > 0)
                return false;

            if (_fileSystem.FileExists(_activeManifestPath))
            {
                if (!TryGetGenerationContentPaths(
                        _activeManifestPath,
                        out var contentPaths))
                {
                    return false;
                }
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

    private bool TryGetGenerationContentPaths(
        string manifestPath,
        out string[] contentPaths)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryCaptureRecoveryContentPaths(
                new[] { manifestPath },
                paths))
        {
            contentPaths = paths.ToArray();
            return false;
        }

        if (!AddGenerationContentPaths(
                Path.GetFileName(manifestPath),
                paths))
        {
            contentPaths = paths.ToArray();
            return false;
        }
        contentPaths = paths.ToArray();
        return true;
    }

    private bool AddGenerationContentPaths(
        string manifestName,
        ISet<string> contentPaths)
    {
        var fileName = Path.GetFileNameWithoutExtension(manifestName);
        const string prefix = "manifest-";
        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var generationId = fileName[prefix.Length..];
            foreach (var path in _fileSystem.GetFiles(_autoSaveDir, $"{generationId}-*.txt"))
            {
                if (!TryResolveContentPath(
                        manifestName,
                        Path.GetFileName(path),
                        out var contentPath))
                {
                    return false;
                }

                contentPaths.Add(contentPath);
            }
        }

        return true;
    }

    private void CleanupRetiredGenerations()
    {
        foreach (var retiredManifestPath in _fileSystem
            .GetFiles(_autoSaveDir, ".retired-*.json")
            .Concat(_fileSystem.GetFiles(
                _autoSaveDir,
                ".retired-*.candidate")))
        {
            var cleanupComplete = true;
            var originalManifestName = GetOriginalManifestName(retiredManifestPath);
            var contentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryCaptureRecoveryContentPaths(
                    new[] { retiredManifestPath },
                    contentPaths))
            {
                continue;
            }
            if (originalManifestName != null)
            {
                if (!AddGenerationContentPaths(
                        originalManifestName,
                        contentPaths))
                {
                    continue;
                }
            }

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
        if (contentPaths.Any(_pendingAdoptionContentPaths.Contains))
            return false;

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
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (contentFiles.Length != contentPaths.Count ||
                contentFiles.Any(fileName =>
                    !TryResolveOwnedGenerationContentPath(
                        generationId,
                        fileName,
                        out var resolvedPath) ||
                    !contentPaths.Contains(
                        resolvedPath,
                        StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Cleanup content does not match its auto-save generation.");
            }

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
                intent.ContentFiles.Length !=
                    intent.ContentFiles
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count())
            {
                throw new InvalidDataException("The auto-save cleanup intent is invalid.");
            }

            generationId = intent.GenerationId;
            var resolvedContentPaths = new List<string>(intent.ContentFiles.Length);
            foreach (var fileName in intent.ContentFiles)
            {
                if (!TryResolveOwnedGenerationContentPath(
                        generationId,
                        fileName,
                        out var resolvedPath))
                {
                    throw new InvalidDataException(
                        "The auto-save cleanup content path is invalid.");
                }
                resolvedContentPaths.Add(resolvedPath);
            }
            contentPaths = resolvedContentPaths.ToArray();
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
            .Concat(_fileSystem.GetFiles(
                _autoSaveDir,
                ".retired-*.candidate"))
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
            var candidateManifestPath = Path.Combine(
                _autoSaveDir,
                $"manifest-{generationId}.candidate");
            var writingManifestPath = Path.Combine(
                _autoSaveDir,
                $"manifest-{generationId}.writing");
            if (_fileSystem.FileExists(manifestPath) ||
                _fileSystem.FileExists(candidateManifestPath) ||
                _fileSystem.FileExists(writingManifestPath) ||
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

            var legacyContentPaths = contentPaths
                .Where(path => !IsSnapshotPayload(path, generationId))
                .ToArray();
            if (legacyContentPaths.Length > 0)
                TryWriteSyntheticManifest(manifestPath, legacyContentPaths);
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
            var manifestStem = Path.GetFileNameWithoutExtension(manifestPath);
            const string manifestPrefix = "manifest-";
            var generationId = manifestStem.StartsWith(
                manifestPrefix,
                StringComparison.OrdinalIgnoreCase)
                ? manifestStem[manifestPrefix.Length..]
                : string.Empty;
            var manifest = contentPaths.Select(contentPath => new AutoSaveManifestEntry
            {
                Id = GetSyntheticEntryId(contentPath, generationId),
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

    private static string GetSyntheticEntryId(
        string contentPath,
        string generationId)
    {
        var contentStem = Path.GetFileNameWithoutExtension(contentPath);
        var generationPrefix = $"{generationId}-";
        return !string.IsNullOrEmpty(generationId) &&
               contentStem.StartsWith(
                   generationPrefix,
                   StringComparison.OrdinalIgnoreCase)
            ? contentStem[generationPrefix.Length..]
            : contentStem;
    }

    private static bool IsSnapshotPayload(
        string contentPath,
        string generationId)
    {
        var contentStem = Path.GetFileNameWithoutExtension(contentPath);
        var generationPrefix = $"{generationId}-";
        if (!contentStem.StartsWith(
                generationPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = contentStem[generationPrefix.Length..];
        var separatorIndex = remainder.IndexOf('-');
        return separatorIndex == 32 &&
            Guid.TryParseExact(
                remainder[..separatorIndex],
                "N",
                out _);
    }

    private string GetCleanupIntentPath(string generationId) =>
        Path.Combine(_autoSaveDir, $".cleanup-{generationId}.json");

    private IReadOnlyList<string> GetManifestPaths()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir))
            return Array.Empty<string>();

        return _fileSystem.GetFiles(_autoSaveDir, "manifest*.json")
            .Concat(_fileSystem.GetFiles(
                _autoSaveDir,
                "manifest*.candidate"))
            .Concat(_fileSystem.GetFiles(
                _autoSaveDir,
                "manifest*.writing"))
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

    private bool TryCaptureRecoveryContentPaths(
        IEnumerable<string> manifestPaths,
        ISet<string> contentPaths)
    {
        var allPathsValid = true;
        foreach (var manifestPath in manifestPaths)
        {
            try
            {
                var json = _fileSystem.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<List<AutoSaveManifestEntry>>(json);
                if (manifest == null)
                    continue;

                foreach (var item in manifest)
                {
                    if (TryResolveContentPath(
                            manifestPath,
                            item.ContentFile,
                            out var contentPath,
                            item.Id))
                    {
                        contentPaths.Add(contentPath);
                    }
                    else
                    {
                        allPathsValid = false;
                    }
                }
            }
            catch (Exception ex)
            {
                allPathsValid = false;
                Trace.TraceWarning(
                    "Failed to enumerate content for recovery manifest '{0}': {1}",
                    manifestPath,
                    ex);
            }
        }
        return allPathsValid;
    }

    private bool TryResolveContentPath(
        string manifestPath,
        string contentFile,
        out string contentPath,
        string? legacyEntryId = null)
    {
        contentPath = string.Empty;
        if (!TryResolveSafeAutoSaveLeaf(contentFile, out var candidatePath))
            return false;

        var manifestName = GetOriginalManifestName(manifestPath) ??
            Path.GetFileName(manifestPath);
        var manifestStem = Path.GetFileNameWithoutExtension(manifestName);
        const string manifestPrefix = "manifest-";
        if (manifestStem.Equals("manifest", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(legacyEntryId) ||
                !string.Equals(
                    contentFile,
                    $"{legacyEntryId}.txt",
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (manifestStem.StartsWith(
                manifestPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            var generationId = manifestStem[manifestPrefix.Length..];
            var matchesLegacySingleEntry = string.Equals(
                contentFile,
                $"{generationId}.txt",
                StringComparison.OrdinalIgnoreCase);
            var matchesOwnedGenerationLeaf =
                contentFile.StartsWith(
                   $"{generationId}-",
                   StringComparison.OrdinalIgnoreCase) &&
                contentFile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(generationId) ||
                (!matchesLegacySingleEntry &&
                !matchesOwnedGenerationLeaf))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        contentPath = candidatePath;
        return true;
    }

    private bool TryResolveOwnedGenerationContentPath(
        string generationId,
        string contentFile,
        out string contentPath)
    {
        contentPath = string.Empty;
        if (!contentFile.StartsWith(
                $"{generationId}-",
                StringComparison.OrdinalIgnoreCase) ||
            !contentFile.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryResolveSafeAutoSaveLeaf(contentFile, out contentPath);
    }

    private bool TryResolveSafeAutoSaveLeaf(
        string contentFile,
        out string contentPath)
    {
        contentPath = string.Empty;
        if (string.IsNullOrWhiteSpace(contentFile) ||
            Path.IsPathRooted(contentFile) ||
            !string.Equals(
                Path.GetFileName(contentFile),
                contentFile,
                StringComparison.Ordinal) ||
            contentFile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var rootPath = Path.GetFullPath(_autoSaveDir);
        var candidatePath = Path.GetFullPath(
            Path.Combine(rootPath, contentFile));
        var relativePath = Path.GetRelativePath(rootPath, candidatePath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (_fileSystem.DirectoryExists(rootPath) &&
            (_fileSystem.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        if (_fileSystem.FileExists(candidatePath) &&
            (_fileSystem.GetAttributes(candidatePath) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        contentPath = candidatePath;
        return true;
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
        public FileOpenMode Mode { get; set; } = FileOpenMode.Text;
        public bool IsModified { get; set; } = true;
        public int EncodingCodePage { get; set; } = System.Text.Encoding.UTF8.CodePage;
        public bool HasBom { get; set; }
        public long HexOffset { get; set; }
        public int BytesPerRow { get; set; } = 16;
        public long LargeFileTopLine { get; set; } = 1;
        public string? ContentFormat { get; set; }
        public string? ContentHash { get; set; }
        public long ContentLength { get; set; } = -1;
    }

    private sealed record RecoveryOrigin(string ManifestName, string EntryId);
    private sealed record CleanupIntent(string GenerationId, string[] ContentFiles);
}
