using System.Diagnostics;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;

namespace FastEdit.Infrastructure;

public sealed class MainWindowLifecycleCoordinator
{
    private readonly IAutoSaveService _autoSaveService;
    private readonly MainWindowLifecycleOperations _operations;
    private readonly object _startupLock = new();
    private Task<MainWindowStartupResult>? _startupTask;
    private int _closeState;

    public MainWindowLifecycleCoordinator(
        IAutoSaveService autoSaveService,
        MainWindowLifecycleOperations operations)
    {
        ArgumentNullException.ThrowIfNull(autoSaveService);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(operations.HasUnsavedChanges);
        ArgumentNullException.ThrowIfNull(operations.PrepareForExitAsync);
        ArgumentNullException.ThrowIfNull(operations.CancelExitPreparation);
        ArgumentNullException.ThrowIfNull(operations.RestoreSessionAsync);
        ArgumentNullException.ThrowIfNull(operations.OpenStartupFileAsync);
        ArgumentNullException.ThrowIfNull(operations.GetWorkingDirectory);
        ArgumentNullException.ThrowIfNull(operations.SetWorkingDirectoryAsync);
        ArgumentNullException.ThrowIfNull(operations.RecoverTabs);
        ArgumentNullException.ThrowIfNull(operations.CaptureRecoverySnapshot);
        ArgumentNullException.ThrowIfNull(operations.ShutdownTerminalAsync);

        _autoSaveService = autoSaveService;
        _operations = operations;
    }

    public bool IsCloseComplete => Volatile.Read(ref _closeState) == 2;

    public Task<MainWindowStartupResult> StartAsync(
        IReadOnlyList<string> startupFiles,
        bool hasAnotherRunningInstance,
        Func<bool> requestRecovery)
    {
        ArgumentNullException.ThrowIfNull(startupFiles);
        ArgumentNullException.ThrowIfNull(requestRecovery);

        TaskCompletionSource<MainWindowStartupResult> startupCompletion;
        lock (_startupLock)
        {
            if (_startupTask != null)
                return _startupTask;

            if (Volatile.Read(ref _closeState) != 0)
            {
                return Task.FromResult(new MainWindowStartupResult(
                    MainWindowStartupOutcome.Failure,
                    new[]
                    {
                        new MainWindowStartupIssue(
                            MainWindowStartupIssueKind.Startup,
                            "FastEdit startup was skipped because shutdown is already in progress.")
                    }));
            }

            startupCompletion = new TaskCompletionSource<MainWindowStartupResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _startupTask = startupCompletion.Task;
        }

        _ = RunStartupAsync(
            startupCompletion,
            startupFiles,
            hasAnotherRunningInstance,
            requestRecovery);
        return startupCompletion.Task;
    }

    private async Task RunStartupAsync(
        TaskCompletionSource<MainWindowStartupResult> startupCompletion,
        IReadOnlyList<string> startupFiles,
        bool hasAnotherRunningInstance,
        Func<bool> requestRecovery)
    {
        try
        {
            var result = await StartCoreAsync(
                startupFiles,
                hasAnotherRunningInstance,
                requestRecovery);
            startupCompletion.TrySetResult(result);
        }
        catch (OperationCanceledException ex)
        {
            startupCompletion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            startupCompletion.TrySetException(ex);
        }
    }

    private async Task<MainWindowStartupResult> StartCoreAsync(
        IReadOnlyList<string> startupFiles,
        bool hasAnotherRunningInstance,
        Func<bool> requestRecovery)
    {
        var issues = new List<MainWindowStartupIssue>();
        TryRecover(hasAnotherRunningInstance, requestRecovery, issues);

        try
        {
            await _operations.RestoreSessionAsync();

            foreach (var path in startupFiles)
                await _operations.OpenStartupFileAsync(path);

            var workingDirectory = _operations.GetWorkingDirectory();
            if (!string.IsNullOrEmpty(workingDirectory))
                await _operations.SetWorkingDirectoryAsync(workingDirectory);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Main window startup failed safely: {0}", ex);
            issues.Add(new MainWindowStartupIssue(
                MainWindowStartupIssueKind.Startup,
                $"FastEdit could not finish restoring the startup workspace: {ex.Message}",
                ex));
            return new MainWindowStartupResult(
                MainWindowStartupOutcome.Failure,
                issues);
        }

        return new MainWindowStartupResult(
            issues.Count == 0
                ? MainWindowStartupOutcome.Success
                : MainWindowStartupOutcome.PartialFailure,
            issues);
    }

