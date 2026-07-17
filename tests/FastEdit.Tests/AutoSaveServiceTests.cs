using System.IO;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FluentAssertions;
using Moq;

namespace FastEdit.Tests;

public class AutoSaveServiceTests
{
    private readonly Mock<IFileSystemService> _fileSystem = new();
    private readonly Mock<IDispatcherService> _dispatcher = new();
    private readonly AutoSaveService _sut;
    private readonly string _autoSaveDir;

    public AutoSaveServiceTests()
    {
        _autoSaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "AutoSave");
        _dispatcher.Setup(dispatcher => dispatcher.Invoke(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        _dispatcher.Setup(dispatcher => dispatcher.InvokeAsync(It.IsAny<Func<List<AutoSaveEntry>>>()))
            .Returns((Func<List<AutoSaveEntry>> action) => Task.FromResult(action()));
        _sut = new AutoSaveService(_fileSystem.Object, _dispatcher.Object);
    }

    [Fact]
    public void HasRecoveryFiles_NoDirectory_ReturnsFalse()
    {
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(_autoSaveDir)).Returns(false);

        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void HasRecoveryFiles_CleanShutdown_ReturnsFalse()
    {
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(true);

        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void HasRecoveryFiles_UncleanWithManifest_ReturnsTrue()
    {
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(Path.Combine(_autoSaveDir, "manifest.json"))).Returns(true);

        _sut.HasRecoveryFiles().Should().BeTrue();
    }

    [Fact]
    public void HasRecoveryFiles_UncleanWithoutManifest_ReturnsFalse()
    {
        _fileSystem.Setup(fileSystem => fileSystem.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(false);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(Path.Combine(_autoSaveDir, "manifest.json"))).Returns(false);

        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void SaveNow_WritesContentAndManifestTransactionally()
    {
        var entries = new[]
        {
            new AutoSaveEntry("id1", "test.txt", @"C:\test.txt", "content here", false, 10, 20.5)
        };

        _sut.SaveNow(entries);

        _fileSystem.Verify(fileSystem => fileSystem.WriteAllText(
            It.Is<string>(path => path.StartsWith(Path.Combine(_autoSaveDir, "id1.txt.")) && path.EndsWith(".tmp")),
            "content here"), Times.Once);
        _fileSystem.Verify(fileSystem => fileSystem.MoveFile(
            It.Is<string>(path => path.StartsWith(Path.Combine(_autoSaveDir, "id1.txt."))),
            Path.Combine(_autoSaveDir, "id1.txt"),
            true), Times.Once);
        _fileSystem.Verify(fileSystem => fileSystem.MoveFile(
            It.Is<string>(path => path.StartsWith(Path.Combine(_autoSaveDir, "manifest.json."))),
            Path.Combine(_autoSaveDir, "manifest.json"),
            true), Times.Once);
    }

    [Fact]
    public void SaveNow_ContentWriteFailure_DoesNotReplaceManifest()
    {
        _fileSystem.Setup(fileSystem => fileSystem.WriteAllText(
                It.Is<string>(path => path.Contains("id1.txt.")),
                It.IsAny<string>()))
            .Throws(new IOException("disk full"));

        var action = () => _sut.SaveNow(
            new[] { new AutoSaveEntry("id1", "test.txt", null, "content", true) });

        action.Should().Throw<IOException>();
        _fileSystem.Verify(fileSystem => fileSystem.MoveFile(
            It.IsAny<string>(),
            Path.Combine(_autoSaveDir, "manifest.json"),
            It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void GetRecoveryEntries_ReportsMissingContentWithoutDroppingOtherEntries()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var manifest = """
            [
              {"Id":"good","FileName":"good.txt","FilePath":null,"ContentFile":"good.txt","IsUntitled":true,"CursorOffset":5,"ScrollOffset":10.0},
              {"Id":"missing","FileName":"missing.txt","FilePath":null,"ContentFile":"missing.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}
            ]
            """;
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(manifestPath)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(Path.Combine(_autoSaveDir, "good.txt"))).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.ReadAllText(Path.Combine(_autoSaveDir, "good.txt"))).Returns("recovered");

        var result = _sut.GetRecoveryEntries();

        result.Entries.Should().ContainSingle(entry => entry.Id == "good" && entry.Content == "recovered");
        result.Failures.Should().ContainSingle(message => message.Contains("missing.txt"));
    }

    [Fact]
    public void RemoveRecoveryEntries_RewritesManifestBeforeDeletingRecoveredContent()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var recoveredContentPath = Path.Combine(_autoSaveDir, "good.txt");
        var manifest = """
            [
              {"Id":"good","FileName":"good.txt","FilePath":null,"ContentFile":"good.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0},
              {"Id":"retry","FileName":"retry.txt","FilePath":null,"ContentFile":"retry.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}
            ]
            """;
        var writes = new List<string>();
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(manifestPath)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(recoveredContentPath)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, content) => writes.Add(content));

