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
}
