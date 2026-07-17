using System.IO;
using FastEdit.Services;

namespace FastEdit.Tests;

public class FileSystemServiceTests
{
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
