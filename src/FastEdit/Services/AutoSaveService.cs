using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public sealed class AutoSaveService : IAutoSaveService, IDisposable
{
    private readonly IFileSystemService _fileSystem;
    private readonly IDispatcherService _dispatcher;
    private readonly string _autoSaveDir;
    private readonly string _manifestPath;
    private readonly string _shutdownMarkerPath;
    private readonly object _stateLock = new();
    private readonly object _persistenceLock = new();
    private readonly HashSet<string> _pendingRecoveryIds = new(StringComparer.Ordinal);
    private DispatcherTimer? _timer;
    private Task? _activeSaveTask;
    private Func<IEnumerable<AutoSaveEntry>>? _entryProvider;
    private int _saveInProgress;
    private bool _preserveRecoveryManifest;
    private bool _disposed;

    public bool IsEnabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;

    public AutoSaveService(IFileSystemService fileSystem, IDispatcherService dispatcher)
    {
        _fileSystem = fileSystem;
        _dispatcher = dispatcher;
        _autoSaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "AutoSave");
        _manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        _shutdownMarkerPath = Path.Combine(_autoSaveDir, ".clean-shutdown");
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _dispatcher.Invoke(() =>
        {
            if (_timer != null)
                return;

            _fileSystem.CreateDirectory(_autoSaveDir);
            if (_fileSystem.FileExists(_shutdownMarkerPath))
                _fileSystem.DeleteFile(_shutdownMarkerPath);

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(IntervalSeconds)
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        });
    }

    public void Stop()
    {
        _dispatcher.Invoke(() =>
        {
            if (_timer == null)
                return;

            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        });

        Task? activeSave;
        lock (_stateLock)
            activeSave = _activeSaveTask;
        activeSave?.GetAwaiter().GetResult();
    }

    public void SetEntryProvider(Func<IEnumerable<AutoSaveEntry>> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _entryProvider = provider;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var saveTask = RunAutoSaveCycleAsync();
        lock (_stateLock)
            _activeSaveTask = saveTask;

        _ = saveTask.ContinueWith(
            completed =>
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_activeSaveTask, completed))
                        _activeSaveTask = null;
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    internal async Task RunAutoSaveCycleAsync()
    {
        if (!IsEnabled || _entryProvider == null || _disposed)
            return;
        if (Interlocked.CompareExchange(ref _saveInProgress, 1, 0) != 0)
            return;

        try
        {
            var entries = await _dispatcher.InvokeAsync(() => _entryProvider().ToList());
            if (entries.Count > 0)
                await Task.Run(() => SaveNow(entries));
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Auto-save timer failed: {0}", ex.Message);
        }
        finally
        {
            Volatile.Write(ref _saveInProgress, 0);
        }
    }

    public void SaveNow(IEnumerable<AutoSaveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_persistenceLock)
        {
            if (_preserveRecoveryManifest)
                throw new InvalidOperationException("The existing recovery manifest is unreadable and has been preserved.");

            _fileSystem.CreateDirectory(_autoSaveDir);
            var previousManifest = ReadManifestIfPresent();
            HashSet<string> pendingIds;
            lock (_stateLock)
                pendingIds = new HashSet<string>(_pendingRecoveryIds, StringComparer.Ordinal);

            var manifest = previousManifest
                .Where(entry => pendingIds.Contains(entry.Id))
                .ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            var supersededPendingIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in entries)
            {
                var contentFile = $"{entry.Id}.txt";
                AtomicFileWriter.WriteAllText(
                    _fileSystem,
                    Path.Combine(_autoSaveDir, contentFile),
                    entry.Content);
                manifest[entry.Id] = AutoSaveManifestEntry.From(entry, contentFile);
                if (pendingIds.Contains(entry.Id))
                    supersededPendingIds.Add(entry.Id);
            }

            WriteManifest(manifest.Values);
            DeleteUnreferencedContent(previousManifest, manifest.Values);
            lock (_stateLock)
            {
                foreach (var id in supersededPendingIds)
                    _pendingRecoveryIds.Remove(id);
            }
        }
    }

    public void MarkCleanShutdown()
    {
        Stop();

        lock (_persistenceLock)
        {
            if (_preserveRecoveryManifest)
            {
                DeleteShutdownMarker();
                return;
            }

            HashSet<string> pendingIds;
            lock (_stateLock)
                pendingIds = new HashSet<string>(_pendingRecoveryIds, StringComparer.Ordinal);

            if (pendingIds.Count > 0)
            {
                var previousManifest = ReadManifestIfPresent();
                var retained = previousManifest.Where(entry => pendingIds.Contains(entry.Id)).ToList();
                WriteManifest(retained);
                DeleteUnreferencedContent(previousManifest, retained);
                DeleteShutdownMarker();
                return;
            }

            ClearRecoveryFiles();
            AtomicFileWriter.WriteAllText(_fileSystem, _shutdownMarkerPath, DateTime.UtcNow.ToString("O"));
        }
    }

    public bool HasRecoveryFiles()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir))
            return false;
        if (_fileSystem.FileExists(_shutdownMarkerPath))
            return false;
        return _fileSystem.FileExists(_manifestPath);
    }

    public RecoveryReadResult GetRecoveryEntries()
    {
        lock (_persistenceLock)
        {
            if (!_fileSystem.FileExists(_manifestPath))
                return new RecoveryReadResult(Array.Empty<AutoSaveEntry>(), Array.Empty<string>());

            List<AutoSaveManifestEntry> manifest;
            try
            {
                manifest = ReadManifest();
            }
            catch (Exception ex)
            {
                _preserveRecoveryManifest = true;
                Trace.TraceWarning("Failed to read auto-save recovery manifest: {0}", ex.Message);
                return new RecoveryReadResult(
                    Array.Empty<AutoSaveEntry>(),
                    new[] { $"Recovery manifest could not be read: {ex.Message}" });
            }

            lock (_stateLock)
            {
                foreach (var entry in manifest)
                    _pendingRecoveryIds.Add(entry.Id);
            }

            var entries = new List<AutoSaveEntry>();
            var failures = new List<string>();
            foreach (var item in manifest)
            {
                var contentPath = Path.Combine(_autoSaveDir, item.ContentFile);
                try
                {
                    if (!_fileSystem.FileExists(contentPath))
                    {
                        failures.Add($"{item.FileName}: recovery content is missing.");
                        continue;
                    }

                    var content = _fileSystem.ReadAllText(contentPath);
                    entries.Add(item.ToEntry(content));
                }
                catch (Exception ex)
                {
                    failures.Add($"{item.FileName}: {ex.Message}");
                }
            }

            return new RecoveryReadResult(entries, failures);
        }
    }

    public void RemoveRecoveryEntries(IEnumerable<string> entryIds)
    {
        var ids = entryIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0)
            return;

        lock (_persistenceLock)
        {
            var previousManifest = ReadManifestIfPresent();
            var retained = previousManifest.Where(entry => !ids.Contains(entry.Id)).ToList();
            WriteManifest(retained);

            foreach (var entry in previousManifest.Where(entry => ids.Contains(entry.Id)))
                DeleteRecoveryContent(entry);

            lock (_stateLock)
            {
                foreach (var id in ids)
                    _pendingRecoveryIds.Remove(id);
            }
        }
    }

    public void ClearRecoveryFiles()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir))
            return;

        foreach (var file in _fileSystem.GetFiles(_autoSaveDir, "*.txt"))
        {
            try
            {
                _fileSystem.DeleteFile(file);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to delete auto-save recovery file '{0}': {1}", file, ex.Message);
            }
        }

        if (_fileSystem.FileExists(_manifestPath))
            _fileSystem.DeleteFile(_manifestPath);
    }

    private List<AutoSaveManifestEntry> ReadManifestIfPresent() =>
        _fileSystem.FileExists(_manifestPath) ? ReadManifest() : new List<AutoSaveManifestEntry>();

    private List<AutoSaveManifestEntry> ReadManifest()
    {
        var json = _fileSystem.ReadAllText(_manifestPath);
        return JsonSerializer.Deserialize<List<AutoSaveManifestEntry>>(json)
            ?? throw new InvalidDataException("Recovery manifest contained no entries.");
    }

    private void WriteManifest(IEnumerable<AutoSaveManifestEntry> entries)
    {
        var manifest = entries.ToList();
        if (manifest.Count == 0)
        {
            if (_fileSystem.FileExists(_manifestPath))
                _fileSystem.DeleteFile(_manifestPath);
            return;
        }

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        AtomicFileWriter.WriteAllText(_fileSystem, _manifestPath, json);
    }

    private void DeleteUnreferencedContent(
        IEnumerable<AutoSaveManifestEntry> previousEntries,
        IEnumerable<AutoSaveManifestEntry> currentEntries)
    {
        var retainedFiles = currentEntries
            .Select(entry => entry.ContentFile)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in previousEntries.Where(entry => !retainedFiles.Contains(entry.ContentFile)))
            DeleteRecoveryContent(entry);
    }

    private void DeleteRecoveryContent(AutoSaveManifestEntry entry)
    {
        var path = Path.Combine(_autoSaveDir, entry.ContentFile);
        if (!_fileSystem.FileExists(path))
            return;

        try
        {
            _fileSystem.DeleteFile(path);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to delete auto-save recovery file '{0}': {1}", path, ex.Message);
        }
    }

    private void DeleteShutdownMarker()
    {
        if (_fileSystem.FileExists(_shutdownMarkerPath))
            _fileSystem.DeleteFile(_shutdownMarkerPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
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

        public static AutoSaveManifestEntry From(AutoSaveEntry entry, string contentFile) => new()
        {
            Id = entry.Id,
            FileName = entry.FileName,
            FilePath = entry.FilePath,
            ContentFile = contentFile,
            IsUntitled = entry.IsUntitled,
            CursorOffset = entry.CursorOffset,
            ScrollOffset = entry.ScrollOffset
        };

        public AutoSaveEntry ToEntry(string content) =>
            new(Id, FileName, FilePath, content, IsUntitled, CursorOffset, ScrollOffset);
    }
}
