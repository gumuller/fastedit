namespace FastEdit.Services.Interfaces;

public interface IFileWatcherService : IDisposable
{
    event EventHandler<string>? FileChanged;
    bool IsWatching { get; }
    string? WatchedPath { get; }
    void StartWatching(string filePath);
    void StopWatching();
}