    public async Task<MainWindowCloseResult> CloseAsync(
        Action beginPersistence,
        Action persistSession,
        TimeSpan terminalShutdownTimeout)
    {
        ArgumentNullException.ThrowIfNull(beginPersistence);
        ArgumentNullException.ThrowIfNull(persistSession);
        if (terminalShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(terminalShutdownTimeout));

        var previousState = Interlocked.CompareExchange(ref _closeState, 1, 0);
        if (previousState == 1)
            return new MainWindowCloseResult(MainWindowCloseOutcome.InProgress);
        if (previousState == 2)
            return new MainWindowCloseResult(MainWindowCloseOutcome.ReadyToClose);

        try
        {
            Task<MainWindowStartupResult>? startupTask;
            lock (_startupLock)
                startupTask = _startupTask;
            if (startupTask != null)
                await startupTask;

            if (_operations.HasUnsavedChanges() &&
                !await _operations.PrepareForExitAsync())
            {
                Volatile.Write(ref _closeState, 0);
                return new MainWindowCloseResult(MainWindowCloseOutcome.Cancelled);
            }
        }
        catch (Exception ex)
        {
            _operations.CancelExitPreparation();
            Volatile.Write(ref _closeState, 0);
            return new MainWindowCloseResult(
                MainWindowCloseOutcome.PreparationFailed,
                ex);
        }

        try
        {
            beginPersistence();
            persistSession();
        }
        catch (Exception ex)
        {
            _operations.CancelExitPreparation();
            Volatile.Write(ref _closeState, 0);
            return new MainWindowCloseResult(
                MainWindowCloseOutcome.PersistenceFailed,
                ex);
        }

        var terminalShutdownTimedOut = false;
        Exception? terminalShutdownFailure = null;
        using (var timeout = new CancellationTokenSource(terminalShutdownTimeout))
        {
            try
            {
                await _operations.ShutdownTerminalAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                terminalShutdownTimedOut = true;
            }
            catch (Exception ex)
            {
                terminalShutdownFailure = ex;
            }
        }

        Volatile.Write(ref _closeState, 2);
        return new MainWindowCloseResult(
            MainWindowCloseOutcome.ReadyToClose,
            terminalShutdownFailure,
            terminalShutdownTimedOut);
    }

    private void TryRecover(
        bool hasAnotherRunningInstance,
        Func<bool> requestRecovery,
        ICollection<MainWindowStartupIssue> issues)
    {
        try
        {
            if (!CrashRecoveryStartupPolicy.ShouldPromptForRecovery(
                    _autoSaveService.HasRecoveryFiles(),
                    hasAnotherRunningInstance))
            {
                return;
            }

            var recoveryRequested = requestRecovery();
            if (recoveryRequested)
            {
                var recoveryAttempt = CrashRecoveryCoordinator.Recover(
                    _autoSaveService,
                    _operations.RecoverTabs,
                    _operations.CaptureRecoverySnapshot);
                if (!recoveryAttempt.Success)
                    AddRecoveryIssue(recoveryAttempt.FailureMessage!, issues);
                return;
            }

            if (CrashRecoveryStartupPolicy.ShouldClearRecoveryFiles(
                    userRequestedRecovery: false,
                    recoveryDataLoaded: false,
                    allEntriesRecovered: false) &&
                !_autoSaveService.ClearRecoveryFiles())
            {
                AddRecoveryIssue(
                    "The recovery files could not be cleared after recovery was declined.",
                    issues);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Crash recovery startup orchestration failed safely: {0}", ex);
            issues.Add(new MainWindowStartupIssue(
                MainWindowStartupIssueKind.Recovery,
                "Crash recovery could not be completed.",
                ex));
        }
    }

    private static void AddRecoveryIssue(
        string message,
        ICollection<MainWindowStartupIssue> issues)
    {
        issues.Add(new MainWindowStartupIssue(
            MainWindowStartupIssueKind.Recovery,
            message));
    }
}

public sealed record MainWindowLifecycleOperations(
    Func<bool> HasUnsavedChanges,
    Func<Task<bool>> PrepareForExitAsync,
    Action CancelExitPreparation,
    Func<Task> RestoreSessionAsync,
    Func<string, Task> OpenStartupFileAsync,
    Func<string?> GetWorkingDirectory,
    Func<string, Task> SetWorkingDirectoryAsync,
    Func<IReadOnlyList<AutoSaveEntry>, TabRecoveryResult> RecoverTabs,
    Func<IReadOnlyList<AutoSaveEntry>> CaptureRecoverySnapshot,
    Func<CancellationToken, Task> ShutdownTerminalAsync);

public enum MainWindowStartupOutcome
{
    Success,
    PartialFailure,
    Failure
}

public enum MainWindowStartupIssueKind
{
    Recovery,
    Startup
}

public sealed record MainWindowStartupIssue(
    MainWindowStartupIssueKind Kind,
    string Message,
    Exception? Exception = null);

public sealed record MainWindowStartupResult(
    MainWindowStartupOutcome Outcome,
    IReadOnlyList<MainWindowStartupIssue> Issues);

public enum MainWindowCloseOutcome
{
    InProgress,
    Cancelled,
    PreparationFailed,
    PersistenceFailed,
    ReadyToClose
}

public sealed record MainWindowCloseResult(
    MainWindowCloseOutcome Outcome,
    Exception? Error = null,
    bool TerminalShutdownTimedOut = false)
{
    public bool ShouldClose => Outcome == MainWindowCloseOutcome.ReadyToClose;
}
