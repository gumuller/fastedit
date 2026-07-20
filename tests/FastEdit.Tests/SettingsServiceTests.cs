using System.IO;
using FastEdit.Services;
using FastEdit.Services.Interfaces;
using Moq;

namespace FastEdit.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void Save_UsesAtomicWrite()
    {
        var fileSystem = new Mock<IFileSystemService>();
        var appDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sut = new SettingsService(fileSystem.Object, appDataPath);

        sut.ThemeName = "Light";

        fileSystem.Verify(f => f.WriteAllTextAtomic(
            Path.Combine(appDataPath, "settings.json"),
            It.Is<string>(json => json.Contains("\"ThemeName\": \"Light\""))),
            Times.Once);
    }

    [Fact]
    public void AutoSaveIntervalChange_RaisesEventAfterPersistence()
    {
        var fileSystem = new Mock<IFileSystemService>();
        var appDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sut = new SettingsService(fileSystem.Object, appDataPath);
        var raised = false;
        sut.AutoSaveIntervalChanged += (_, _) => raised = true;

        sut.AutoSaveIntervalSeconds = 12;

        Assert.True(raised);
        Assert.Equal(12, sut.AutoSaveIntervalSeconds);
    }

    [Fact]
    public void AutoSaveIntervalChange_PersistenceFailureRestoresPreviousValue()
    {
        var fileSystem = new Mock<IFileSystemService>();
        var appDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sut = new SettingsService(fileSystem.Object, appDataPath);
        var raised = false;
        sut.AutoSaveIntervalChanged += (_, _) => raised = true;
        fileSystem.Setup(f => f.WriteAllTextAtomic(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new IOException("disk full"));

        Assert.Throws<IOException>(() => sut.AutoSaveIntervalSeconds = 12);

        Assert.Equal(30, sut.AutoSaveIntervalSeconds);
        Assert.False(raised);
    }

    [Fact]
    public void StaleOrdinarySettingsWriterPreservesPublishedShutdownSession()
    {
        var appDataPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDataPath);
        try
        {
            var fileSystem = new FileSystemService();
            var staleWriter = new SettingsService(fileSystem, appDataPath);
            var publisher = new SettingsService(fileSystem, appDataPath);
            var session = new ShutdownSessionState(
                new[]
                {
                    new SessionFile
                    {
                        FilePath = "Untitled-1",
                        FileName = "Untitled-1",
                        TabIdentity = "owned",
                        SnapshotOwner = "publisher",
                        IsUntitled = true,
                        SnapshotGeneration = "generation",
                        SnapshotFile = "tab-owned.txt"
                    }
                },
                0);

            publisher.PublishShutdownSession(session);
            staleWriter.ThemeName = "Light";

            var reader = new SettingsService(fileSystem, appDataPath);
            var persisted = reader.ReadShutdownSession();
            Assert.Equal("Light", reader.ThemeName);
            Assert.Equal("owned", Assert.Single(persisted.Files).TabIdentity);
            Assert.Equal("generation", persisted.Files[0].SnapshotGeneration);
        }
        finally
        {
            Directory.Delete(appDataPath, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentShutdownPublishersSerializeWholeSessionPublication()
    {
        var appDataPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDataPath);
        try
        {
            var fileSystem = new FileSystemService();
            var first = new SettingsService(fileSystem, appDataPath);
            var second = new SettingsService(fileSystem, appDataPath);
            var start = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var publishers = Enumerable.Range(0, 20)
                .Select(index => Task.Run(async () =>
                {
                    await start.Task;
                    var owner = index % 2 == 0 ? first : second;
                    owner.PublishShutdownSession(
                        new ShutdownSessionState(
                            new[]
                            {
                                new SessionFile
                                {
                                    FilePath = $"Untitled-{index}",
                                    FileName = $"Untitled-{index}",
                                    TabIdentity = $"identity-{index}",
                                    SnapshotOwner = $"publisher-{index}",
                                    IsUntitled = true,
                                    SnapshotGeneration = $"generation-{index}",
                                    SnapshotFile = $"tab-identity-{index}.txt"
                                }
                            },
                            index));
                }))
                .ToArray();

            start.SetResult();
            await Task.WhenAll(publishers);

            var reader = new SettingsService(fileSystem, appDataPath);
            var persisted = reader.ReadShutdownSession();
            Assert.Equal(20, persisted.Files.Count);
            Assert.Equal(
                20,
                persisted.Files
                    .Select(file => file.SnapshotOwner)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            foreach (var file in persisted.Files)
            {
                var suffix = int.Parse(file.TabIdentity!["identity-".Length..]);
                Assert.Equal($"generation-{suffix}", file.SnapshotGeneration);
                Assert.Equal($"Untitled-{suffix}", file.FileName);
            }
        }
        finally
        {
            Directory.Delete(appDataPath, recursive: true);
        }
    }
}
