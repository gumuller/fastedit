using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;
using Xunit;

namespace FastEdit.Tests;

public class FileTreeViewModelTests
{
    private readonly Mock<IFileService> _fileService = new();
    private readonly Mock<ISettingsService> _settingsService = new();
    private readonly Mock<IDialogService> _dialogService = new();
    private readonly Mock<IFileSystemService> _fileSystemService = new();

    private FileTreeViewModel CreateSut()
    {
        _fileSystemService.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        _fileSystemService.Setup(f => f.GetDirectories(It.IsAny<string>())).Returns(Array.Empty<string>());
        _fileSystemService.Setup(f => f.GetFiles(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(Array.Empty<string>());
        _settingsService.Setup(s => s.RecentFiles).Returns(new List<string>());

        return new FileTreeViewModel(
            _fileService.Object,
            _settingsService.Object,
            _dialogService.Object,
            _fileSystemService.Object);
    }

    [Fact]
    public void Constructor_LoadsLastFolder()
    {
        _settingsService.Setup(s => s.LastOpenedFolder).Returns(@"C:\Projects");

        var sut = CreateSut();

        Assert.Equal(@"C:\Projects", sut.RootPath);
        Assert.Single(sut.RootNodes);
    }

    [Fact]
    public void Constructor_FallsBackToHome_WhenLastFolderMissing()
    {
        _settingsService.Setup(s => s.LastOpenedFolder).Returns("");

        var sut = CreateSut();

        Assert.NotNull(sut.RootPath);
        Assert.Single(sut.RootNodes);
    }

    [Fact]
    public void SetRootFolder_UpdatesRootPath()
    {
        var sut = CreateSut();

        sut.SetRootFolderCommand.Execute(@"C:\NewFolder");

        Assert.Equal(@"C:\NewFolder", sut.RootPath);
        _settingsService.VerifySet(s => s.LastOpenedFolder = @"C:\NewFolder");
    }

    [Fact]
    public void SetRootFolder_InvalidDir_DoesNothing()
    {
        var sut = CreateSut();
        var originalPath = sut.RootPath;

        _fileSystemService.Setup(f => f.DirectoryExists(@"C:\NonExistent")).Returns(false);
        sut.SetRootFolderCommand.Execute(@"C:\NonExistent");

        Assert.Equal(originalPath, sut.RootPath);
    }

    [Fact]
    public void OpenFolder_ShowsDialog()
    {
        var sut = CreateSut();

        _dialogService.Setup(d => d.ShowFolderBrowserDialog(null)).Returns(@"C:\Selected");

        sut.OpenFolderCommand.Execute(null);

        Assert.Equal(@"C:\Selected", sut.RootPath);
    }

    [Fact]
    public void OpenFolder_CancelDialog_NoChange()
    {
        var sut = CreateSut();
        var originalPath = sut.RootPath;

        _dialogService.Setup(d => d.ShowFolderBrowserDialog(null)).Returns((string?)null);

        sut.OpenFolderCommand.Execute(null);

        Assert.Equal(originalPath, sut.RootPath);
    }

    [Fact]
    public void OpenSelectedFile_RaisesEvent()
    {
        var sut = CreateSut();
        string? openedFile = null;
        sut.FileOpenRequested += (_, path) => openedFile = path;

        var node = new FileNodeViewModel(@"C:\test.txt", false, _fileSystemService.Object);
        sut.SelectedNode = node;

        sut.OpenSelectedFileCommand.Execute(null);

        Assert.Equal(@"C:\test.txt", openedFile);
    }

    [Fact]
    public void OpenSelectedFile_Directory_DoesNotRaiseEvent()
    {
        var sut = CreateSut();
        bool raised = false;
        sut.FileOpenRequested += (_, _) => raised = true;

        var node = new FileNodeViewModel(@"C:\folder", true, _fileSystemService.Object);
        sut.SelectedNode = node;

        sut.OpenSelectedFileCommand.Execute(null);

        Assert.False(raised);
    }
}
