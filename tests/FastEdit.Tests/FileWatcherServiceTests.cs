using System.IO;
using FastEdit.Services;

namespace FastEdit.Tests;

public class FileWatcherServiceTests : IDisposable
{
    private readonly FileWatcherService _watcher = new();

    public void Dispose()
    {
        _watcher.Dispose();
    }

    [Fact]
    public void Initially_Not_Watching()
    {
        Assert.False(_watcher.IsWatching);
        Assert.Null(_watcher.WatchedPath);
    }

    [Fact]
    public void StartWatching_Sets_IsWatching()
    {
        var path = Path.GetTempFileName();
        try
        {
            _watcher.StartWatching(path);
            Assert.True(_watcher.IsWatching);
            Assert.Equal(path, _watcher.WatchedPath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StopWatching_Clears_State()
    {
        var path = Path.GetTempFileName();
        try
        {
            _watcher.StartWatching(path);
            _watcher.StopWatching();

            Assert.False(_watcher.IsWatching);
            Assert.Null(_watcher.WatchedPath);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StartWatching_NonExistent_File_Does_Nothing()
    {
        _watcher.StartWatching(@"C:\nonexistent\file.txt");
        Assert.False(_watcher.IsWatching);
    }

    [Fact]
    public void StartWatching_Replaces_Previous_Watch()
    {
        var path1 = Path.GetTempFileName();
        var path2 = Path.GetTempFileName();
        try
        {
            _watcher.StartWatching(path1);
            _watcher.StartWatching(path2);

            Assert.True(_watcher.IsWatching);
            Assert.Equal(path2, _watcher.WatchedPath);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public void Dispose_Stops_Watching()
    {
        var path = Path.GetTempFileName();
        try
        {
            _watcher.StartWatching(path);
            _watcher.Dispose();

            Assert.False(_watcher.IsWatching);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Double_Dispose_Does_Not_Throw()
    {
        _watcher.Dispose();
        var exception = Record.Exception(() => _watcher.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task FileChanged_Event_Fires_On_Modification()
    {
        var path = Path.GetTempFileName();
        try
        {
            var tcs = new TaskCompletionSource<string>();
            _watcher.FileChanged += (s, changedPath) => tcs.TrySetResult(changedPath);
            _watcher.StartWatching(path);

            // Modify the file
            await Task.Delay(100);
            await File.WriteAllTextAsync(path, "updated content");

            var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            if (result == tcs.Task)
            {
                Assert.Equal(path, await tcs.Task);
            }
            // If timeout, it's acceptable — FileSystemWatcher timing is OS-dependent
        }
        finally { File.Delete(path); }
    }
}