        _sut.GetRecoveryEntries();
        _sut.RemoveRecoveryEntries(new[] { "good" });

        writes.Should().Contain(content => content.Contains("\"retry\"") && !content.Contains("\"good\""));
        _fileSystem.Verify(fileSystem => fileSystem.MoveFile(
            It.IsAny<string>(), manifestPath, true), Times.Once);
        _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(recoveredContentPath), Times.Once);
    }

    [Fact]
    public void SaveNow_CurrentSnapshotSupersedesPendingEntryWithSameId()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var contentPath = Path.Combine(_autoSaveDir, "same.txt");
        var manifest = """
            [{"Id":"same","FileName":"notes.txt","FilePath":"C:\\notes.txt","ContentFile":"same.txt","IsUntitled":false,"CursorOffset":0,"ScrollOffset":0}]
            """;
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(manifestPath)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(contentPath)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.ReadAllText(contentPath)).Returns("old");

        _sut.GetRecoveryEntries();
        _sut.SaveNow(new[]
        {
            new AutoSaveEntry("same", "notes.txt", @"C:\notes.txt", "new", false)
        });

        _fileSystem.Verify(fileSystem => fileSystem.WriteAllText(
            It.Is<string>(path => path.StartsWith($"{contentPath}.")),
            "new"), Times.Once);
    }

    [Fact]
    public void GetRecoveryEntries_InvalidManifest_SurfacesFailureAndPreservesManifest()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        _fileSystem.Setup(fileSystem => fileSystem.FileExists(manifestPath)).Returns(true);
        _fileSystem.Setup(fileSystem => fileSystem.ReadAllText(manifestPath)).Returns("not json");

        var result = _sut.GetRecoveryEntries();
        _sut.MarkCleanShutdown();

        result.Entries.Should().BeEmpty();
        result.Failures.Should().ContainSingle(message => message.Contains("manifest"));
        _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(manifestPath), Times.Never);
    }

    [Fact]
    public async Task RunAutoSaveCycle_CapturesEntriesThroughDispatcher()
    {
        var providerCalls = 0;
        _sut.SetEntryProvider(() =>
        {
            providerCalls++;
            return new[] { new AutoSaveEntry("id", "file.txt", null, "content", true) };
        });

        await _sut.RunAutoSaveCycleAsync();

        providerCalls.Should().Be(1);
        _dispatcher.Verify(dispatcher => dispatcher.InvokeAsync(
            It.IsAny<Func<List<AutoSaveEntry>>>()), Times.Once);
    }

    [Fact]
    public async Task RunAutoSaveCycle_DoesNotOverlap()
    {
        using var writeStarted = new ManualResetEventSlim();
        using var allowWrite = new ManualResetEventSlim();
        var providerCalls = 0;
        _sut.SetEntryProvider(() =>
        {
            providerCalls++;
            return new[] { new AutoSaveEntry("id", "file.txt", null, "content", true) };
        });
        _fileSystem.Setup(fileSystem => fileSystem.WriteAllText(
                It.Is<string>(path => path.Contains("id.txt.")),
                "content"))
            .Callback(() =>
            {
                writeStarted.Set();
                allowWrite.Wait(TimeSpan.FromSeconds(5));
            });

        var firstCycle = _sut.RunAutoSaveCycleAsync();
        writeStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await _sut.RunAutoSaveCycleAsync();
        allowWrite.Set();
        await firstCycle;

        providerCalls.Should().Be(1);
    }

    [Fact]
    public void IsEnabled_DefaultsToTrue() => _sut.IsEnabled.Should().BeTrue();

    [Fact]
    public void IntervalSeconds_DefaultsTo60() => _sut.IntervalSeconds.Should().Be(60);
}
