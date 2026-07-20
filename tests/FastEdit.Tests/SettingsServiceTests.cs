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
