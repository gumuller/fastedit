using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using Moq;

namespace FastEdit.Tests;

public class DroppedPathClassifierTests
{
    private readonly Mock<IFileSystemService> _fileSystemService = new();

    [Fact]
    public void Classify_ReturnsFileDirectoryAndUnsupportedActions()
    {
        _fileSystemService.Setup(fs => fs.FileExists("file.txt")).Returns(true);
        _fileSystemService.Setup(fs => fs.DirectoryExists("folder")).Returns(true);

        var actions = DroppedPathClassifier.Classify(
            new[] { "file.txt", "folder", "missing" },
            _fileSystemService.Object);

        Assert.Equal(
            new[]
            {
                new DroppedPathAction("file.txt", DroppedPathKind.File),
                new DroppedPathAction("folder", DroppedPathKind.Directory),
                new DroppedPathAction("missing", DroppedPathKind.Unsupported),
            },
            actions);
    }

    [Fact]
    public void Classify_DoesNotCheckDirectoryForExistingFile()
    {
        _fileSystemService.Setup(fs => fs.FileExists("file.txt")).Returns(true);

        _ = DroppedPathClassifier.Classify(new[] { "file.txt" }, _fileSystemService.Object);

        _fileSystemService.Verify(fs => fs.DirectoryExists("file.txt"), Times.Never);
    }
}
