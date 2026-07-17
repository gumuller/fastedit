using FastEdit.Infrastructure;

namespace FastEdit.Tests;

public class CrashRecoveryStartupPolicyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldPromptForRecovery_OnlyPromptsWhenRecoveryFilesExistAndNoInstanceIsRunning(
        bool hasRecoveryFiles,
        bool hasAnotherRunningInstance,
        bool expected)
    {
        var result = CrashRecoveryStartupPolicy.ShouldPromptForRecovery(
            hasRecoveryFiles,
            hasAnotherRunningInstance);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void ShouldClearRecoveryFiles_RequiresCompleteRequestedRecovery(
        bool userRequestedRecovery,
        bool recoveryDataLoaded,
        bool allEntriesRecovered,
        bool expected)
    {
        var result = CrashRecoveryStartupPolicy.ShouldClearRecoveryFiles(
            userRequestedRecovery,
            recoveryDataLoaded,
            allEntriesRecovered);

        Assert.Equal(expected, result);
    }
}
