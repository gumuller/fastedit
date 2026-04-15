using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;
using Xunit;

namespace FastEdit.Tests;

public class FileNodeViewModelTests
{
    private readonly Mock<IFileSystemService> _fileSystemService = new();

    [Fact]
    public void Constructor_File_SetsProperties()
    {
        var node = new FileNodeViewModel(@"C:\folder\test.txt", false, _fileSystemService.Object);

        Assert.Equal("test.txt", node.Name);
        Assert.Equal(@"C:\folder\test.txt", node.FullPath);
        Assert.False(node.IsDirectory);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void Constructor_Directory_AddsDummyChild()
    {
        var node = new FileNodeViewModel(@"C:\folder", true, _fileSystemService.Object);

        Assert.True(node.IsDirectory);
        Assert.Single(node.Children);
        Assert.Equal("Loading...", node.Children[0].Name);
    }

    [Fact]
    public void LoadChildren_PopulatesDirectories()
    {
        _fileSystemService.Setup(f => f.GetDirectories(@"C:\root"))
            .Returns(new[] { @"C:\root\subdir" });
        _fileSystemService.Setup(f => f.GetFiles(@"C:\root", "*", false))
            .Returns(Array.Empty<string>());

        var node = new FileNodeViewModel(@"C:\root", true, _fileSystemService.Object);
        node.LoadChildren();

        Assert.Single(node.Children);
        Assert.Equal("subdir", node.Children[0].Name);
        Assert.True(node.Children[0].IsDirectory);
        Assert.True(node.IsLoaded);
    }

    [Fact]
    public void LoadChildren_PopulatesFiles()
    {
        _fileSystemService.Setup(f => f.GetDirectories(@"C:\root"))
            .Returns(Array.Empty<string>());
        _fileSystemService.Setup(f => f.GetFiles(@"C:\root", "*", false))
            .Returns(new[] { @"C:\root\file.txt" });

        var node = new FileNodeViewModel(@"C:\root", true, _fileSystemService.Object);
        node.LoadChildren();

        Assert.Single(node.Children);
        Assert.Equal("file.txt", node.Children[0].Name);
        Assert.False(node.Children[0].IsDirectory);
    }

    [Fact]
    public void LoadChildren_SkipsHiddenFolders()
    {
        _fileSystemService.Setup(f => f.GetDirectories(@"C:\root"))
            .Returns(new[] { @"C:\root\.git", @"C:\root\$Recycle.Bin", @"C:\root\visible" });
        _fileSystemService.Setup(f => f.GetFiles(@"C:\root", "*", false))
            .Returns(Array.Empty<string>());

        var node = new FileNodeViewModel(@"C:\root", true, _fileSystemService.Object);
        node.LoadChildren();

        Assert.Single(node.Children);
        Assert.Equal("visible", node.Children[0].Name);
    }

    [Fact]
    public void LoadChildren_SkipsHiddenFiles()
    {
        _fileSystemService.Setup(f => f.GetDirectories(@"C:\root"))
            .Returns(Array.Empty<string>());
        _fileSystemService.Setup(f => f.GetFiles(@"C:\root", "*", false))
            .Returns(new[] { @"C:\root\.hidden", @"C:\root\visible.txt" });

        var node = new FileNodeViewModel(@"C:\root", true, _fileSystemService.Object);
        node.LoadChildren();

        Assert.Single(node.Children);
        Assert.Equal("visible.txt", node.Children[0].Name);
    }

    [Fact]
    public void LoadChildren_SortsAlphabetically()
    {
        _fileSystemService.Setup(f => f.GetDirectories(@"C:\root"))
            .Returns(new[] { @"C:\root\Zebra", @"C:\root\Alpha" });
        _fileSystemService.Setup(f => f.GetFiles(@"C:\root", "*", false))
            .Returns(Array.Empty<string>());

        var node = new FileNodeViewModel(@"C:\root", true, _fileSystemService.Object);
        node.LoadChildren();

        Assert.Equal("Alpha", node.Children[0].Name);
        Assert.Equal("Zebra", node.Children[1].Name);
    }

    [Fact]
    public void LoadChildren_OnlyLoadsOnce()
    {
        _fileSystemService.Setup(f => f.GetDirectories(@"C:\root")).Returns(Array.Empty<string>());
        _fileSystemService.Setup(f => f.GetFiles(@"C:\root", "*", false)).Returns(Array.Empty<string>());

        var node = new FileNodeViewModel(@"C:\root", true, _fileSystemService.Object);
        node.LoadChildren();
        node.LoadChildren(); // Should not reload

        _fileSystemService.Verify(f => f.GetDirectories(@"C:\root"), Times.Once);
    }

    [Fact]
    public void Icon_File_ReturnsFileIcon()
    {
        var node = new FileNodeViewModel(@"C:\test.txt", false, _fileSystemService.Object);
        Assert.Equal("\uE8A5", node.Icon);
    }

    [Fact]
    public void Icon_Directory_ReturnsFolderIcon()
    {
        var node = new FileNodeViewModel(@"C:\folder", true, _fileSystemService.Object);
        Assert.Equal("\uE8B7", node.Icon);
    }
}
