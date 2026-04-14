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
}
