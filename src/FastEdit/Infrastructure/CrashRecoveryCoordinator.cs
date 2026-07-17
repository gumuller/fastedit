using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public static class CrashRecoveryCoordinator
{
    public static CrashRecoveryAttemptResult Recover(
        IAutoSaveService autoSaveService,
        Func<IReadOnlyList<AutoSaveEntry>, TabRecoveryResult> recoverTabs,
        Func<IReadOnlyList<AutoSaveEntry>> captureReplacementSnapshot)
    {
        var recovery = autoSaveService.GetRecoveryEntries();
        var tabRecovery = recoverTabs(recovery.Entries);
        var recoveredAll = recovery.Success && tabRecovery.Success;

        if (recoveredAll || tabRecovery.RecoveredEntryIds.Count > 0)
        {
            if (!autoSaveService.CompleteRecovery(
                captureReplacementSnapshot(),
                tabRecovery.RecoveredEntryIds,
                recoveredAll))
            {
                return new CrashRecoveryAttemptResult(
                    false,
                    "Recovered files could not be persisted and retired safely.");
            }
        }

        if (!recoveredAll)
        {
            return new CrashRecoveryAttemptResult(
                false,
                recovery.ErrorMessage ?? "One or more files could not be recovered.");
        }

        return new CrashRecoveryAttemptResult(true);
    }
}

public record CrashRecoveryAttemptResult(
    bool Success,
    string? FailureMessage = null);
