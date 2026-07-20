using System.IO;
using FastEdit.Services;
using FastEdit.Services.Interfaces;

namespace FastEdit.Tests;

public class FileSystemServiceTests
{
    [Fact]
    public void SecureOperations_ReadAndDeleteRegularFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "payload.bin");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);

        try
        {
            ISecureFileSystemService service = new FileSystemService();

            Assert.Equal([0x01, 0x02, 0x03], service.ReadAllBytesNoFollow(path));

            service.DeleteFileNoFollow(path);

            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SecureOperations_RejectSymbolicLinkWithoutTouchingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var targetPath = Path.Combine(directory, "outside.bin");
        var linkPath = Path.Combine(directory, "payload.bin");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(targetPath, [0x2A]);

        try
        {
            try
            {
                File.CreateSymbolicLink(linkPath, targetPath);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                return;
            }

            ISecureFileSystemService service = new FileSystemService();

            Assert.Throws<InvalidDataException>(() => service.ReadAllBytesNoFollow(linkPath));
            Assert.Throws<InvalidDataException>(() => service.DeleteFileNoFollow(linkPath));
            Assert.Equal([0x2A], File.ReadAllBytes(targetPath));
        }
        finally
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_ReplacesExistingContentWithoutLeavingTempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "data.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, "old");

        try
        {
            new FileSystemService().WriteAllTextAtomic(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_ReconcilesCommittedReplacementFailure()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "data.json");
        File.WriteAllText(path, "old");
        var service = CreateService(
            replaceFile: (source, destination, backup) =>
            {
                File.Replace(source, destination, backup);
                throw new AtomicIOException(1177);
            });
        try
        {
            service.WriteAllTextAtomic(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteStreamAtomic_PartialSaveAsFailureRestoresExistingDestination()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-as.bin");
        File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
        var service = CreateService(
            replaceFile: (_, destination, backup) =>
            {
                File.Move(destination, backup);
                throw new AtomicIOException(1177);
            });
        try
        {
            Assert.Throws<AtomicIOException>(() =>
                service.WriteStreamAtomic(
                    path,
                    stream => stream.Write(new byte[] { 1, 2, 3 })));

            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteStreamAtomic_NewSaveAsReconcilesCommittedMoveFailure()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-as.bin");
        var service = CreateService(
            moveFile: (source, destination, overwrite) =>
            {
                File.Move(source, destination, overwrite);
                throw new AtomicIOException(1177);
            });
        try
        {
            service.WriteStreamAtomic(
                path,
                stream => stream.Write(new byte[] { 1, 2, 3 }));

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteStreamAtomic_PartialNewSaveAsNeverLeavesDestinationMissing()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-as.bin");
        var service = CreateService(
            moveFile: (source, _, _) =>
            {
                File.Delete(source);
                throw new AtomicIOException(1177);
            });
        try
        {
            Assert.Throws<AtomicIOException>(() =>
                service.WriteStreamAtomic(
                    path,
                    stream => stream.Write(new byte[] { 1, 2, 3 })));

            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteStreamAtomic_PartialConflictRollbackPreservesBothVersions()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-as.bin");
        var externalPath = Path.Combine(directory, "external.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(externalPath, new byte[] { 9, 9, 9 });
        var attempts = 0;
        var service = CreateService(
            replaceFile: (source, destination, backup) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    File.Replace(externalPath, destination, null);
                    File.Replace(source, destination, backup);
                    return;
                }
                File.Move(destination, backup);
                throw new AtomicIOException(1177);
            });
        try
        {
            Assert.Throws<AtomicIOException>(() =>
                service.WriteStreamAtomic(
                    path,
                    stream => stream.Write(new byte[] { 4, 5, 6 })));

            Assert.Equal(2, attempts);
            Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(path));
            Assert.Contains(
                Directory.GetFiles(directory, "*.snapshot"),
                snapshot =>
                    File.ReadAllBytes(snapshot)
                        .SequenceEqual(new byte[] { 4, 5, 6 }));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(1175)]
    public void WriteAllTextAtomic_RetriesRetryableWindowsReplacementFailures(
        int errorCode)
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "data.json");
        File.WriteAllText(path, "old");
        var attempts = 0;
        var delays = 0;
        var service = CreateService(
            replaceFile: (source, destination, backup) =>
            {
                attempts++;
                if (attempts < 4)
                    throw new AtomicIOException(errorCode);
                File.Replace(source, destination, backup);
            },
            delay: _ => delays++);
        try
        {
            service.WriteAllTextAtomic(path, "new");

            Assert.Equal(4, attempts);
            Assert.Equal(3, delays);
            Assert.Equal("new", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_RejectsReparsePointWithoutChangingTarget()
    {
        var directory = CreateDirectory();
        var targetPath = Path.Combine(directory, "target.json");
        var linkPath = Path.Combine(directory, "data.json");
        File.WriteAllText(targetPath, "old");
        try
        {
            try
            {
                File.CreateSymbolicLink(linkPath, targetPath);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                return;
            }

            Assert.Throws<IOException>(
                () => new FileSystemService().WriteAllTextAtomic(linkPath, "new"));

            Assert.Equal("old", File.ReadAllText(targetPath));
            Assert.True(
                File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_RejectsAlternateStreamWithoutLosingMetadata()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = CreateDirectory();
        var path = Path.Combine(directory, "data.json");
        var streamPath = $"{path}:fastedit-test";
        File.WriteAllText(path, "old");
        try
        {
            try
            {
                File.WriteAllText(streamPath, "protected metadata");
            }
            catch (IOException)
            {
                return;
            }

            Assert.Throws<IOException>(
                () => new FileSystemService().WriteAllTextAtomic(path, "new"));

            Assert.Equal("old", File.ReadAllText(path));
            Assert.Equal("protected metadata", File.ReadAllText(streamPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_RejectsHardLinkWithoutBreakingIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var directory = CreateDirectory();
        var path = Path.Combine(directory, "data.json");
        var linkPath = Path.Combine(directory, "linked.json");
        File.WriteAllText(path, "old");
        try
        {
            WindowsFileSystemTestHelper.CreateHardLink(linkPath, path);

            Assert.Throws<IOException>(
                () => new FileSystemService().WriteAllTextAtomic(path, "new"));

            Assert.Equal("old", File.ReadAllText(path));
            Assert.Equal("old", File.ReadAllText(linkPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_DoesNotRetryNonSharingFailure()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "data.json");
        File.WriteAllText(path, "old");
        var attempts = 0;
        var service = CreateService(
            replaceFile: (_, _, _) =>
            {
                attempts++;
                throw new AtomicIOException(1177);
            },
            delay: _ => throw new InvalidOperationException(
                "A non-sharing failure must not be retried."));
        try
        {
            Assert.Throws<AtomicIOException>(
                () => service.WriteAllTextAtomic(path, "new"));

            Assert.Equal(1, attempts);
            Assert.Equal("old", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllTextAtomic_StressLeavesNoTransientArtifacts()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "data.json");
        File.WriteAllText(path, "0");
        var service = new FileSystemService();
        try
        {
            for (var index = 1; index <= 50; index++)
                service.WriteAllTextAtomic(path, index.ToString());

            Assert.Equal("50", File.ReadAllText(path));
            Assert.Empty(
                Directory.GetFiles(directory)
                    .Where(file => file != path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static FileSystemService CreateService(
        Action<string, string, string>? replaceFile = null,
        Action<string, string, bool>? moveFile = null,
        Action<TimeSpan>? delay = null) =>
        new(
            moveFile ??
                ((source, destination, overwrite) =>
                    File.Move(source, destination, overwrite)),
            replaceFile ??
                ((source, destination, backup) =>
                    File.Replace(source, destination, backup)),
            delay ?? Thread.Sleep);

    private sealed class AtomicIOException : IOException
    {
        public AtomicIOException(int errorCode)
        {
            HResult = unchecked((int)(0x80070000u | (uint)errorCode));
        }
    }
}
