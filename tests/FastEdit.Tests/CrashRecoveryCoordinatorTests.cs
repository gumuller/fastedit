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
        autoSave.Setup(service => service.CompleteRecovery(
                It.IsAny<IEnumerable<AutoSaveEntry>>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .Callback(() => operations.Add("complete"))
            .Returns(true);

        var result = CrashRecoveryCoordinator.Recover(
            autoSave.Object,
            _ => new TabRecoveryResult(true, new[] { source.Id }),
            () => new[] { replacement });

        Assert.True(result.Success);
        Assert.Equal(new[] { "complete" }, operations);
        autoSave.Verify(service => service.CompleteRecovery(
                It.Is<IEnumerable<AutoSaveEntry>>(entries =>
                    entries.Single().Id == replacement.Id),
                It.Is<IEnumerable<string>>(ids => ids.Single() == source.Id),
                true),
            Times.Once);
    }

    [Fact]
    public void Recover_ReplacementPersistenceFailureRetainsSourceGeneration()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "recovered", true);
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(true, new[] { source }));
        autoSave.Setup(service => service.CompleteRecovery(
                It.IsAny<IEnumerable<AutoSaveEntry>>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .Returns(false);

        var result = CrashRecoveryCoordinator.Recover(
            autoSave.Object,
            _ => new TabRecoveryResult(true, new[] { source.Id }),
            () => new[] { source });

        Assert.False(result.Success);
        Assert.Contains("could not be persisted", result.FailureMessage);
        autoSave.Verify(service => service.CompleteRecovery(
            It.IsAny<IEnumerable<AutoSaveEntry>>(),
            It.IsAny<IEnumerable<string>>(),
            true), Times.Once);
    }

    [Fact]
    public void Recover_ReplacementSnapshotCaptureFailureRetainsSourceGeneration()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "recovered", true);
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(true, new[] { source }));

        var result = CrashRecoveryCoordinator.Recover(
            autoSave.Object,
            _ => new TabRecoveryResult(true, new[] { source.Id }),
            () => throw new InvalidDataException("malformed path"));

        Assert.False(result.Success);
        Assert.Contains("could not be persisted", result.FailureMessage);
        autoSave.Verify(service => service.CompleteRecovery(
            It.IsAny<IEnumerable<AutoSaveEntry>>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<bool>()), Times.Never);
        autoSave.Verify(service => service.RecordRecoveredEntries(
            It.IsAny<IEnumerable<string>>()), Times.Never);
        autoSave.Verify(service => service.ClearRecoveryFiles(), Times.Never);
    }

    [Fact]
    public void Recover_ReplacementServiceExceptionReturnsControlledFailure()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "recovered", true);
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(true, new[] { source }));
        autoSave.Setup(service => service.CompleteRecovery(
                It.IsAny<IEnumerable<AutoSaveEntry>>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .Throws(new IOException("storage unavailable"));

        var result = CrashRecoveryCoordinator.Recover(
            autoSave.Object,
            _ => new TabRecoveryResult(true, new[] { source.Id }),
            () => new[] { source });

        Assert.False(result.Success);
        Assert.Contains("could not be persisted", result.FailureMessage);
        autoSave.Verify(service => service.RecordRecoveredEntries(
            It.IsAny<IEnumerable<string>>()), Times.Never);
        autoSave.Verify(service => service.ClearRecoveryFiles(), Times.Never);
    }

    [Fact]
    public void Recover_RecoveryReadExceptionDoesNotEscapeStartupBoundary()
    {
        var autoSave = new Mock<IAutoSaveService>();
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Throws(new IOException("storage unavailable"));
        var recoverTabsCalled = false;
        var captureCalled = false;

        var result = CrashRecoveryCoordinator.Recover(
            autoSave.Object,
            _ =>
            {
                recoverTabsCalled = true;
                return new TabRecoveryResult(true, Array.Empty<string>());
            },
            () =>
            {
                captureCalled = true;
                return Array.Empty<AutoSaveEntry>();
            });

        Assert.False(result.Success);
        Assert.Contains("could not be persisted", result.FailureMessage);
        Assert.False(recoverTabsCalled);
        Assert.False(captureCalled);
        autoSave.Verify(service => service.CompleteRecovery(
            It.IsAny<IEnumerable<AutoSaveEntry>>(),
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<bool>()), Times.Never);
    }
}
