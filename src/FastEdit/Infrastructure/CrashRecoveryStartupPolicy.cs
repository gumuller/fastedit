namespace FastEdit.Infrastructure;

public static class CrashRecoveryStartupPolicy
{
    public static bool ShouldPromptForRecovery(bool hasRecoveryFiles, bool hasAnotherRunningInstance)
    {
        return hasRecoveryFiles && !hasAnotherRunningInstance;
    }

    public static bool ShouldClearRecoveryFiles(
        bool userRequestedRecovery,
        bool recoveryDataLoaded,
        bool allEntriesRecovered)
    {
        return !userRequestedRecovery || (recoveryDataLoaded && allEntriesRecovered);
    }
}
