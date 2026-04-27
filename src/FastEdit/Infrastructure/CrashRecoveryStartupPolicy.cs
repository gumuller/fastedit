namespace FastEdit.Infrastructure;

public static class CrashRecoveryStartupPolicy
{
    public static bool ShouldPromptForRecovery(bool hasRecoveryFiles, bool hasAnotherRunningInstance)
    {
        return hasRecoveryFiles && !hasAnotherRunningInstance;
    }
}
