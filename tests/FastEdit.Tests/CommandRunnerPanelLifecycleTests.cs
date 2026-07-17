using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FastEdit.Services.Interfaces;
using FastEdit.Views.Controls;
using Xunit;

namespace FastEdit.Tests;

public class CommandRunnerPanelLifecycleTests
{
    [Fact]
    public async Task Reload_CreatesFreshRunnerAndBalancesSubscriptions()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var factory = new FakeCommandRunnerFactory();
            var panel = new CommandRunnerPanel { RunnerFactory = factory };
            AddTerminalResources(panel);
            var desiredDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

            await panel.SetWorkingDirectoryAsync(desiredDirectory);
            await panel.EnsureStartedAsync();
            var first = Assert.Single(factory.Created);
            Assert.Equal(1, first.StartCount);
            Assert.Equal(desiredDirectory, first.InitialDirectory);
            Assert.Equal(4, first.SubscriptionCount);

            await panel.SetWorkingDirectoryAsync(@"C:\path-that-does-not-exist-fastedit");
            await panel.ShutdownAsync();
            Assert.Equal(1, first.ShutdownCount);
            Assert.Equal(0, first.SubscriptionCount);

            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, panel));

            Assert.Equal(2, factory.Created.Count);
            var second = factory.Created[1];
            Assert.NotSame(first, second);
            Assert.Equal(1, second.StartCount);
            Assert.Equal(desiredDirectory, second.InitialDirectory);
            Assert.Equal(4, second.SubscriptionCount);

            await panel.ShutdownAsync();
            Assert.Equal(0, second.SubscriptionCount);
        });
    }

    [Fact]
    public async Task StartupCompletingAfterUnloadTimeout_ShutsDownRunnerBeforeReload()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var factory = new FakeCommandRunnerFactory(blockFirstStart: true);
            var panel = new CommandRunnerPanel { RunnerFactory = factory };
            AddTerminalResources(panel);

            var startup = panel.EnsureStartedAsync();
            var first = Assert.Single(factory.Created);
            await first.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => panel.ShutdownAsync(timeout.Token));
            }

            first.AllowStart();
            await startup.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, first.ShutdownCount);
            Assert.Equal(0, first.SubscriptionCount);
            Assert.False(first.IsRunning);

            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, panel));

            Assert.Equal(2, factory.Created.Count);
            var second = factory.Created[1];
            Assert.Equal(1, second.StartCount);
            Assert.Equal(4, second.SubscriptionCount);

            await panel.ShutdownAsync();
        });
    }

    private static void AddTerminalResources(FrameworkElement element)
    {
        element.Resources["EditorForegroundBrush"] = Brushes.White;
        element.Resources["EditorBackgroundBrush"] = Brushes.Black;
        element.Resources["TabBorderBrush"] = Brushes.Gray;
        element.Resources["AccentBrush"] = Brushes.Cyan;
    }

    private static Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(dispatcher));
                var task = action();
                _ = task.ContinueWith(
                    _ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal),
                    TaskScheduler.Default);
                Dispatcher.Run();
                task.GetAwaiter().GetResult();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class FakeCommandRunnerFactory : ICommandRunnerFactory
    {
        private readonly bool _blockFirstStart;

        public FakeCommandRunnerFactory(bool blockFirstStart = false)
        {
            _blockFirstStart = blockFirstStart;
        }

        public List<FakeCommandRunner> Created { get; } = new();

        public ICommandRunner Create()
        {
            var runner = new FakeCommandRunner(_blockFirstStart && Created.Count == 0);
            Created.Add(runner);
            return runner;
        }
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        private Action<string>? _outputReceived;
        private Action<string>? _workingDirectoryChanged;
        private Action? _commandStarted;
        private Action? _commandCompleted;
        private readonly TaskCompletionSource _allowStart =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount { get; private set; }
        public int ShutdownCount { get; private set; }
        public int SubscriptionCount { get; private set; }
        public string? InitialDirectory { get; private set; }
        public string WorkingDirectory { get; private set; } = Environment.CurrentDirectory;
        public TaskCompletionSource StartEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> History => Array.Empty<string>();
        public bool IsRunning { get; private set; }
        public bool IsBusy => false;

        public FakeCommandRunner(bool blockStart)
        {
            if (!blockStart)
                _allowStart.TrySetResult();
        }

        public event Action<string>? OutputReceived
        {
            add
            {
                _outputReceived += value;
                SubscriptionCount++;
            }
            remove
            {
                _outputReceived -= value;
                SubscriptionCount--;
            }
        }

        public event Action<string>? WorkingDirectoryChanged
        {
            add
            {
                _workingDirectoryChanged += value;
                SubscriptionCount++;
            }
            remove
            {
                _workingDirectoryChanged -= value;
                SubscriptionCount--;
            }
        }

        public event Action? CommandStarted
        {
            add
            {
                _commandStarted += value;
                SubscriptionCount++;
            }
            remove
            {
                _commandStarted -= value;
                SubscriptionCount--;
            }
        }

        public event Action? CommandCompleted
        {
            add
            {
                _commandCompleted += value;
                SubscriptionCount++;
            }
            remove
            {
                _commandCompleted -= value;
                SubscriptionCount--;
            }
        }

        public async Task StartShellAsync(
            string? initialDirectory = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            StartEntered.TrySetResult();
            await _allowStart.Task.WaitAsync(cancellationToken);
            InitialDirectory = initialDirectory;
            if (initialDirectory != null)
                WorkingDirectory = initialDirectory;
            IsRunning = true;
        }

        public void AllowStart() => _allowStart.TrySetResult();

        public Task ExecuteCommandAsync(
            string command,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopCurrentProcessAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public string? GetPreviousHistoryItem() => null;
        public string? GetNextHistoryItem() => "";

        public Task<bool> SetWorkingDirectoryAsync(
            string? directory,
            CancellationToken cancellationToken = default)
        {
            if (directory == null)
                return Task.FromResult(false);

            WorkingDirectory = directory;
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            IsRunning = false;
        }
    }
}
