using System.IO;
using FastEdit.Infrastructure;
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
                    },
                    new SessionFile
                    {
                        FilePath = "Untitled-2",
                        FileName = "Untitled-2",
                        TabIdentity = "owned-2",
                        SnapshotOwner = "publisher",
                        IsUntitled = true,
                        IsActive = true,
                        SnapshotGeneration = "generation",
                        SnapshotFile = "tab-owned-2.txt"
                    }
                },
                1);

            publisher.PublishShutdownSession(session);
            staleWriter.ThemeName = "Light";

            var reader = new SettingsService(fileSystem, appDataPath);
            var persisted = reader.ReadShutdownSession();
            Assert.Equal("Light", reader.ThemeName);
            Assert.Equal(2, persisted.Files.Count);
            Assert.Equal("owned", persisted.Files[0].TabIdentity);
            Assert.Equal("generation", persisted.Files[0].SnapshotGeneration);
            Assert.Equal("owned-2", persisted.Files[1].TabIdentity);
            Assert.Equal(1, persisted.ActiveTabIndex);
        }
        finally
        {
            Directory.Delete(appDataPath, recursive: true);
        }
    }

    [Fact]
    public void PublishShutdownSession_MergesPendingGeometryIntoLatestDurableSettings()
    {
        var appDataPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDataPath);
        try
        {
            var fileSystem = new FileSystemService();
            var seed = new SettingsService(fileSystem, appDataPath)
            {
                WindowLeft = 10,
                WindowTop = 20,
                WindowWidth = 800,
                WindowHeight = 600,
                WindowMaximized = false
            };
            seed.PublishShutdownSession(new ShutdownSessionState(
                new[]
                {
                    new SessionFile
                    {
                        FilePath = "old.txt",
                        TabIdentity = "old",
                        SnapshotOwner = "old-owner"
                    }
                },
                0));

            var publisher = new SettingsService(fileSystem, appDataPath)
            {
                WindowLeft = 110,
                WindowTop = 120,
                WindowWidth = 1280,
                WindowHeight = 720,
                WindowMaximized = true
            };
            var unrelatedWriter = new SettingsService(fileSystem, appDataPath);
            unrelatedWriter.ThemeName = "Light";

            publisher.PublishShutdownSession(new ShutdownSessionState(
                new[]
                {
                    new SessionFile
                    {
                        FilePath = "new.txt",
                        TabIdentity = "new",
                        SnapshotOwner = "new-owner"
                    }
                },
                0,
                new[] { "old-owner" }));

            var reloaded = new SettingsService(fileSystem, appDataPath);
            var session = reloaded.ReadShutdownSession();
            Assert.Equal(110, reloaded.WindowLeft);
            Assert.Equal(120, reloaded.WindowTop);
            Assert.Equal(1280, reloaded.WindowWidth);
            Assert.Equal(720, reloaded.WindowHeight);
            Assert.True(reloaded.WindowMaximized);
            Assert.Equal("Light", reloaded.ThemeName);
            Assert.Equal("new", Assert.Single(session.Files).TabIdentity);
        }
        finally
        {
            Directory.Delete(appDataPath, recursive: true);
        }
    }

    [Fact]
    public void LegacyStorePublishesThroughVersionedDurableSessionApi()
    {
        var appDataPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(appDataPath);
        try
        {
            var fileSystem = new FileSystemService();
            var settings = new SettingsService(fileSystem, appDataPath);
            settings.PublishShutdownSession(new ShutdownSessionState(
                new[]
                {
                    new SessionFile
                    {
                        FilePath = "old.txt",
                        TabIdentity = "old",
                        SnapshotOwner = "old-owner"
                    }
                },
                0));
            var legacy = new LegacyShutdownSessionStore(settings);

            legacy.PublishShutdownSession(new ShutdownSessionState(
                new[]
                {
                    new SessionFile
                    {
                        FilePath = "new.txt",
                        TabIdentity = "new",
                        SnapshotOwner = "new-owner"
                    }
                },
                0,
                new[] { "old-owner" }));

            var durable = new SettingsService(fileSystem, appDataPath)
                .ReadShutdownSession();
            Assert.Equal("new", Assert.Single(durable.Files).TabIdentity);
        }
        finally
        {
            Directory.Delete(appDataPath, recursive: true);
        }
    }

    [Fact]
    public void LegacyStorePublicationFailureRetainsPreviousDurableSession()
    {
        var appDataPath = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(appDataPath, "settings.json");
        string? durableJson = null;
        var failWrites = false;
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(service => service.FileExists(settingsPath))
            .Returns(() => durableJson != null);
        fileSystem.Setup(service => service.ReadAllText(settingsPath))
            .Returns(() => durableJson!);
        fileSystem.Setup(service => service.WriteAllTextAtomic(
                settingsPath,
                It.IsAny<string>()))
            .Callback<string, string>((_, json) =>
            {
                if (failWrites)
                    throw new IOException("disk full");
                durableJson = json;
            });
        var settings = new SettingsService(fileSystem.Object, appDataPath);
        settings.PublishShutdownSession(new ShutdownSessionState(
            new[]
            {
                new SessionFile
                {
                    FilePath = "old.txt",
                    TabIdentity = "old",
                    SnapshotOwner = "old-owner"
                }
            },
            0));
        var legacy = new LegacyShutdownSessionStore(settings);
        failWrites = true;

        Assert.Throws<IOException>(() => legacy.PublishShutdownSession(
            new ShutdownSessionState(
                new[]
                {
                    new SessionFile
                    {
                        FilePath = "new.txt",
                        TabIdentity = "new",
                        SnapshotOwner = "new-owner"
                    }
                },
                0,
                new[] { "old-owner" })));

        failWrites = false;
        var durable = new SettingsService(fileSystem.Object, appDataPath)
            .ReadShutdownSession();
        Assert.Equal("old", Assert.Single(durable.Files).TabIdentity);
    }

    [Fact]
    public void LegacyStoreRejectsNonAtomicSessionReadsAndWrites()
    {
        var legacy = new LegacyShutdownSessionStore(
            new Mock<ISettingsService>().Object);

        Assert.Throws<InvalidOperationException>(
            () => legacy.ReadShutdownSession());
        Assert.Throws<InvalidOperationException>(
            () => legacy.PublishShutdownSession(
                new ShutdownSessionState(Array.Empty<SessionFile>(), 0)));
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
