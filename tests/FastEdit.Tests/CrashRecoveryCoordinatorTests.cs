using System.IO;
using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;

namespace FastEdit.Tests;

public class CrashRecoveryCoordinatorTests
{
    [Fact]
    public void Recover_PersistsReplacementBeforeResolvingAndClearingSources()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "recovered", true);
        var replacement = new AutoSaveEntry("replacement", "recovered.txt", null, "recovered", true);
        var operations = new List<string>();
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(true, new[] { source }));
        autoSave.Setup(service => service.SaveNow(It.IsAny<IEnumerable<AutoSaveEntry>>()))
            .Callback(() => operations.Add("persist"));
        autoSave.Setup(service => service.RecordRecoveredEntries(new[] { source.Id }))
            .Callback(() => operations.Add("resolve"))
            .Returns(true);
        autoSave.Setup(service => service.ClearRecoveryFiles())
            .Callback(() => operations.Add("clear"))
            .Returns(true);

        var result = CrashRecoveryCoordinator.Recover(
            autoSave.Object,
            _ => new TabRecoveryResult(true, new[] { source.Id }),
            () => new[] { replacement });

        Assert.True(result.Success);
        Assert.Equal(new[] { "persist", "resolve", "clear" }, operations);
        autoSave.Verify(service => service.SaveNow(
            It.Is<IEnumerable<AutoSaveEntry>>(entries => entries.Single().Id == replacement.Id)),
            Times.Once);
    }

    [Fact]
    public void Recover_ReplacementPersistenceFailureRetainsSourceGeneration()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "recovered", true);
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(true, new[] { source }));
        autoSave.Setup(service => service.SaveNow(It.IsAny<IEnumerable<AutoSaveEntry>>()))
            .Throws(new IOException("disk full"));

        var result = CrashRecoveryCoordinator.Recover(
            autoSave.Object,
            _ => new TabRecoveryResult(true, new[] { source.Id }),
            () => new[] { source });

        Assert.False(result.Success);
        Assert.Contains("could not be persisted", result.FailureMessage);
        autoSave.Verify(
            service => service.RecordRecoveredEntries(It.IsAny<IEnumerable<string>>()),
            Times.Never);
        autoSave.Verify(service => service.ClearRecoveryFiles(), Times.Never);
    }
}
