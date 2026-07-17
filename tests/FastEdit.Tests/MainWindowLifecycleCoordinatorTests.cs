using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;
using System.Windows.Threading;

namespace FastEdit.Tests;

public class MainWindowLifecycleCoordinatorTests
{
    [Fact]
    public async Task StartAsync_RecoversThenRestoresStartupFilesAndWorkingDirectory()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "content", true);
        var replacement = new AutoSaveEntry("replacement", "recovered.txt", null, "content", true);
        var operations = new List<string>();
        autoSave.Setup(service => service.HasRecoveryFiles()).Returns(true);
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(true, new[] { source }));
        autoSave.Setup(service => service.CompleteRecovery(
                It.IsAny<IEnumerable<AutoSaveEntry>>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .Callback(() => operations.Add("complete-recovery"))
            .Returns(true);
        var coordinator = CreateCoordinator(
            autoSave.Object,
            restoreSessionAsync: () =>
            {
                operations.Add("restore-session");
                return Task.CompletedTask;
            },
            openStartupFileAsync: path =>
            {
                operations.Add($"open:{path}");
                return Task.CompletedTask;
            },
            getWorkingDirectory: () => @"C:\workspace",
            setWorkingDirectoryAsync: path =>
            {
                operations.Add($"directory:{path}");
                return Task.CompletedTask;
            },
            recoverTabs: entries =>
            {
                operations.Add("recover-tabs");
                return new TabRecoveryResult(true, entries.Select(entry => entry.Id).ToArray());
            },
            captureRecoverySnapshot: () =>
            {
                operations.Add("capture-replacement");
                return new[] { replacement };
            });

        var result = await coordinator.StartAsync(
            new[] { "first.txt", "second.txt" },
            hasAnotherRunningInstance: false,
            requestRecovery: () => true);

        Assert.Equal(MainWindowStartupOutcome.Success, result.Outcome);
        Assert.Empty(result.Issues);
        Assert.Equal(
            new[]
            {
                "recover-tabs",
                "capture-replacement",
                "complete-recovery",
                "restore-session",
                "open:first.txt",
                "open:second.txt",
                @"directory:C:\workspace"
            },
            operations);
    }

    [Fact]
    public async Task StartAsync_PartialRecoveryFailureRemainsVisibleAndContinuesStartup()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "content", true);
        var restored = false;
        autoSave.Setup(service => service.HasRecoveryFiles()).Returns(true);
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(false, new[] { source }, "Recovery data was incomplete."));
        var coordinator = CreateCoordinator(
            autoSave.Object,
            restoreSessionAsync: () =>
            {
                restored = true;
                return Task.CompletedTask;
            },
            recoverTabs: _ => new TabRecoveryResult(false, Array.Empty<string>()));

        var result = await coordinator.StartAsync(
            Array.Empty<string>(),
            hasAnotherRunningInstance: false,
            requestRecovery: () => true);

        Assert.Equal(MainWindowStartupOutcome.PartialFailure, result.Outcome);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(MainWindowStartupIssueKind.Recovery, issue.Kind);
        Assert.Contains("incomplete", issue.Message);
        Assert.True(restored);
        autoSave.Verify(service => service.ClearRecoveryFiles(), Times.Never);
    }

    [Fact]
    public async Task StartAsync_ContainsFatalStartupFailure()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var openCalled = false;
        var coordinator = CreateCoordinator(
            autoSave.Object,
            restoreSessionAsync: () => throw new InvalidOperationException("session unavailable"),
            openStartupFileAsync: _ =>
            {
                openCalled = true;
                return Task.CompletedTask;
            });

        var startupTask = coordinator.StartAsync(
            new[] { "file.txt" },
            hasAnotherRunningInstance: false,
            requestRecovery: () => true);
        var result = await startupTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(MainWindowStartupOutcome.Failure, result.Outcome);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(MainWindowStartupIssueKind.Startup, issue.Kind);
        Assert.IsType<InvalidOperationException>(issue.Exception);
        Assert.False(openCalled);
        Assert.True(startupTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StartAsync_RepeatedCallReturnsPublishedTaskUntilStartupCompletes()
    {
        var startupStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupRelease =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCount = 0;
        var coordinator = CreateCoordinator(
            Mock.Of<IAutoSaveService>(),
            restoreSessionAsync: async () =>
            {
                restoreCount++;
                startupStarted.TrySetResult();
                await startupRelease.Task;
            });

        var firstStart = coordinator.StartAsync(
            Array.Empty<string>(),
            hasAnotherRunningInstance: false,
            requestRecovery: () => false);
        await startupStarted.Task;
        var repeatedStart = coordinator.StartAsync(
            new[] { "ignored.txt" },
            hasAnotherRunningInstance: true,
            requestRecovery: () => throw new InvalidOperationException("must not run"));

        Assert.Same(firstStart, repeatedStart);
        Assert.False(repeatedStart.IsCompleted);

        startupRelease.TrySetResult();
        var result = await repeatedStart;

        Assert.Equal(MainWindowStartupOutcome.Success, result.Outcome);
        Assert.Same(result, await firstStart);
        Assert.Equal(1, restoreCount);
    }

    [Fact]
    public async Task CloseAsync_CancelledPreparationDoesNotPersistOrShutdown()
    {
        var persisted = false;
        var shutdown = false;
        var coordinator = CreateCoordinator(
            Mock.Of<IAutoSaveService>(),
            hasUnsavedChanges: () => true,
            prepareForExitAsync: () => Task.FromResult(false),
            shutdownTerminalAsync: _ =>
            {
                shutdown = true;
                return Task.CompletedTask;
            });

        var result = await coordinator.CloseAsync(
            () => { },
            () => persisted = true,
            TimeSpan.FromSeconds(1));

        Assert.Equal(MainWindowCloseOutcome.Cancelled, result.Outcome);
        Assert.False(persisted);
        Assert.False(shutdown);
        Assert.False(coordinator.IsCloseComplete);
    }

    [Fact]
    public async Task CloseAsync_PersistenceFailureCancelsPreparationAndKeepsWindowOpen()
    {
        var preparationCancelled = false;
        var shutdown = false;
        var coordinator = CreateCoordinator(
            Mock.Of<IAutoSaveService>(),
            hasUnsavedChanges: () => true,
            prepareForExitAsync: () => Task.FromResult(true),
            cancelExitPreparation: () => preparationCancelled = true,
            shutdownTerminalAsync: _ =>
            {
                shutdown = true;
                return Task.CompletedTask;
            });

        var result = await coordinator.CloseAsync(
            () => { },
            () => throw new InvalidOperationException("disk full"),
            TimeSpan.FromSeconds(1));

        Assert.Equal(MainWindowCloseOutcome.PersistenceFailed, result.Outcome);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.True(preparationCancelled);
        Assert.False(shutdown);
        Assert.False(coordinator.IsCloseComplete);
    }

    [Fact]
    public async Task CloseAsync_PersistsBeforeAwaitingTerminalAndAllowsTimeout()
    {
        var operations = new List<string>();
        var coordinator = CreateCoordinator(
            Mock.Of<IAutoSaveService>(),
            shutdownTerminalAsync: async cancellationToken =>
            {
                operations.Add("terminal");
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        var result = await coordinator.CloseAsync(
            () => operations.Add("begin-persistence"),
            () => operations.Add("persist"),
            TimeSpan.FromMilliseconds(50));

        Assert.Equal(MainWindowCloseOutcome.ReadyToClose, result.Outcome);
        Assert.True(result.TerminalShutdownTimedOut);
        Assert.True(result.ShouldClose);
        Assert.True(coordinator.IsCloseComplete);
        Assert.Equal(
            new[] { "begin-persistence", "persist", "terminal" },
            operations);
    }

    [Fact]
    public async Task CloseAsync_ReentrantAttemptReturnsInProgress()
    {
        var preparationStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparationRelease =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var persistenceCount = 0;
        var coordinator = CreateCoordinator(
            Mock.Of<IAutoSaveService>(),
            hasUnsavedChanges: () => true,
            prepareForExitAsync: () =>
            {
                preparationStarted.TrySetResult();
                return preparationRelease.Task;
            });

        var firstClose = coordinator.CloseAsync(
            () => { },
            () => persistenceCount++,
            TimeSpan.FromSeconds(1));
        await preparationStarted.Task;

        var reentrantClose = await coordinator.CloseAsync(
            () => throw new InvalidOperationException("must not run"),
            () => throw new InvalidOperationException("must not run"),
            TimeSpan.FromSeconds(1));
        preparationRelease.TrySetResult(true);
        var completedClose = await firstClose;

        Assert.Equal(MainWindowCloseOutcome.InProgress, reentrantClose.Outcome);
        Assert.Equal(MainWindowCloseOutcome.ReadyToClose, completedClose.Outcome);
        Assert.Equal(1, persistenceCount);
    }

    [Fact]
    public async Task CloseAsync_WaitsForActiveStartupBeforePersisting()
    {
        var startupStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startupRelease =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new List<string>();
        var coordinator = CreateCoordinator(
            Mock.Of<IAutoSaveService>(),
            restoreSessionAsync: async () =>
            {
                startupStarted.TrySetResult();
                await startupRelease.Task;
                operations.Add("startup-complete");
            });

        var startupTask = coordinator.StartAsync(
            Array.Empty<string>(),
            hasAnotherRunningInstance: false,
            requestRecovery: () => true);
        await startupStarted.Task;
        var closeTask = coordinator.CloseAsync(
            () => { },
            () => operations.Add("persist"),
            TimeSpan.FromSeconds(1));

        Assert.False(closeTask.IsCompleted);
        Assert.Empty(operations);

        startupRelease.TrySetResult();
        await startupTask;
        var closeResult = await closeTask;

        Assert.Equal(MainWindowCloseOutcome.ReadyToClose, closeResult.Outcome);
        Assert.Equal(new[] { "startup-complete", "persist" }, operations);
    }

    [Fact]
    public async Task CloseAsync_RequestedDuringRecoveryWaitsForStartupBeforePersisting()
    {
        var autoSave = new Mock<IAutoSaveService>();
        var source = new AutoSaveEntry("source", "recovered.txt", null, "content", true);
        var replacement = new AutoSaveEntry("replacement", "recovered.txt", null, "content", true);
        var operations = new List<string>();
        Task<MainWindowCloseResult>? closeTask = null;
        autoSave.Setup(service => service.HasRecoveryFiles()).Returns(true);
        autoSave.Setup(service => service.GetRecoveryEntries())
            .Returns(new RecoveryEntriesResult(true, new[] { source }));
        autoSave.Setup(service => service.CompleteRecovery(
                It.IsAny<IEnumerable<AutoSaveEntry>>(),
                It.IsAny<IEnumerable<string>>(),
                true))
            .Callback(() => operations.Add("retire-recovery-source"))
            .Returns(true);
        var coordinator = CreateCoordinator(
            autoSave.Object,
            restoreSessionAsync: () =>
            {
                operations.Add("restore-session");
                return Task.CompletedTask;
            },
            recoverTabs: entries =>
            {
                operations.Add("recover-tabs");
                return new TabRecoveryResult(
                    true,
                    entries.Select(entry => entry.Id).ToArray());
            },
            captureRecoverySnapshot: () =>
            {
                operations.Add("capture-replacement");
                return new[] { replacement };
            },
            shutdownTerminalAsync: _ =>
            {
                operations.Add("shutdown-terminal");
                return Task.CompletedTask;
            });

        var startupTask = coordinator.StartAsync(
            Array.Empty<string>(),
            hasAnotherRunningInstance: false,
            requestRecovery: () =>
            {
                operations.Add("prompt-close");
                closeTask = coordinator.CloseAsync(
                    () => { },
                    () => operations.Add("persist-session"),
                    TimeSpan.FromSeconds(1));
                Assert.False(closeTask.IsCompleted);
                Assert.DoesNotContain("persist-session", operations);
                return true;
            });
        var startupResult = await startupTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(closeTask);
        var closeResult = await closeTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(MainWindowStartupOutcome.Success, startupResult.Outcome);
        Assert.Equal(MainWindowCloseOutcome.ReadyToClose, closeResult.Outcome);
        Assert.Equal(
            new[]
            {
                "prompt-close",
                "recover-tabs",
                "capture-replacement",
                "retire-recovery-source",
                "restore-session",
                "persist-session",
                "shutdown-terminal"
            },
            operations);
    }

    [Fact]
    public async Task StartupAndReentrantClose_KeepCallbacksOnDispatcher()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var autoSave = new Mock<IAutoSaveService>();
            var source = new AutoSaveEntry("source", "recovered.txt", null, "content", true);
            Task<MainWindowCloseResult>? closeTask = null;
            autoSave.Setup(service => service.HasRecoveryFiles()).Returns(true);
            autoSave.Setup(service => service.GetRecoveryEntries())
                .Returns(new RecoveryEntriesResult(true, new[] { source }));
            autoSave.Setup(service => service.CompleteRecovery(
                    It.IsAny<IEnumerable<AutoSaveEntry>>(),
                    It.IsAny<IEnumerable<string>>(),
                    true))
                .Callback(dispatcher.VerifyAccess)
                .Returns(true);
            var coordinator = CreateCoordinator(
                autoSave.Object,
                restoreSessionAsync: async () =>
                {
                    dispatcher.VerifyAccess();
                    await Dispatcher.Yield();
                    dispatcher.VerifyAccess();
                },
                recoverTabs: entries =>
                {
                    dispatcher.VerifyAccess();
                    return new TabRecoveryResult(
                        true,
                        entries.Select(entry => entry.Id).ToArray());
                },
                captureRecoverySnapshot: () =>
                {
                    dispatcher.VerifyAccess();
                    return new[] { source };
                },
                shutdownTerminalAsync: _ =>
                {
                    dispatcher.VerifyAccess();
                    return Task.CompletedTask;
                });

            var startupTask = coordinator.StartAsync(
                Array.Empty<string>(),
                hasAnotherRunningInstance: false,
                requestRecovery: () =>
                {
                    dispatcher.VerifyAccess();
                    closeTask = coordinator.CloseAsync(
                        dispatcher.VerifyAccess,
                        dispatcher.VerifyAccess,
                        TimeSpan.FromSeconds(1));
                    return true;
                });

            Assert.Equal(
                MainWindowStartupOutcome.Success,
                (await startupTask).Outcome);
            dispatcher.VerifyAccess();
            Assert.NotNull(closeTask);
            Assert.Equal(
                MainWindowCloseOutcome.ReadyToClose,
                (await closeTask).Outcome);
            dispatcher.VerifyAccess();
        });
    }

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static MainWindowLifecycleCoordinator CreateCoordinator(
        IAutoSaveService autoSaveService,
        Func<bool>? hasUnsavedChanges = null,
        Func<Task<bool>>? prepareForExitAsync = null,
        Action? cancelExitPreparation = null,
        Func<Task>? restoreSessionAsync = null,
        Func<string, Task>? openStartupFileAsync = null,
        Func<string?>? getWorkingDirectory = null,
        Func<string, Task>? setWorkingDirectoryAsync = null,
        Func<IReadOnlyList<AutoSaveEntry>, TabRecoveryResult>? recoverTabs = null,
        Func<IReadOnlyList<AutoSaveEntry>>? captureRecoverySnapshot = null,
        Func<CancellationToken, Task>? shutdownTerminalAsync = null)
    {
        return new MainWindowLifecycleCoordinator(
            autoSaveService,
            new MainWindowLifecycleOperations(
                hasUnsavedChanges ?? (() => false),
                prepareForExitAsync ?? (() => Task.FromResult(true)),
                cancelExitPreparation ?? (() => { }),
                restoreSessionAsync ?? (() => Task.CompletedTask),
                openStartupFileAsync ?? (_ => Task.CompletedTask),
                getWorkingDirectory ?? (() => null),
                setWorkingDirectoryAsync ?? (_ => Task.CompletedTask),
                recoverTabs ?? (entries =>
                    new TabRecoveryResult(true, entries.Select(entry => entry.Id).ToArray())),
                captureRecoverySnapshot ?? (() => Array.Empty<AutoSaveEntry>()),
                shutdownTerminalAsync ?? (_ => Task.CompletedTask)));
    }
}
