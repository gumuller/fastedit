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
    public void WriteSnapshot_IncludesUncachedModifiedBytesWithoutChangingSource()
    {
        var path = CreateTempFile(new byte[] { 0, 1, 2, 3 });
        using var buffer = new VirtualizedByteBuffer(path);
        buffer.SetByte(1, 42);
        using var snapshot = new MemoryStream();

        buffer.WriteSnapshot(snapshot);

        Assert.Equal(new byte[] { 0, 42, 2, 3 }, snapshot.ToArray());
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
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
    public void Save_WriteFailureLeavesOriginalAndPendingEditsIntact()
    {
        var original = new byte[] { 0x01, 0x02, 0x03 };
        var path = CreateTempFile(original);
        using var buffer = new VirtualizedByteBuffer(
            path,
            destination =>
            {
                destination.WriteByte(0xFF);
                Assert.Equal(original, File.ReadAllBytes(path));
                throw new IOException("injected snapshot failure");
            });
        buffer.SetByte(1, 0xAA);

        Assert.Throws<IOException>(() => buffer.Save());

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(0xAA, buffer.GetByte(1));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.*.tmp"));
    }

    [Fact]
    public async Task Save_DoesNotClearEditArrivingDuringAtomicCommit()
    {
        var path = CreateTempFile(new byte[] { 0, 1, 2 });
        using var writerStarted = new ManualResetEventSlim();
        using var releaseWriter = new ManualResetEventSlim();
        using var buffer = new VirtualizedByteBuffer(
            path,
            destination =>
            {
                destination.Write(new byte[] { 0, 42, 2 });
                writerStarted.Set();
                releaseWriter.Wait();
            });
        buffer.SetByte(1, 42);

        var saveTask = Task.Run(buffer.Save);
        Assert.True(await Task.Run(
            () => writerStarted.Wait(TimeSpan.FromSeconds(5))));
        var laterEdit = Task.Run(() => buffer.SetByte(2, 99));
        await Task.Delay(50);
        Assert.False(laterEdit.IsCompleted);
        releaseWriter.Set();
        await saveTask;
        await laterEdit;

        Assert.Equal(new byte[] { 0, 42, 2 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(99, buffer.GetByte(2));
    }

    [Fact]
    public void Save_RefusesToOverwriteConcurrentPathReplacement()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        var replacement = CreateTempFile(new byte[] { 9, 9, 9 });
        using var buffer = new VirtualizedByteBuffer(
            path,
            destination => destination.Write(new byte[] { 1, 42, 3 }),
            () => ReplaceFileWithRetries(replacement, path));
        buffer.SetByte(1, 42);

        Assert.Throws<IOException>(() => buffer.Save());

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(42, buffer.GetByte(1));
    }

    [Fact]
    public async Task Save_ConflictRetainsSearchableSnapshotAndDiscardBaseline()
    {
        var original = Enumerable.Repeat((byte)0x11, 2 * 1024 * 1024)
            .ToArray();
        var replacementBytes = Enumerable.Repeat((byte)0x99, original.Length)
            .ToArray();
        var path = CreateTempFile(original);
        var replacement = CreateTempFile(replacementBytes);
        using var buffer = new VirtualizedByteBuffer(
            path,
            saveSnapshotWriter: null,
            beforeAtomicCommit: () =>
                ReplaceFileWithRetries(replacement, path));
        buffer.SetByte(original.Length - 1, 0x42);

        Assert.Throws<IOException>(() => buffer.Save());

        Assert.Equal(0x99, File.ReadAllBytes(path)[100_000]);
        using var snapshot = new MemoryStream();
        buffer.WriteSnapshot(snapshot);
        var retained = snapshot.ToArray();
        Assert.Equal(0x11, retained[100_000]);
        Assert.Equal(0x42, retained[^1]);
        Assert.True(buffer.HasModifications);

        var search = await buffer.SearchAsync(
            new byte[] { 0x11, 0x11, 0x11 },
            maxResults: 1,
            progress: null,
            CancellationToken.None);
        Assert.Equal(0, Assert.Single(search.Results));

        buffer.DiscardModifications();
        Assert.False(buffer.HasModifications);
        using var discarded = new MemoryStream();
        buffer.WriteSnapshot(discarded);
        var clean = discarded.ToArray();
        Assert.Equal(0x11, clean[100_000]);
        Assert.Equal(0x11, clean[^1]);
        Assert.Equal(0x99, File.ReadAllBytes(path)[100_000]);
    }

    [Fact]
    public void Save_BlocksInPlaceExternalWritesThroughAtomicReplacement()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        using var buffer = new VirtualizedByteBuffer(
            path,
            destination => destination.Write(new byte[] { 1, 42, 3 }),
            () => File.WriteAllBytes(path, new byte[] { 9, 9, 9 }));
        buffer.SetByte(1, 42);

        Assert.Throws<IOException>(() => buffer.Save());

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(42, buffer.GetByte(1));
    }

    [Fact]
    public void Save_ReconcilesReplacementThatCommittedBeforeReportedFailure()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        using var buffer = new VirtualizedByteBuffer(
            path,
            saveSnapshotWriter: null,
            beforeAtomicCommit: null,
            replaceFile: (source, destination, backup) =>
            {
                File.Replace(source, destination, backup);
                throw new AtomicIOException(1177);
            });
        buffer.SetByte(1, 42);

        buffer.Save();

        Assert.Equal(new byte[] { 1, 42, 3 }, File.ReadAllBytes(path));
        Assert.False(buffer.HasModifications);
    }

    [Fact]
    public void Save_PartialReplacementRestoresMissingDestinationAndStaysDirty()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        using var buffer = new VirtualizedByteBuffer(
            path,
            saveSnapshotWriter: null,
            beforeAtomicCommit: null,
            replaceFile: (source, destination, backup) =>
            {
                File.Move(destination, backup);
                throw new AtomicIOException(1177);
            });
        buffer.SetByte(1, 42);

        Assert.Throws<AtomicIOException>(() => buffer.Save());

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
        Assert.Equal(42, buffer.GetByte(1));
    }

    [Fact]
    public void Save_PostCommitPathReplacementIsNotAdoptedOrCleared()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        var external = CreateTempFile(new byte[] { 9, 9, 9 });
        using var buffer = new VirtualizedByteBuffer(
            path,
            saveSnapshotWriter: null,
            beforeAtomicCommit: null,
            replaceFile: (source, destination, backup) =>
            {
                File.Replace(source, destination, backup);
                ReplaceFileWithRetries(external, destination);
            });
        buffer.SetByte(1, 42);

        Assert.Throws<IOException>(() => buffer.Save());

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
        using var retained = new MemoryStream();
        buffer.WriteSnapshot(retained);
        Assert.Equal(new byte[] { 1, 42, 3 }, retained.ToArray());
        Assert.True(buffer.HasModifications);
    }

    [Fact]
    public void Save_PartialConflictRollbackRestoresExternalTargetAndEditedSnapshot()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        var external = CreateTempFile(new byte[] { 9, 9, 9 });
        var replaceAttempts = 0;
        using var buffer = new VirtualizedByteBuffer(
            path,
            saveSnapshotWriter: null,
            beforeAtomicCommit: () =>
                ReplaceFileWithRetries(external, path),
            replaceFile: (source, destination, backup) =>
            {
                replaceAttempts++;
                if (replaceAttempts == 1)
                {
                    File.Replace(source, destination, backup);
                    return;
                }
                File.Move(destination, backup);
                throw new AtomicIOException(1177);
            });
        buffer.SetByte(1, 42);

        Assert.Throws<AtomicIOException>(() => buffer.Save());

        Assert.Equal(2, replaceAttempts);
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
        using var retained = new MemoryStream();
        buffer.WriteSnapshot(retained);
        Assert.Equal(new byte[] { 1, 42, 3 }, retained.ToArray());
        Assert.True(buffer.HasModifications);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(1175)]
    public void Save_RetriesRetryableWindowsReplacementFailures(int errorCode)
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        var attempts = 0;
        using var buffer = new VirtualizedByteBuffer(
            path,
            saveSnapshotWriter: null,
            beforeAtomicCommit: null,
            replaceFile: (source, destination, backup) =>
            {
                attempts++;
                if (attempts < 4)
                    throw new AtomicIOException(errorCode);
                File.Replace(source, destination, backup);
            });
        buffer.SetByte(1, 42);

        buffer.Save();

        Assert.Equal(4, attempts);
        Assert.Equal(new byte[] { 1, 42, 3 }, File.ReadAllBytes(path));
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
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                return;
            }

            using var buffer = new VirtualizedByteBuffer(linkPath);
            buffer.SetByte(1, 0xFF);

            Assert.Throws<IOException>(buffer.Save);
            Assert.Equal(new byte[] { 0x10, 0x20 }, File.ReadAllBytes(targetPath));
            Assert.True(
                File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint));
            Assert.True(buffer.HasModifications);
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
        Assert.True(buffer.HasModifications);
    }

    [Fact]
    public void Save_HardLinkedPathFailsWithoutBreakingLinkIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(
            Path.GetTempPath(),
            $"fastedit-hardlink-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source.bin");
        var linkPath = Path.Combine(directory, "linked.bin");
        try
        {
            File.WriteAllBytes(path, new byte[] { 0x10, 0x20 });
            WindowsFileSystemTestHelper.CreateHardLink(linkPath, path);
            using var buffer = new VirtualizedByteBuffer(path);
            buffer.SetByte(1, 0xFF);

            Assert.Throws<IOException>(buffer.Save);

            Assert.Equal(new byte[] { 0x10, 0x20 }, File.ReadAllBytes(path));
            Assert.Equal(new byte[] { 0x10, 0x20 }, File.ReadAllBytes(linkPath));
            Assert.True(buffer.HasModifications);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_DoesNotRetryNonSharingFailures()
    {
        var path = CreateTempFile(new byte[] { 1, 2, 3 });
        var attempts = 0;
        using var buffer = new VirtualizedByteBuffer(
            path,
            saveSnapshotWriter: null,
            beforeAtomicCommit: null,
            replaceFile: (_, _, _) =>
            {
                attempts++;
                throw new AtomicIOException(1177);
            });
        buffer.SetByte(1, 42);

        Assert.Throws<AtomicIOException>(() => buffer.Save());

        Assert.Equal(1, attempts);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.True(buffer.HasModifications);
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

    private static void ReplaceFileWithRetries(
        string sourcePath,
        string destinationPath)
    {
        const int maxAttempts = 10;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Replace(sourcePath, destinationPath, null);
                return;
            }
            catch (Exception ex) when (
                attempt < maxAttempts - 1 &&
                File.Exists(sourcePath) &&
                ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(
                    10 * (attempt + 1)));
            }
        }
    }

    private sealed class AtomicIOException : IOException
    {
        public AtomicIOException(int errorCode)
        {
            HResult = unchecked((int)(0x80070000u | (uint)errorCode));
        }
    }
}
