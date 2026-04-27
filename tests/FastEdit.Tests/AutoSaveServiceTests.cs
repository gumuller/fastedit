using System.IO;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FluentAssertions;
using Moq;

namespace FastEdit.Tests;

public class AutoSaveServiceTests
{
    private readonly Mock<IFileSystemService> _mockFs = new();
    private readonly AutoSaveService _sut;
    private readonly string _autoSaveDir;

    public AutoSaveServiceTests()
    {
        _autoSaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "AutoSave");
        _sut = new AutoSaveService(_mockFs.Object);
    }

    [Fact]
    public void HasRecoveryFiles_NoDirectory_ReturnsFalse()
    {
        _mockFs.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(false);
        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void HasRecoveryFiles_CleanShutdown_ReturnsFalse()
    {
        _mockFs.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(true);
        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void HasRecoveryFiles_UncleanWithManifest_ReturnsTrue()
    {
        _mockFs.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(false);
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "manifest.json"))).Returns(true);
        _sut.HasRecoveryFiles().Should().BeTrue();
    }

    [Fact]
    public void HasRecoveryFiles_UncleanNoManifest_ReturnsFalse()
    {
        _mockFs.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(false);
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "manifest.json"))).Returns(false);
        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void MarkCleanShutdown_WritesMarker()
    {
        _sut.MarkCleanShutdown();
        _mockFs.Verify(f => f.WriteAllText(
            Path.Combine(_autoSaveDir, ".clean-shutdown"),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void SaveNow_WritesContentAndManifest()
    {
        var entries = new[]
        {
            new AutoSaveEntry("id1", "test.txt", @"C:\test.txt", "content here", false, 10, 20.5)
        };

        _sut.SaveNow(entries);

        _mockFs.Verify(f => f.WriteAllText(
            Path.Combine(_autoSaveDir, "id1.txt"), "content here"), Times.Once);
        _mockFs.Verify(f => f.WriteAllText(
            Path.Combine(_autoSaveDir, "manifest.json"),
            It.Is<string>(s => s.Contains("id1") && s.Contains("test.txt"))), Times.Once);
    }

    [Fact]
    public void SaveNow_MultipleEntries_WritesAll()
    {
        var entries = new[]
        {
            new AutoSaveEntry("a", "a.txt", null, "aaa", true),
            new AutoSaveEntry("b", "b.txt", @"C:\b.txt", "bbb", false)
        };

        _sut.SaveNow(entries);

        _mockFs.Verify(f => f.WriteAllText(Path.Combine(_autoSaveDir, "a.txt"), "aaa"), Times.Once);
        _mockFs.Verify(f => f.WriteAllText(Path.Combine(_autoSaveDir, "b.txt"), "bbb"), Times.Once);
    }

    [Fact]
    public void GetRecoveryEntries_ValidManifest_ReturnsEntries()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var manifest = """[{"Id":"id1","FileName":"test.txt","FilePath":"C:\\test.txt","ContentFile":"id1.txt","IsUntitled":false,"CursorOffset":5,"ScrollOffset":10.0}]""";

        _mockFs.Setup(f => f.FileExists(manifestPath)).Returns(true);
        _mockFs.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "id1.txt"))).Returns(true);
        _mockFs.Setup(f => f.ReadAllText(Path.Combine(_autoSaveDir, "id1.txt"))).Returns("recovered content");

        var entries = _sut.GetRecoveryEntries();

        entries.Should().HaveCount(1);
        entries[0].Id.Should().Be("id1");
        entries[0].FileName.Should().Be("test.txt");
        entries[0].Content.Should().Be("recovered content");
        entries[0].CursorOffset.Should().Be(5);
    }

    [Fact]
    public void GetRecoveryEntries_NoManifest_ReturnsEmpty()
    {
        _mockFs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        _sut.GetRecoveryEntries().Should().BeEmpty();
    }

    [Fact]
    public void GetRecoveryEntries_InvalidManifest_LogsWarningAndReturnsEmpty()
    {
        using var trace = new TraceCapture();
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        _mockFs.Setup(f => f.FileExists(manifestPath)).Returns(true);
        _mockFs.Setup(f => f.ReadAllText(manifestPath)).Returns("not json");

        _sut.GetRecoveryEntries().Should().BeEmpty();
        System.Diagnostics.Trace.Flush();

        trace.Messages.Should().Contain("Failed to read auto-save recovery manifest");
    }

    [Fact]
    public void GetRecoveryEntries_MissingContentFile_SkipsEntry()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var manifest = """[{"Id":"id1","FileName":"test.txt","FilePath":null,"ContentFile":"id1.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}]""";

        _mockFs.Setup(f => f.FileExists(manifestPath)).Returns(true);
        _mockFs.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "id1.txt"))).Returns(false);

        _sut.GetRecoveryEntries().Should().BeEmpty();
    }

    [Fact]
    public void ClearRecoveryFiles_DeletesAllTxtFiles()
    {
        _mockFs.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _mockFs.Setup(f => f.GetFiles(_autoSaveDir, "*.txt", false)).Returns(new[]
        {
            Path.Combine(_autoSaveDir, "id1.txt"),
            Path.Combine(_autoSaveDir, "id2.txt")
        });
        _mockFs.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "manifest.json"))).Returns(true);

        _sut.ClearRecoveryFiles();

        _mockFs.Verify(f => f.DeleteFile(Path.Combine(_autoSaveDir, "id1.txt")), Times.Once);
        _mockFs.Verify(f => f.DeleteFile(Path.Combine(_autoSaveDir, "id2.txt")), Times.Once);
        _mockFs.Verify(f => f.DeleteFile(Path.Combine(_autoSaveDir, "manifest.json")), Times.Once);
    }

    [Fact]
    public void ClearRecoveryFiles_DeleteFailure_LogsWarningAndContinues()
    {
        using var trace = new TraceCapture();
        var firstFile = Path.Combine(_autoSaveDir, "id1.txt");
        var secondFile = Path.Combine(_autoSaveDir, "id2.txt");
        _mockFs.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _mockFs.Setup(f => f.GetFiles(_autoSaveDir, "*.txt", false)).Returns(new[] { firstFile, secondFile });
        _mockFs.Setup(f => f.DeleteFile(firstFile)).Throws(new IOException("locked"));

        _sut.ClearRecoveryFiles();
        System.Diagnostics.Trace.Flush();

        _mockFs.Verify(f => f.DeleteFile(secondFile), Times.Once);
        trace.Messages.Should().Contain("Failed to delete auto-save recovery file");
        trace.Messages.Should().Contain(firstFile);
    }

    [Fact]
    public void ClearRecoveryFiles_GetFilesFailure_LogsWarning()
    {
        using var trace = new TraceCapture();
        _mockFs.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _mockFs.Setup(f => f.GetFiles(_autoSaveDir, "*.txt", false)).Throws(new IOException("unavailable"));

        _sut.ClearRecoveryFiles();
        System.Diagnostics.Trace.Flush();

        trace.Messages.Should().Contain("Failed to clear auto-save recovery files");
    }

    [Fact]
    public void IsEnabled_DefaultsToTrue()
    {
        _sut.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void IntervalSeconds_DefaultsTo60()
    {
        _sut.IntervalSeconds.Should().Be(60);
    }
}
