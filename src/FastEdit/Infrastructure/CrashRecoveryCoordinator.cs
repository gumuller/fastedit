using System.Diagnostics;
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
        try
        {
            var recovery = autoSaveService.GetRecoveryEntries();
            var tabRecovery = recoverTabs(recovery.Entries);
            var recoveredAll = recovery.Success && tabRecovery.Success;

            if (recoveredAll || tabRecovery.RecoveredEntryIds.Count > 0)
            {
                var replacementSnapshot = captureReplacementSnapshot();
                if (!autoSaveService.CompleteRecovery(
                    replacementSnapshot,
                    tabRecovery.RecoveredEntryIds,
                    recoveredAll))
                {
                    return ReplacementPersistenceFailure();
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
        catch (Exception ex)
        {
            Trace.TraceWarning("Crash recovery failed safely: {0}", ex);
            return ReplacementPersistenceFailure();
        }
    }

    private static CrashRecoveryAttemptResult ReplacementPersistenceFailure() =>
        new(
            false,
            "Recovered files could not be persisted and retired safely.");
}

public record CrashRecoveryAttemptResult(
    bool Success,
    string? FailureMessage = null);
