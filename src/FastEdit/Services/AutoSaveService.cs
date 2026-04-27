using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

public class AutoSaveService : IAutoSaveService
{
    private readonly IFileSystemService _fileSystem;
    private readonly string _autoSaveDir;
    private readonly string _shutdownMarkerPath;
    private System.Timers.Timer? _timer;
    private Func<IEnumerable<AutoSaveEntry>>? _entryProvider;

    public bool IsEnabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;

    public AutoSaveService(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
        _autoSaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "AutoSave");
        _shutdownMarkerPath = Path.Combine(_autoSaveDir, ".clean-shutdown");
    }

    public void Start()
    {
        _fileSystem.CreateDirectory(_autoSaveDir);
        // Remove clean-shutdown marker when starting (indicates session is active)
        if (_fileSystem.FileExists(_shutdownMarkerPath))
            _fileSystem.DeleteFile(_shutdownMarkerPath);

        _timer = new System.Timers.Timer(IntervalSeconds * 1000);
        _timer.Elapsed += (s, e) => OnTimerElapsed();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    public void SetEntryProvider(Func<IEnumerable<AutoSaveEntry>> provider)
    {
        _entryProvider = provider;
    }

    private void OnTimerElapsed()
    {
        if (!IsEnabled || _entryProvider == null) return;
        try
        {
            var entries = _entryProvider().ToList();
            if (entries.Count > 0)
                SaveNow(entries);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Auto-save timer failed: {0}", ex.Message);
        }
    }

    public void SaveNow(IEnumerable<AutoSaveEntry> entries)
    {
        _fileSystem.CreateDirectory(_autoSaveDir);

        var manifest = new List<AutoSaveManifestEntry>();

        foreach (var entry in entries)
        {
            var contentPath = Path.Combine(_autoSaveDir, $"{entry.Id}.txt");
            _fileSystem.WriteAllText(contentPath, entry.Content);

            manifest.Add(new AutoSaveManifestEntry
            {
                Id = entry.Id,
                FileName = entry.FileName,
                FilePath = entry.FilePath,
                ContentFile = $"{entry.Id}.txt",
                IsUntitled = entry.IsUntitled,
                CursorOffset = entry.CursorOffset,
                ScrollOffset = entry.ScrollOffset
            });
        }

        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        _fileSystem.WriteAllText(manifestPath, json);
    }

    public void MarkCleanShutdown()
    {
        _fileSystem.CreateDirectory(_autoSaveDir);
        _fileSystem.WriteAllText(_shutdownMarkerPath, DateTime.UtcNow.ToString("O"));
        ClearRecoveryFiles();
    }

    public bool HasRecoveryFiles()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir)) return false;
        // If clean shutdown marker exists, no recovery needed
        if (_fileSystem.FileExists(_shutdownMarkerPath)) return false;

        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        return _fileSystem.FileExists(manifestPath);
    }

    public List<AutoSaveEntry> GetRecoveryEntries()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        if (!_fileSystem.FileExists(manifestPath)) return new();

        try
        {
            var json = _fileSystem.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<List<AutoSaveManifestEntry>>(json) ?? new();

            var entries = new List<AutoSaveEntry>();
            foreach (var m in manifest)
            {
                var contentPath = Path.Combine(_autoSaveDir, m.ContentFile);
                if (!_fileSystem.FileExists(contentPath)) continue;

                var content = _fileSystem.ReadAllText(contentPath);
                entries.Add(new AutoSaveEntry(m.Id, m.FileName, m.FilePath, content, m.IsUntitled, m.CursorOffset, m.ScrollOffset));
            }
            return entries;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to read auto-save recovery manifest: {0}", ex.Message);
            return new();
        }
    }

    public void ClearRecoveryFiles()
    {
        if (!_fileSystem.DirectoryExists(_autoSaveDir)) return;

        try
        {
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
            var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
            if (_fileSystem.FileExists(manifestPath))
                _fileSystem.DeleteFile(manifestPath);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to clear auto-save recovery files: {0}", ex.Message);
        }
    }

    private class AutoSaveManifestEntry
    {
        public string Id { get; set; } = "";
        public string FileName { get; set; } = "";
        public string? FilePath { get; set; }
        public string ContentFile { get; set; } = "";
        public bool IsUntitled { get; set; }
        public int CursorOffset { get; set; }
        public double ScrollOffset { get; set; }
    }
}
