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
}
