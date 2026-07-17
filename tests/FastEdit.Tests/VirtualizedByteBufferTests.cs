using System.IO;
using FastEdit.Core.HexEngine;

namespace FastEdit.Tests;

public class VirtualizedByteBufferTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    private string CreateTempFile(byte[] content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void Length_Returns_File_Size()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        Assert.Equal(5, buffer.Length);
    }

    [Fact]
    public void GetByte_Returns_Correct_Values()
    {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        Assert.Equal(0xDE, buffer.GetByte(0));
        Assert.Equal(0xAD, buffer.GetByte(1));
        Assert.Equal(0xBE, buffer.GetByte(2));
        Assert.Equal(0xEF, buffer.GetByte(3));
    }

    [Fact]
    public void SetByte_Modifies_Value()
    {
        var data = new byte[] { 0x00, 0x00, 0x00 };
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        buffer.SetByte(1, 0xFF);

        Assert.Equal(0xFF, buffer.GetByte(1));
        Assert.True(buffer.IsModified(1));
        Assert.False(buffer.IsModified(0));
    }

    [Fact]
    public void HasModifications_Tracks_Changes()
    {
        var path = CreateTempFile(new byte[] { 0x00 });

        using var buffer = new VirtualizedByteBuffer(path);
        Assert.False(buffer.HasModifications);

        buffer.SetByte(0, 0xFF);
        Assert.True(buffer.HasModifications);
    }

    [Fact]
    public void DiscardModifications_Reverts_Changes()
    {
        var data = new byte[] { 0xAA, 0xBB };
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        buffer.SetByte(0, 0xFF);
        buffer.DiscardModifications();

        Assert.False(buffer.HasModifications);
        Assert.Equal(0xAA, buffer.GetByte(0));
    }

    [Fact]
    public void Save_Persists_Changes_To_File()
    {
        var data = new byte[] { 0x01, 0x02, 0x03 };
        var path = CreateTempFile(data);

        using (var buffer = new VirtualizedByteBuffer(path))
        {
            buffer.SetByte(1, 0xFF);
            buffer.Save();
        }

        var saved = File.ReadAllBytes(path);
        Assert.Equal(0xFF, saved[1]);
        Assert.Equal(0x01, saved[0]);
        Assert.Equal(0x03, saved[2]);
    }

    [Fact]
    public void GetRows_Returns_Formatted_Rows()
    {
        var data = Enumerable.Range(0, 48).Select(i => (byte)i).ToArray();
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        var rows = buffer.GetRows(0, 3, 16).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(0, rows[0].Offset);
        Assert.Equal(16, rows[1].Offset);
        Assert.Equal(32, rows[2].Offset);
    }

    [Fact]
    public void GetBytes_Returns_Correct_Span()
    {
        var data = new byte[] { 10, 20, 30, 40, 50 };
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        var span = buffer.GetBytes(1, 3);

        Assert.Equal(3, span.Length);
        Assert.Equal(20, span[0]);
        Assert.Equal(30, span[1]);
        Assert.Equal(40, span[2]);
    }

    [Fact]
    public void ModificationsChanged_Event_Fires()
    {
        var path = CreateTempFile(new byte[] { 0x00 });
        using var buffer = new VirtualizedByteBuffer(path);

        bool fired = false;
        buffer.ModificationsChanged += (s, e) => fired = true;
        buffer.SetByte(0, 0xFF);

        Assert.True(fired);
    }

    [Fact]
    public void Works_With_Large_File()
    {
        // Create a 256KB file to exercise paged reading
        var data = new byte[256 * 1024];
        new Random(42).NextBytes(data);
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        Assert.Equal(data.Length, buffer.Length);

        // Read from different pages
        Assert.Equal(data[0], buffer.GetByte(0));
        Assert.Equal(data[100_000], buffer.GetByte(100_000));
        Assert.Equal(data[255_000], buffer.GetByte(255_000));
    }

    [Fact]
    public async Task SearchAsync_Finds_Match_Across_Page_Boundary()
    {
        var data = new byte[128 * 1024];
        var pattern = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        pattern.CopyTo(data, (64 * 1024) - 2);
        var path = CreateTempFile(data);

        using var buffer = new VirtualizedByteBuffer(path);
        var result = await buffer.SearchAsync(
            pattern,
            VirtualizedByteBuffer.DefaultSearchResultLimit,
            progress: null,
            CancellationToken.None);

        Assert.Equal(new long[] { (64 * 1024) - 2 }, result.Results);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task SearchAsync_Enforces_Exact_Result_Limit()
    {
        var path = CreateTempFile(Enumerable.Repeat((byte)0xAA, 100).ToArray());
        using var buffer = new VirtualizedByteBuffer(path);

        var result = await buffer.SearchAsync(
            new byte[] { 0xAA },
            maxResults: 10,
            progress: null,
            CancellationToken.None);

        Assert.Equal(10, result.Results.Count);
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task SearchAsync_Observes_Cancellation()
    {
        var path = CreateTempFile(new byte[128 * 1024]);
        using var buffer = new VirtualizedByteBuffer(path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => buffer.SearchAsync(
                new byte[] { 0x00 },
                VirtualizedByteBuffer.DefaultSearchResultLimit,
                progress: null,
                cts.Token));
    }

    [Fact]
    public async Task Save_Waits_For_Active_Search_Before_Requesting_Exclusive_Access()
    {
        var path = CreateTempFile(new byte[256 * 1024]);
        using var buffer = new VirtualizedByteBuffer(path);
        using var progress = new BlockingProgress();
        using var saveStarted = new ManualResetEventSlim();
        buffer.SetByte(0, 0xFF);

        var searchTask = buffer.SearchAsync(
            new byte[] { 0x7F },
            VirtualizedByteBuffer.DefaultSearchResultLimit,
            progress,
            CancellationToken.None);
        Assert.True(progress.Started.Wait(TimeSpan.FromSeconds(5)));

        var saveTask = Task.Run(() =>
        {
            saveStarted.Set();
            buffer.Save();
        });
        Assert.True(saveStarted.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            var completedTask = await Task.WhenAny(saveTask, Task.Delay(100));
            Assert.NotSame(saveTask, completedTask);
        }
        finally
        {
            progress.Release.Set();
        }

        await Task.WhenAll(searchTask, saveTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0xFF, File.ReadAllBytes(path)[0]);
    }

    private sealed class BlockingProgress : IProgress<double>, IDisposable
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public void Report(double value)
        {
            Started.Set();
            Release.Wait();
        }

        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
        }
    }
}
