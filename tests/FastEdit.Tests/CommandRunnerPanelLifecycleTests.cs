using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
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
        var thread = new Thread(async () =>
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
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class FakeCommandRunnerFactory : ICommandRunnerFactory
    {
        public List<FakeCommandRunner> Created { get; } = new();

        public ICommandRunner Create()
        {
            var runner = new FakeCommandRunner();
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

        public int StartCount { get; private set; }
        public int ShutdownCount { get; private set; }
        public int SubscriptionCount { get; private set; }
        public string? InitialDirectory { get; private set; }
        public string WorkingDirectory { get; private set; } = Environment.CurrentDirectory;
        public IReadOnlyList<string> History => Array.Empty<string>();
        public bool IsRunning { get; private set; }
        public bool IsBusy => false;

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

        public Task StartShellAsync(
            string? initialDirectory = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            InitialDirectory = initialDirectory;
            if (initialDirectory != null)
                WorkingDirectory = initialDirectory;
            IsRunning = true;
            return Task.CompletedTask;
        }

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
