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
    public void CreateSnapshot_IncludesUnsavedModifiedBytesWithoutWritingSource()
    {
        var path = CreateTempFile(new byte[] { 0x10, 0x20, 0x30 });
        using var buffer = new VirtualizedByteBuffer(path);
        buffer.SetByte(1, 0xFF);

        var snapshot = buffer.CreateSnapshot();

        Assert.Equal(new byte[] { 0x10, 0xFF, 0x30 }, snapshot);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void SaveTo_NewPathStreamsModifiedBytesWithoutChangingSource()
    {
        var sourcePath = CreateTempFile(new byte[] { 0x10, 0x20, 0x30 });
        var destinationPath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.bin");
        _tempFiles.Add(destinationPath);
        using var buffer = new VirtualizedByteBuffer(sourcePath);
        buffer.SetByte(1, 0xFF);

        buffer.SaveTo(destinationPath);

        Assert.Equal(new byte[] { 0x10, 0xFF, 0x30 }, File.ReadAllBytes(destinationPath));
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, File.ReadAllBytes(sourcePath));
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
    public void Save_MidSnapshotFailure_LeavesOriginalUntouched()
    {
        var path = CreateTempFile(new byte[] { 0x10, 0x20, 0x30 });
        using var buffer = new VirtualizedByteBuffer(
            path,
            (stage, _) =>
            {
                if (stage == BinarySaveStage.SnapshotProgress)
                    throw new IOException("injected write failure");
            });
        buffer.SetByte(1, 0xFF);

        var action = buffer.Save;

        Assert.Throws<IOException>(action);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(0xFF, buffer.GetByte(1));
    }

    [Fact]
    public void Save_ReopenFailureRollsBackAndKeepsBufferUsable()
    {
        var path = CreateTempFile(new byte[] { 0x10, 0x20, 0x30 });
        using var buffer = new VirtualizedByteBuffer(
            path,
            (stage, _) =>
            {
                if (stage == BinarySaveStage.BeforeReopen)
                    throw new IOException("injected reopen failure");
            });
        buffer.SetByte(1, 0xFF);

        Assert.Throws<IOException>(buffer.Save);

        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(0xFF, buffer.GetByte(1));
        var saveAsPath = CreateTempFile(Array.Empty<byte>());
        buffer.SaveTo(saveAsPath);
        Assert.Equal(new byte[] { 0x10, 0xFF, 0x30 }, File.ReadAllBytes(saveAsPath));
    }

    [Fact]
    public void Save_ConcurrentReplacementAtCommitIsDetectedAndPreserved()
    {
        var path = CreateTempFile(new byte[] { 0x10, 0x20, 0x30 });
        var replacement = CreateTempFile(new byte[] { 0xAA, 0xBB, 0xCC });
        using var buffer = new VirtualizedByteBuffer(path, (stage, _) =>
        {
            if (stage == BinarySaveStage.BeforeCommit)
                File.Move(replacement, path, overwrite: true);
        });
        buffer.SetByte(1, 0xFF);

        var action = buffer.Save;

        Assert.Throws<IOException>(action);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(0xFF, buffer.GetByte(1));
        Assert.Throws<IOException>(() => buffer.SaveTo(path));
    }

    [Fact]
    public void Save_ConcurrentInPlaceWriteAtCommitIsDetectedAndPreserved()
    {
        var path = CreateTempFile(new byte[] { 0x10, 0x20, 0x30 });
        using var buffer = new VirtualizedByteBuffer(path, (stage, _) =>
        {
            if (stage == BinarySaveStage.BeforeCommit)
                File.WriteAllBytes(path, new byte[] { 0xAA, 0xBB, 0xCC });
        });
        buffer.SetByte(1, 0xFF);

        Assert.Throws<IOException>(buffer.Save);

        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(0xFF, buffer.GetByte(1));
    }

    [Fact]
    public void IsBackedBy_UsesFileIdentityForNormalSameFilePath()
    {
        var path = CreateTempFile(new byte[] { 0x10, 0x20 });
        using var buffer = new VirtualizedByteBuffer(path);

        Assert.True(buffer.IsBackedBy(path));
    }

    [Fact]
    public void Save_ReparsePointPathFailsWithoutReplacingTarget()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"fastedit-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, "target.bin");
        var linkPath = Path.Combine(directory, "link.bin");
        try
        {
            File.WriteAllBytes(targetPath, new byte[] { 0x10, 0x20 });
            try
            {
                File.CreateSymbolicLink(linkPath, targetPath);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }

            using var buffer = new VirtualizedByteBuffer(linkPath);
            buffer.SetByte(1, 0xFF);

            Assert.Throws<IOException>(buffer.Save);
            Assert.Equal(new byte[] { 0x10, 0x20 }, File.ReadAllBytes(targetPath));
            Assert.True(File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_FileWithAlternateStreamFailsWithoutLosingStream()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var path = CreateTempFile(new byte[] { 0x10, 0x20 });
        var streamPath = $"{path}:fastedit-test";
        try
        {
            File.WriteAllText(streamPath, "protected metadata");
        }
        catch (IOException)
        {
            return;
        }
        using var buffer = new VirtualizedByteBuffer(path);
        buffer.SetByte(1, 0xFF);

        Assert.Throws<IOException>(buffer.Save);

        Assert.Equal(new byte[] { 0x10, 0x20 }, File.ReadAllBytes(path));
        Assert.Equal("protected metadata", File.ReadAllText(streamPath));
    }

    [Fact]
    public void SaveTo_DistinguishesCaseSensitiveSiblingFiles_WhenSupported()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"fastedit-case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var upperPath = Path.Combine(directory, "File.bin");
        var lowerPath = Path.Combine(directory, "file.bin");
        try
        {
            File.WriteAllBytes(upperPath, new byte[] { 0x10, 0x20 });
            File.WriteAllBytes(lowerPath, new byte[] { 0xAA, 0xBB });
            if (Directory.GetFiles(directory).Length != 2)
                return;

            using var buffer = new VirtualizedByteBuffer(upperPath);
            buffer.SetByte(1, 0xFF);

            Assert.False(buffer.IsBackedBy(lowerPath));
            buffer.SaveTo(lowerPath);

            Assert.Equal(new byte[] { 0x10, 0x20 }, File.ReadAllBytes(upperPath));
            Assert.Equal(new byte[] { 0x10, 0xFF }, File.ReadAllBytes(lowerPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
    public async Task Save_Cancels_Active_Search_Before_Exclusive_Write()
    {
        var path = Path.GetTempFileName();
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength(128L * 1024 * 1024);
        _tempFiles.Add(path);

        using var buffer = new VirtualizedByteBuffer(path);
        var search = buffer.SearchAsync(
            new byte[] { 0xFF },
            VirtualizedByteBuffer.DefaultSearchResultLimit,
            progress: null,
            CancellationToken.None);

        buffer.SetByte(0, 0x7F);
        buffer.Save();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => search);
        using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(0x7F, readStream.ReadByte());
        Assert.False(buffer.HasModifications);
    }
}
