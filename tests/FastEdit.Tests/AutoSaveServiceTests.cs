using System.IO;
using System.IO.Enumeration;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using FluentAssertions;
using Moq;

namespace FastEdit.Tests;

public class AutoSaveServiceTests
{
    private readonly Mock<IFileSystemService> _fileSystem = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly InlineDispatcherService _dispatcher = new();
    private readonly AutoSaveService _sut;
    private readonly string _autoSaveDir;

    public AutoSaveServiceTests()
    {
        _autoSaveDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FastEdit", "AutoSave");
        _settings.SetupGet(s => s.AutoSaveIntervalSeconds).Returns(60);
        _sut = new AutoSaveService(_fileSystem.Object, _settings.Object, _dispatcher);
    }

    [Fact]
    public void Constructor_UsesConfiguredInterval()
    {
        _sut.IntervalSeconds.Should().Be(60);
    }

    [Fact]
    public void SettingsChange_UpdatesRunningInterval()
    {
        _sut.Start();
        _settings.SetupGet(s => s.AutoSaveIntervalSeconds).Returns(15);

        _settings.Raise(s => s.AutoSaveIntervalChanged += null, EventArgs.Empty);

        _sut.IntervalSeconds.Should().Be(15);
        _sut.Stop();
    }

    [Fact]
    public void HasRecoveryFiles_NoDirectory_ReturnsFalse()
    {
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(false);

        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void HasRecoveryFiles_CleanShutdown_ReturnsFalse()
    {
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(true);

        _sut.HasRecoveryFiles().Should().BeFalse();
    }

    [Fact]
    public void HasRecoveryFiles_UncleanWithManifest_ReturnsTrue()
    {
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown"))).Returns(false);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { Path.Combine(_autoSaveDir, "manifest.json") });

        _sut.HasRecoveryFiles().Should().BeTrue();
    }

    [Fact]
    public void MarkCleanShutdown_DeletesOnlyActiveGeneration()
    {
        string? contentPath = null;
        string? manifestPath = null;
        string? manifestJson = null;
        string? retiredManifestPath = null;
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, _) =>
            {
                if (path.EndsWith(".txt", StringComparison.Ordinal))
                    contentPath = path;
                else if (Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal))
                {
                    manifestPath = path;
                    manifestJson = _;
                }
            });
        _sut.SaveNow(new[]
        {
            new AutoSaveEntry("id", "test.txt", null, "content", true)
        });
        _fileSystem.Setup(f => f.GetFiles(
                _autoSaveDir,
                It.Is<string>(pattern => pattern.EndsWith("*.txt", StringComparison.Ordinal)),
                false))
            .Returns(() => new[] { contentPath! });
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => path == manifestPath || path == contentPath);
        _fileSystem.Setup(f => f.ReadAllText(It.IsAny<string>()))
            .Returns(() => manifestJson!);
        _fileSystem.Setup(f => f.MoveFile(manifestPath!, It.IsAny<string>(), false))
            .Callback<string, string, bool>((_, destination, _) => retiredManifestPath = destination);

        _sut.MarkCleanShutdown().Should().BeTrue();

        _fileSystem.Verify(f => f.DeleteFile(contentPath!), Times.Once);
        _fileSystem.Verify(f => f.MoveFile(manifestPath!, It.IsAny<string>(), false), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(retiredManifestPath!), Times.Once);
        _fileSystem.Verify(f => f.WriteAllTextAtomic(
            Path.Combine(_autoSaveDir, ".clean-shutdown"),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void MarkCleanShutdown_ActiveGenerationCleanupFailure_ReturnsFalse()
    {
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        string? manifestPath = null;
        _fileSystem.Setup(f => f.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, _) =>
            {
                if (Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal))
                    manifestPath = path;
            });
        _sut.SaveNow(new[]
        {
            new AutoSaveEntry("id", "test.txt", null, "content", true)
        });
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => path == manifestPath);
        _fileSystem.Setup(f => f.MoveFile(manifestPath!, It.IsAny<string>(), false))
            .Throws(new IOException("unavailable"));

        _sut.MarkCleanShutdown().Should().BeFalse();

        _fileSystem.Verify(f => f.WriteAllTextAtomic(
            Path.Combine(_autoSaveDir, ".clean-shutdown"),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void MarkCleanShutdown_RemovesContentFromOlderSnapshotsInActiveGeneration()
    {
        string? manifestPath = null;
        string? manifestJson = null;
        var contentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) =>
            {
                if (path.EndsWith(".txt", StringComparison.Ordinal))
                    contentPaths.Add(path);
                else if (Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal))
                {
                    manifestPath = path;
                    manifestJson = content;
                }
            });
        _sut.SaveNow(new[]
        {
            new AutoSaveEntry("one", "one.txt", null, "one", true),
            new AutoSaveEntry("two", "two.txt", null, "two", true)
        });
        _sut.SaveNow(new[]
        {
            new AutoSaveEntry("one", "one.txt", null, "new one", true)
        });
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => path == manifestPath || contentPaths.Contains(path));
        _fileSystem.Setup(f => f.ReadAllText(manifestPath!)).Returns(() => manifestJson!);
        _fileSystem.Setup(f => f.GetFiles(
                _autoSaveDir,
                It.Is<string>(pattern => pattern.EndsWith("-*.txt", StringComparison.Ordinal)),
                false))
            .Returns(() => contentPaths.ToArray());

        _sut.MarkCleanShutdown().Should().BeTrue();

        foreach (var contentPath in contentPaths)
            _fileSystem.Verify(f => f.DeleteFile(contentPath), Times.Once);
    }

    [Fact]
    public void Start_RetriesQuarantinedGenerationCleanup()
    {
        var retiredManifest = Path.Combine(
            _autoSaveDir,
            ".retired-operation-manifest-old.json");
        var contentPath = Path.Combine(_autoSaveDir, "old-content.txt");
        var manifest = """[{"Id":"old","FileName":"old.txt","FilePath":null,"ContentFile":"old-content.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}]""";
        var deleteAttempts = 0;
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, ".retired-*.json", false))
            .Returns(new[] { retiredManifest });
        _fileSystem.Setup(f => f.ReadAllText(retiredManifest)).Returns(manifest);
        _fileSystem.Setup(f => f.FileExists(contentPath)).Returns(true);
        _fileSystem.Setup(f => f.DeleteFile(contentPath))
            .Callback(() =>
            {
                deleteAttempts++;
                if (deleteAttempts == 1)
                    throw new IOException("locked");
            });

        _sut.Start();
        _sut.Stop();
        _sut.Start();
        _sut.Stop();

        _fileSystem.Verify(f => f.DeleteFile(contentPath), Times.Exactly(2));
        _fileSystem.Verify(f => f.DeleteFile(retiredManifest), Times.Once);
    }

    [Fact]
    public void Start_RetryCleanupIncludesOlderSnapshotFilesFromGenerationPrefix()
    {
        var retiredManifest = Path.Combine(
            _autoSaveDir,
            ".retired-operation-manifest-generation.json");
        var currentContent = Path.Combine(_autoSaveDir, "generation-current.txt");
        var staleContent = Path.Combine(_autoSaveDir, "generation-stale.txt");
        var manifest = """[{"Id":"current","FileName":"current.txt","FilePath":null,"ContentFile":"generation-current.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}]""";
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, ".retired-*.json", false))
            .Returns(new[] { retiredManifest });
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "generation-*.txt", false))
            .Returns(new[] { currentContent, staleContent });
        _fileSystem.Setup(f => f.ReadAllText(retiredManifest)).Returns(manifest);
        _fileSystem.Setup(f => f.FileExists(currentContent)).Returns(true);
        _fileSystem.Setup(f => f.FileExists(staleContent)).Returns(true);

        _sut.Start();
        _sut.Stop();

        _fileSystem.Verify(f => f.DeleteFile(currentContent), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(staleContent), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(retiredManifest), Times.Once);
    }

    [Fact]
    public void SaveNow_WritesContentBeforeAtomicManifest()
    {
        var operations = new List<string>();
        _fileSystem.Setup(f => f.WriteAllTextAtomic(
                It.Is<string>(path => path.EndsWith("-id1.txt", StringComparison.Ordinal)),
                "content here"))
            .Callback(() => operations.Add("content"));
        _fileSystem.Setup(f => f.WriteAllTextAtomic(
                It.Is<string>(path =>
                    Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal)),
                It.IsAny<string>()))
            .Callback(() => operations.Add("manifest"));

        _sut.SaveNow(new[]
        {
            new AutoSaveEntry("id1", "test.txt", @"C:\test.txt", "content here", false, 10, 20.5)
        });

        operations.Should().Equal("content", "manifest");
    }

    [Fact]
    public void SaveNow_ContentWriteFailure_PreservesPreviousManifest()
    {
        _fileSystem.Setup(f => f.WriteAllTextAtomic(
                It.Is<string>(path => path.EndsWith("-id1.txt", StringComparison.Ordinal)),
                "content"))
            .Throws(new IOException("disk full"));

        var action = () => _sut.SaveNow(new[]
        {
            new AutoSaveEntry("id1", "test.txt", null, "content", true)
        });

        action.Should().Throw<IOException>();
        _fileSystem.Verify(f => f.WriteAllTextAtomic(
            It.Is<string>(path =>
                Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal)),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void SaveNow_SeparateServiceInstancesUseDistinctGenerations()
    {
        var writtenPaths = new List<string>();
        _fileSystem.Setup(f => f.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, _) => writtenPaths.Add(path));
        var second = new AutoSaveService(
            _fileSystem.Object,
            _settings.Object,
            new InlineDispatcherService());

        _sut.SaveNow(new[]
        {
            new AutoSaveEntry("same", "one.txt", null, "one", true)
        });
        second.SaveNow(new[]
        {
            new AutoSaveEntry("same", "two.txt", null, "two", true)
        });

        writtenPaths.Where(path => path.EndsWith(".json", StringComparison.Ordinal))
            .Should().HaveCount(2).And.OnlyHaveUniqueItems();
        writtenPaths.Where(path => path.EndsWith(".txt", StringComparison.Ordinal))
            .Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task TimerRun_CapturesEntriesOnDispatcher()
    {
        var capturedOnDispatcher = false;
        _sut.SetEntryProvider(() =>
        {
            capturedOnDispatcher = _dispatcher.IsInvoking;
            return new[] { new AutoSaveEntry("id", "test.txt", null, "content", true) };
        });
        _sut.Start();

        await _sut.RunAutoSaveAsync();

        capturedOnDispatcher.Should().BeTrue();
        _sut.Stop();
    }

    [Fact]
    public async Task TimerRun_PendingRecoveryWritesSeparateGeneration()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, ".clean-shutdown")))
            .Returns(false);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _sut.SetEntryProvider(() =>
            new[] { new AutoSaveEntry("new", "new.txt", null, "new", true) });
        _sut.Start();

        await _sut.RunAutoSaveAsync();

        _fileSystem.Verify(f => f.WriteAllTextAtomic(
            manifestPath,
            It.IsAny<string>()), Times.Never);
        _fileSystem.Verify(f => f.WriteAllTextAtomic(
            It.Is<string>(path =>
                Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal) &&
                path.EndsWith(".json", StringComparison.Ordinal)),
            It.IsAny<string>()), Times.Once);
        _sut.Stop();
    }

    [Fact]
    public async Task TimerRun_OverlappingInvocation_IsSkipped()
    {
        using var writeStarted = new ManualResetEventSlim();
        using var releaseWrite = new ManualResetEventSlim();
        var providerCalls = 0;
        _sut.SetEntryProvider(() =>
        {
            Interlocked.Increment(ref providerCalls);
            return new[] { new AutoSaveEntry("id", "test.txt", null, "content", true) };
        });
        _fileSystem.Setup(f => f.WriteAllTextAtomic(
                It.Is<string>(path => path.EndsWith("-id.txt", StringComparison.Ordinal)),
                "content"))
            .Callback(() =>
            {
                writeStarted.Set();
                releaseWrite.Wait(TimeSpan.FromSeconds(5));
            });
        _sut.Start();

        var firstRun = _sut.RunAutoSaveAsync();
        writeStarted.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        await _sut.RunAutoSaveAsync();
        releaseWrite.Set();
        await firstRun;

        providerCalls.Should().Be(1);
        _sut.Stop();
    }

    [Fact]
    public void GetRecoveryEntries_ValidManifest_ReturnsEntries()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var manifest = """[{"Id":"id1","FileName":"test.txt","FilePath":"C:\\test.txt","ContentFile":"id1.txt","IsUntitled":false,"CursorOffset":5,"ScrollOffset":10.0}]""";
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _fileSystem.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "id1.txt"))).Returns(true);
        _fileSystem.Setup(f => f.ReadAllText(Path.Combine(_autoSaveDir, "id1.txt"))).Returns("recovered content");

        var result = _sut.GetRecoveryEntries();

        result.Success.Should().BeTrue();
        result.Entries.Should().ContainSingle();
        result.Entries[0].Content.Should().Be("recovered content");
        result.Entries[0].CursorOffset.Should().Be(5);
    }

    [Fact]
    public void GetRecoveryEntries_InvalidManifest_ReturnsExplicitFailure()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _fileSystem.Setup(f => f.ReadAllText(manifestPath)).Returns("not json");

        var result = _sut.GetRecoveryEntries();

        result.Success.Should().BeFalse();
        result.Entries.Should().BeEmpty();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetRecoveryEntries_MissingContentFile_ReturnsExplicitFailure()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var manifest = """[{"Id":"id1","FileName":"test.txt","FilePath":null,"ContentFile":"id1.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}]""";
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _fileSystem.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "id1.txt"))).Returns(false);

        var result = _sut.GetRecoveryEntries();

        result.Success.Should().BeFalse();
        result.Entries.Should().BeEmpty();
    }

    [Fact]
    public void GetRecoveryEntries_DamagedGenerationStillReturnsIntactEntries()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var manifest = """
            [
              {"Id":"missing","FileName":"missing.txt","FilePath":null,"ContentFile":"missing.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0},
              {"Id":"valid","FileName":"valid.txt","FilePath":null,"ContentFile":"valid.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}
            ]
            """;
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _fileSystem.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(f => f.FileExists(Path.Combine(_autoSaveDir, "valid.txt"))).Returns(true);
        _fileSystem.Setup(f => f.ReadAllText(Path.Combine(_autoSaveDir, "valid.txt"))).Returns("valid");

        var result = _sut.GetRecoveryEntries();

        result.Success.Should().BeFalse();
        result.Entries.Should().ContainSingle(entry =>
            entry.Id.EndsWith(":valid", StringComparison.Ordinal));
    }

    [Fact]
    public void GetRecoveryEntries_DivergentGenerationsRemainSeparate()
    {
        var firstManifestPath = Path.Combine(_autoSaveDir, "manifest-first.json");
        var secondManifestPath = Path.Combine(_autoSaveDir, "manifest-second.json");
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { firstManifestPath, secondManifestPath });
        _fileSystem.Setup(f => f.GetLastWriteTime(firstManifestPath))
            .Returns(new DateTime(2026, 1, 1));
        _fileSystem.Setup(f => f.GetLastWriteTime(secondManifestPath))
            .Returns(new DateTime(2026, 1, 2));
        SetupRecoveryGeneration(firstManifestPath, "first.txt", "first");
        SetupRecoveryGeneration(secondManifestPath, "second.txt", "second");

        var result = _sut.GetRecoveryEntries();

        result.Success.Should().BeTrue();
        result.Entries.Select(entry => entry.Content)
            .Should().BeEquivalentTo("first", "second");
        result.Entries.Select(entry => entry.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ClearRecoveryFiles_DoesNotDeleteLiveGeneration()
    {
        var crashedManifestPath = Path.Combine(_autoSaveDir, "manifest-crashed.json");
        var liveManifestPath = Path.Combine(_autoSaveDir, "manifest-live.json");
        var liveMarkerPath = Path.Combine(_autoSaveDir, "active-live.lock");
        var crashedContentPath = SetupRecoveryGeneration(
            crashedManifestPath,
            "crashed.txt",
            "crashed");
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { crashedManifestPath, liveManifestPath });
        _fileSystem.Setup(f => f.FileExists(liveMarkerPath)).Returns(true);
        _fileSystem.Setup(f => f.ReadAllText(liveMarkerPath))
            .Returns($"{process.Id}|{process.StartTime.ToUniversalTime().Ticks}");
        _sut.GetRecoveryEntries();

        _sut.ClearRecoveryFiles().Should().BeTrue();

        _fileSystem.Verify(f => f.DeleteFile(crashedContentPath), Times.Once);
        _fileSystem.Verify(f => f.MoveFile(crashedManifestPath, It.IsAny<string>(), false), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(crashedManifestPath), Times.Never);
        _fileSystem.Verify(f => f.DeleteFile(liveManifestPath), Times.Never);
    }

    [Fact]
    public void MarkCleanShutdown_UnresolvedRecoveryPreservesRetainedGeneration()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _sut.Start();
        _sut.Stop();

        _sut.MarkCleanShutdown().Should().BeTrue();

        _fileSystem.Verify(f => f.DeleteFile(manifestPath), Times.Never);
        _fileSystem.Verify(f => f.WriteAllTextAtomic(
            Path.Combine(_autoSaveDir, ".clean-shutdown"),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RecordRecoveredEntries_SkipsOnlyResolvedEntriesFromOriginalManifest()
    {
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var resolvedPath = Path.Combine(_autoSaveDir, "resolved.json");
        var manifest = """
            [
              {"Id":"one","FileName":"one.txt","FilePath":null,"ContentFile":"one.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0},
              {"Id":"two","FileName":"two.txt","FilePath":null,"ContentFile":"two.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}
            ]
            """;
        string? resolvedJson = null;
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _fileSystem.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        foreach (var id in new[] { "one", "two" })
        {
            var contentPath = Path.Combine(_autoSaveDir, $"{id}.txt");
            _fileSystem.Setup(f => f.FileExists(contentPath)).Returns(true);
            _fileSystem.Setup(f => f.ReadAllText(contentPath)).Returns(id);
        }
        _fileSystem.Setup(f => f.FileExists(resolvedPath))
            .Returns(() => resolvedJson != null);
        _fileSystem.Setup(f => f.ReadAllText(resolvedPath))
            .Returns(() => resolvedJson!);
        _fileSystem.Setup(f => f.WriteAllTextAtomic(resolvedPath, It.IsAny<string>()))
            .Callback<string, string>((_, json) => resolvedJson = json);
        var firstRecovery = _sut.GetRecoveryEntries();
        var recoveredId = firstRecovery.Entries.Single(entry =>
            entry.Id.EndsWith(":one", StringComparison.Ordinal)).Id;

        _sut.RecordRecoveredEntries(new[] { recoveredId }).Should().BeTrue();
        var nextRecovery = _sut.GetRecoveryEntries();

        nextRecovery.Success.Should().BeTrue();
        nextRecovery.Entries.Should().ContainSingle(entry =>
            entry.Id.EndsWith(":two", StringComparison.Ordinal));
        _sut.ClearRecoveryFiles().Should().BeTrue();
        _fileSystem.Verify(
            f => f.DeleteFile(Path.Combine(_autoSaveDir, "one.txt")),
            Times.Once);
        _fileSystem.Verify(
            f => f.DeleteFile(Path.Combine(_autoSaveDir, "two.txt")),
            Times.Once);
    }

    [Fact]
    public void ClearRecoveryFiles_ContentDeleteFailure_LeavesOnlyQuarantinedManifest()
    {
        var contentPath = Path.Combine(_autoSaveDir, "id1.txt");
        var manifestPath = Path.Combine(_autoSaveDir, "manifest.json");
        var manifest = """[{"Id":"id1","FileName":"test.txt","FilePath":null,"ContentFile":"id1.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}]""";
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            manifestPath,
            contentPath
        };
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { manifestPath });
        _fileSystem.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => existingFiles.Contains(path));
        _fileSystem.Setup(f => f.ReadAllText(contentPath)).Returns("content");
        _fileSystem.Setup(f => f.MoveFile(
                It.IsAny<string>(),
                It.IsAny<string>(),
                false))
            .Callback<string, string, bool>((source, destination, _) =>
            {
                existingFiles.Remove(source);
                existingFiles.Add(destination);
            });
        _fileSystem.Setup(f => f.DeleteFile(contentPath))
            .Throws(new IOException("locked"));
        _sut.GetRecoveryEntries();

        _sut.ClearRecoveryFiles().Should().BeFalse();

        _fileSystem.Verify(f => f.MoveFile(manifestPath, It.IsAny<string>(), false), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(manifestPath), Times.Never);
    }

    [Fact]
    public void FailedFirstSnapshot_ShutdownQuarantinesAndNextStartRetriesManifestlessContent()
    {
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? activeContentPath = null;
        string? retiredManifestPath = null;
        var manifestWriteAttempts = 0;
        var contentDeleteAttempts = 0;
        var cleanupOperations = new List<string>();

        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => existingFiles.Contains(path));
        _fileSystem.Setup(f => f.ReadAllText(It.IsAny<string>()))
            .Returns((string path) => fileContents[path]);
        _fileSystem.Setup(f => f.GetFiles(
                _autoSaveDir,
                It.IsAny<string>(),
                false))
            .Returns((string directory, string pattern, bool recursive) =>
                existingFiles
                    .Where(path => FileSystemName.MatchesSimpleExpression(
                        pattern,
                        Path.GetFileName(path),
                        ignoreCase: true))
                    .ToArray());
        _fileSystem.Setup(f => f.WriteAllTextAtomic(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string>((path, content) =>
            {
                if (path.EndsWith(".txt", StringComparison.Ordinal))
                    activeContentPath = path;
                if (path.Contains("manifest-", StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    manifestWriteAttempts++ == 0)
                {
                    throw new IOException("manifest write failed");
                }
                if (Path.GetFileName(path).StartsWith("cleanup-", StringComparison.OrdinalIgnoreCase))
                    cleanupOperations.Add("intent");

                existingFiles.Add(path);
                fileContents[path] = content;
            });
        _fileSystem.Setup(f => f.MoveFile(
                It.IsAny<string>(),
                It.IsAny<string>(),
                false))
            .Callback<string, string, bool>((source, destination, _) =>
            {
                existingFiles.Remove(source);
                existingFiles.Add(destination);
                fileContents[destination] = fileContents[source];
                fileContents.Remove(source);
                retiredManifestPath = destination;
                cleanupOperations.Add("quarantine");
            });
        _fileSystem.Setup(f => f.DeleteFile(It.IsAny<string>()))
            .Callback<string>(path =>
            {
                if (path == activeContentPath && contentDeleteAttempts++ == 0)
                    throw new IOException("content locked");

                existingFiles.Remove(path);
                fileContents.Remove(path);
            });

        var firstRun = () => _sut.SaveNow(new[]
        {
            new AutoSaveEntry("orphan", "orphan.txt", null, "recover me", true)
        });
        firstRun.Should().Throw<IOException>();

        _sut.MarkCleanShutdown().Should().BeFalse();
        cleanupOperations.Take(2).Should().Equal("intent", "quarantine");
        existingFiles.Should().Contain(activeContentPath!);
        existingFiles.Should().Contain(retiredManifestPath!);

        var nextRun = new AutoSaveService(
            _fileSystem.Object,
            _settings.Object,
            new InlineDispatcherService());
        nextRun.Start();
        nextRun.Stop();

        existingFiles.Should().NotContain(activeContentPath!);
        existingFiles.Should().NotContain(retiredManifestPath!);
        contentDeleteAttempts.Should().Be(2);
    }

    [Fact]
    public void CrashDuringFirstSnapshot_ExposesManifestlessContentForRecovery()
    {
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? activeContentPath = null;
        string? activeMarkerPath = null;
        var manifestWriteAttempts = 0;

        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => existingFiles.Contains(path));
        _fileSystem.Setup(f => f.ReadAllText(It.IsAny<string>()))
            .Returns((string path) => fileContents[path]);
        _fileSystem.Setup(f => f.GetLastWriteTime(It.IsAny<string>()))
            .Returns(DateTime.UtcNow);
        _fileSystem.Setup(f => f.GetFiles(
                _autoSaveDir,
                It.IsAny<string>(),
                false))
            .Returns((string directory, string pattern, bool recursive) =>
                existingFiles
                    .Where(path => FileSystemName.MatchesSimpleExpression(
                        pattern,
                        Path.GetFileName(path),
                        ignoreCase: true))
                    .ToArray());
        _fileSystem.Setup(f => f.WriteAllTextAtomic(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string>((path, content) =>
            {
                if (path.EndsWith(".txt", StringComparison.Ordinal))
                    activeContentPath = path;
                if (path.EndsWith(".lock", StringComparison.Ordinal))
                    activeMarkerPath = path;
                if (Path.GetFileName(path).StartsWith("manifest-", StringComparison.OrdinalIgnoreCase) &&
                    manifestWriteAttempts++ == 0)
                {
                    throw new IOException("manifest write failed");
                }

                existingFiles.Add(path);
                fileContents[path] = content;
            });
        _fileSystem.Setup(f => f.DeleteFile(It.IsAny<string>()))
            .Callback<string>(path =>
            {
                existingFiles.Remove(path);
                fileContents.Remove(path);
            });

        _sut.Start();
        var firstRun = () => _sut.SaveNow(new[]
        {
            new AutoSaveEntry("orphan", "orphan.txt", null, "recover me", true)
        });
        firstRun.Should().Throw<IOException>();
        _sut.Stop();
        fileContents[activeMarkerPath!] = $"{int.MaxValue}|0";

        var nextRun = new AutoSaveService(
            _fileSystem.Object,
            _settings.Object,
            new InlineDispatcherService());

        nextRun.HasRecoveryFiles().Should().BeTrue();
        var recovery = nextRun.GetRecoveryEntries();

        recovery.Success.Should().BeTrue();
        recovery.Entries.Should().ContainSingle(entry => entry.Content == "recover me");
        existingFiles.Should().Contain(activeContentPath!);
        _fileSystem.Verify(f => f.DeleteFile(activeContentPath!), Times.Never);
    }

    [Fact]
    public void ClearRecoveryFiles_LaterQuarantineFailureLeavesThatGenerationIntact()
    {
        var firstManifest = Path.Combine(_autoSaveDir, "manifest-first.json");
        var secondManifest = Path.Combine(_autoSaveDir, "manifest-second.json");
        var firstContent = SetupRecoveryGeneration(firstManifest, "first.txt", "first");
        var secondContent = SetupRecoveryGeneration(secondManifest, "second.txt", "second");
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { firstManifest, secondManifest });
        _sut.GetRecoveryEntries();
        _fileSystem.Setup(f => f.MoveFile(secondManifest, It.IsAny<string>(), false))
            .Throws(new IOException("locked"));

        _sut.ClearRecoveryFiles().Should().BeFalse();

        _fileSystem.Verify(f => f.MoveFile(firstManifest, It.IsAny<string>(), false), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(firstContent), Times.Once);
        _fileSystem.Verify(f => f.DeleteFile(secondContent), Times.Never);
        _fileSystem.Verify(f => f.DeleteFile(secondManifest), Times.Never);
    }

    [Fact]
    public void CompleteRecovery_VerifiesReplacementBeforeRetiringSourceGeneration()
    {
        var sourceManifestPath = Path.Combine(_autoSaveDir, "manifest-crashed.json");
        var sourceContentPath = Path.Combine(_autoSaveDir, "crashed.txt");
        var sourceManifest = """[{"Id":"old","FileName":"recovered.txt","FilePath":null,"ContentFile":"crashed.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}]""";
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [sourceManifestPath] = sourceManifest,
            [sourceContentPath] = "recovered"
        };
        var operations = new List<string>();
        var activeManifestPaths = new List<string>();
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { sourceManifestPath });
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => files.ContainsKey(path));
        _fileSystem.Setup(f => f.ReadAllText(It.IsAny<string>()))
            .Returns((string path) => files[path]);
        _fileSystem.Setup(f => f.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) =>
            {
                files[path] = content;
                if (Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal) &&
                    path != sourceManifestPath)
                {
                    activeManifestPaths.Add(path);
                    operations.Add("replacement-manifest");
                }
            });
        _fileSystem.Setup(f => f.MoveFile(
                It.IsAny<string>(),
                It.IsAny<string>(),
                false))
            .Callback<string, string, bool>((source, destination, _) =>
            {
                if (source == sourceManifestPath)
                    operations.Add("source-manifest-quarantined");
                files[destination] = files[source];
                files.Remove(source);
            });
        _fileSystem.Setup(f => f.DeleteFile(It.IsAny<string>()))
            .Callback<string>(path => files.Remove(path));
        var recovery = _sut.GetRecoveryEntries();
        var replacement = new AutoSaveEntry(
            "tab-stable",
            "recovered.txt",
            null,
            "recovered",
            true);

        _sut.CompleteRecovery(
            new[] { replacement },
            recovery.Entries.Select(entry => entry.Id),
            allEntriesRecovered: true).Should().BeTrue();
        _sut.SaveNow(new[] { replacement with { Content = "newer" } });

        operations.Should().ContainInOrder(
            "replacement-manifest",
            "source-manifest-quarantined");
        activeManifestPaths.Should().NotBeEmpty();
        activeManifestPaths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Should().ContainSingle();
        files.Should().NotContainKey(sourceManifestPath);
        files.Should().ContainKey(activeManifestPaths[0]);
    }

    [Fact]
    public void CompleteRecovery_VerificationFailureRetainsSourceGeneration()
    {
        var sourceManifestPath = Path.Combine(_autoSaveDir, "manifest-crashed.json");
        var sourceContentPath = Path.Combine(_autoSaveDir, "crashed.txt");
        var sourceManifest = """[{"Id":"old","FileName":"recovered.txt","FilePath":null,"ContentFile":"crashed.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}]""";
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { sourceManifestPath });
        _fileSystem.Setup(f => f.FileExists(sourceContentPath)).Returns(true);
        _fileSystem.Setup(f => f.ReadAllText(sourceManifestPath)).Returns(sourceManifest);
        _fileSystem.Setup(f => f.ReadAllText(sourceContentPath)).Returns("recovered");
        var recovery = _sut.GetRecoveryEntries();
        _fileSystem.Setup(f => f.ReadAllText(
                It.Is<string>(path =>
                    Path.GetFileName(path).StartsWith("manifest-", StringComparison.Ordinal) &&
                    path != sourceManifestPath)))
            .Returns("not json");

        _sut.CompleteRecovery(
            new[]
            {
                new AutoSaveEntry(
                    "tab-stable",
                    "recovered.txt",
                    null,
                    "recovered",
                    true)
            },
            recovery.Entries.Select(entry => entry.Id),
            allEntriesRecovered: true).Should().BeFalse();

        _fileSystem.Verify(f => f.MoveFile(
            sourceManifestPath,
            It.IsAny<string>(),
            false), Times.Never);
    }

    [Fact]
    public void CompleteRecovery_PartialRecoveryPersistsReplacementAndResolvesOnlyRecoveredSource()
    {
        var sourceManifestPath = Path.Combine(_autoSaveDir, "manifest-crashed.json");
        var firstContentPath = Path.Combine(_autoSaveDir, "first.txt");
        var secondContentPath = Path.Combine(_autoSaveDir, "second.txt");
        var resolvedPath = Path.Combine(_autoSaveDir, "resolved.json");
        var sourceManifest = """
            [
              {"Id":"first","FileName":"first.txt","FilePath":null,"ContentFile":"first.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0},
              {"Id":"second","FileName":"second.txt","FilePath":null,"ContentFile":"second.txt","IsUntitled":true,"CursorOffset":0,"ScrollOffset":0}
            ]
            """;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [sourceManifestPath] = sourceManifest,
            [firstContentPath] = "first",
            [secondContentPath] = "second"
        };
        _fileSystem.Setup(f => f.DirectoryExists(_autoSaveDir)).Returns(true);
        _fileSystem.Setup(f => f.GetFiles(_autoSaveDir, "manifest*.json", false))
            .Returns(new[] { sourceManifestPath });
        _fileSystem.Setup(f => f.FileExists(It.IsAny<string>()))
            .Returns((string path) => files.ContainsKey(path));
        _fileSystem.Setup(f => f.ReadAllText(It.IsAny<string>()))
            .Returns((string path) => files[path]);
        _fileSystem.Setup(f => f.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((path, content) => files[path] = content);
        var recovery = _sut.GetRecoveryEntries();
        var recoveredEntry = recovery.Entries.Single(entry =>
            entry.Id.EndsWith(":first", StringComparison.Ordinal));

        _sut.CompleteRecovery(
            new[]
            {
                new AutoSaveEntry(
                    "tab-stable",
                    "first.txt",
                    null,
                    "first",
                    true)
            },
            new[] { recoveredEntry.Id },
            allEntriesRecovered: false).Should().BeTrue();
        var nextRecovery = _sut.GetRecoveryEntries();

        nextRecovery.Entries.Should().ContainSingle(entry =>
            entry.Id.EndsWith(":second", StringComparison.Ordinal));
        files.Should().ContainKey(sourceManifestPath);
        files.Should().ContainKey(resolvedPath);
        files[resolvedPath].Should().Contain("\"first\"");
        _fileSystem.Verify(f => f.MoveFile(
            sourceManifestPath,
            It.IsAny<string>(),
            false), Times.Never);
    }

    private string SetupRecoveryGeneration(
        string manifestPath,
        string contentFile,
        string content)
    {
        var contentPath = Path.Combine(_autoSaveDir, contentFile);
        var manifest = $$"""[{"Id":"shared","FileName":"test.txt","FilePath":"C:\\test.txt","ContentFile":"{{contentFile}}","IsUntitled":false,"CursorOffset":0,"ScrollOffset":0}]""";
        _fileSystem.Setup(f => f.ReadAllText(manifestPath)).Returns(manifest);
        _fileSystem.Setup(f => f.FileExists(contentPath)).Returns(true);
        _fileSystem.Setup(f => f.ReadAllText(contentPath)).Returns(content);
        return contentPath;
    }

    private sealed class InlineDispatcherService : IDispatcherService
    {
        public bool IsInvoking { get; private set; }

        public void Invoke(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            IsInvoking = true;
            try
            {
                return Task.FromResult(func());
            }
            finally
            {
                IsInvoking = false;
            }
        }

        public bool CheckAccess() => true;
    }
}
