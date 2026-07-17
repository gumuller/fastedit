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

        if (tabRecovery.RecoveredEntryIds.Count > 0)
        {
            try
            {
                autoSaveService.SaveNow(captureReplacementSnapshot());
            }
            catch (Exception ex)
            {
                return new CrashRecoveryAttemptResult(
                    false,
                    $"Recovered files could not be persisted: {ex.Message}");
            }

            if (!autoSaveService.RecordRecoveredEntries(tabRecovery.RecoveredEntryIds))
            {
                return new CrashRecoveryAttemptResult(
                    false,
                    "Recovered files could not be marked complete.");
            }
        }

        var recoveredAll = recovery.Success && tabRecovery.Success;
        if (!recoveredAll)
        {
            return new CrashRecoveryAttemptResult(
                false,
                recovery.ErrorMessage ?? "One or more files could not be recovered.");
        }

        if (!autoSaveService.ClearRecoveryFiles())
        {
            return new CrashRecoveryAttemptResult(
                false,
                "Recovered content is open, but the source recovery files could not be cleared.");
        }

        return new CrashRecoveryAttemptResult(true);
    }
}

public record CrashRecoveryAttemptResult(
    bool Success,
    string? FailureMessage = null);
