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
}
