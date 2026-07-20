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
    public async Task ConcurrentPublishers_ForSameSettingsPath_AreSerialized()
    {
        var fileSystem = new Mock<IFileSystemService>();
        var appDataPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var activeWriters = 0;
        var maximumWriters = 0;
        fileSystem
            .Setup(service => service.WriteAllTextAtomic(
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback(() =>
            {
                var active = Interlocked.Increment(ref activeWriters);
                InterlockedExtensions.Max(ref maximumWriters, active);
                Thread.Sleep(50);
                Interlocked.Decrement(ref activeWriters);
            });
        var first = new SettingsService(fileSystem.Object, appDataPath);
        var second = new SettingsService(fileSystem.Object, appDataPath);

        await Task.WhenAll(
            Task.Run(() => first.ThemeName = "Light"),
            Task.Run(() => second.ThemeName = "Dark"));

        Assert.Equal(1, maximumWriters);
        fileSystem.Verify(service => service.WriteAllTextAtomic(
            Path.Combine(appDataPath, "settings.json"),
            It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public void StaleOrdinaryWriter_PreservesNewerSessionTuple()
    {
        var appDataPath = Path.Combine(
            Path.GetTempPath(),
            $"FastEdit-Settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataPath);
        try
        {
            var publisher = new SettingsService(
                new FileSystemService(),
                appDataPath);
            var staleOrdinaryWriter = new SettingsService(
                new FileSystemService(),
                appDataPath);

            ConfigureSession(publisher, "published", 0);
            publisher.Save();
            staleOrdinaryWriter.ThemeName = "Light";

            var reloaded = new SettingsService(
                new FileSystemService(),
                appDataPath);
            var restored = Assert.Single(reloaded.OpenFiles);
            Assert.Equal("Light", reloaded.ThemeName);
            Assert.Equal("published", restored.EntryId);
            Assert.Equal("published", restored.Content);
            Assert.Equal("published", reloaded.ActiveSessionEntryId);
        }
        finally
        {
            Directory.Delete(appDataPath, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentSessionPublishers_PublishOnlyCompleteTuple()
    {
        var appDataPath = Path.Combine(
            Path.GetTempPath(),
            $"FastEdit-Settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(appDataPath);
        try
        {
            var first = new SettingsService(new FileSystemService(), appDataPath);
            var second = new SettingsService(new FileSystemService(), appDataPath);
            ConfigureSession(first, "first", 3);
            ConfigureSession(second, "second", 7);

            using var barrier = new Barrier(2);
            var outcomes = await Task.WhenAll(
                Task.Run<Exception?>(() =>
                {
                    barrier.SignalAndWait();
                    try
                    {
                        first.Save();
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                }),
                Task.Run<Exception?>(() =>
                {
                    barrier.SignalAndWait();
                    try
                    {
                        second.Save();
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return ex;
                    }
                }));

            Assert.Single(outcomes, outcome => outcome == null);
            Assert.IsType<IOException>(
                Assert.Single(outcomes, outcome => outcome != null));
            var reloaded = new SettingsService(
                new FileSystemService(),
                appDataPath);
            var restored = Assert.Single(reloaded.OpenFiles);
            var firstWon = restored.EntryId == "first";
            Assert.True(firstWon || restored.EntryId == "second");
            Assert.Equal(firstWon ? 3 : 7, reloaded.ActiveTabIndex);
            Assert.Equal(restored.EntryId, reloaded.ActiveSessionEntryId);
            Assert.Equal(restored.EntryId, restored.Content);

            var conflictedPublisher = outcomes[0] == null ? second : first;
            var conflictedEntryId = outcomes[0] == null ? "second" : "first";
            var conflictedIndex = outcomes[0] == null ? 7 : 3;
            conflictedPublisher.ThemeName = "Light";
            var afterOrdinarySave = new SettingsService(
                new FileSystemService(),
                appDataPath);
            Assert.Equal(restored.EntryId, Assert.Single(afterOrdinarySave.OpenFiles).EntryId);
            Assert.Equal("Light", afterOrdinarySave.ThemeName);
            ConfigureSession(
                conflictedPublisher,
                conflictedEntryId,
                conflictedIndex);
            conflictedPublisher.Save();
            var afterRetry = new SettingsService(
                new FileSystemService(),
                appDataPath);
            Assert.Equal(conflictedEntryId, Assert.Single(afterRetry.OpenFiles).EntryId);
            Assert.Equal(conflictedIndex, afterRetry.ActiveTabIndex);
            Assert.Equal(conflictedEntryId, afterRetry.ActiveSessionEntryId);
        }
        finally
        {
            Directory.Delete(appDataPath, recursive: true);
        }
    }

    private static void ConfigureSession(
        SettingsService service,
        string entryId,
        int activeTabIndex)
    {
        service.OpenFiles =
        [
            new SessionFile
            {
                EntryId = entryId,
                FilePath = $"Untitled-{entryId}",
                IsUntitled = true,
                Content = entryId
            }
        ];
        service.ActiveTabIndex = activeTabIndex;
        service.ActiveSessionEntryId = entryId;
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref int location, int value)
    {
        var current = location;
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }
}
