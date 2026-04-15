using System.IO;
using FastEdit.Services.Interfaces;

namespace FastEdit.Services;

/// <summary>
/// Watches a file for changes and notifies when content is appended (log tailing).
/// </summary>
public class FileWatcherService : IFileWatcherService
{
    private FileSystemWatcher? _watcher;
    private string? _watchedPath;
    private bool _disposed;

    public event EventHandler<string>? FileChanged;

    public bool IsWatching => _watcher != null;
    public string? WatchedPath => _watchedPath;

    public void StartWatching(string filePath)
    {
        StopWatching();

        if (!File.Exists(filePath)) return;

        _watchedPath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);

        if (string.IsNullOrEmpty(dir)) return;

        _watcher = new FileSystemWatcher(dir, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.Changed -= OnFileChanged;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _watchedPath = null;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        FileChanged?.Invoke(this, e.FullPath);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        GC.SuppressFinalize(this);
    }
}
