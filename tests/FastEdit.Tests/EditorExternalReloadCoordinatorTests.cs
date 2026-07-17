using FastEdit.Infrastructure;
using FastEdit.Services.Interfaces;
using FastEdit.ViewModels;
using Moq;

namespace FastEdit.Tests;

public class EditorExternalReloadCoordinatorTests
{
    [Fact]
    public async Task NotifyAsync_NewerGenerationInvalidatesStaleRead()
    {
        var viewModel = CreateViewModel("baseline");
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCount = 0;
        using var coordinator = CreateCoordinator(
            viewModel,
            readFileAsync: _ =>
            {
                if (Interlocked.Increment(ref readCount) == 1)
                {
                    firstReadStarted.SetResult();
                    return releaseFirstRead.Task;
                }

                return Task.FromResult("newest");
            });

        var processing = coordinator.NotifyAsync(viewModel.FilePath);
        await firstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.NotifyAsync(viewModel.FilePath);
        releaseFirstRead.SetResult("stale");
        await processing;

        Assert.Equal("newest", viewModel.Content);
        Assert.False(viewModel.IsModified);
        Assert.Equal(2, readCount);
    }

    [Fact]
    public async Task NotifyAsync_DeclinedDiscardPreservesDirtyBufferWithoutReading()
    {
        var viewModel = CreateViewModel("baseline");
        viewModel.Content = "dirty";
        var readCount = 0;
        using var coordinator = CreateCoordinator(
            viewModel,
            readFileAsync: _ =>
            {
                readCount++;
                return Task.FromResult("disk");
            },
            confirmDiscard: _ => false);

        await coordinator.NotifyAsync(viewModel.FilePath);

        Assert.Equal("dirty", viewModel.Content);
        Assert.True(viewModel.IsModified);
        Assert.Equal(0, readCount);
    }

    [Fact]
    public async Task NotifyAsync_ApprovedDiscardIsReusedAcrossNewerGeneration()
    {
        var viewModel = CreateViewModel("baseline");
        viewModel.Content = "dirty";
        var firstReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmCount = 0;
        var readCount = 0;
        using var coordinator = CreateCoordinator(
            viewModel,
            readFileAsync: _ =>
            {
                if (Interlocked.Increment(ref readCount) == 1)
                {
                    firstReadStarted.SetResult();
                    return releaseFirstRead.Task;
                }

                return Task.FromResult("newest");
            },
            confirmDiscard: _ =>
            {
                confirmCount++;
                return true;
            });

        var processing = coordinator.NotifyAsync(viewModel.FilePath);
        await firstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.NotifyAsync(viewModel.FilePath);
        releaseFirstRead.SetResult("stale");
        await processing;

        Assert.Equal(1, confirmCount);
        Assert.Equal("newest", viewModel.Content);
        Assert.False(viewModel.IsModified);
    }

    [Fact]
    public async Task NotifyAsync_BufferChangedAfterApprovalIsNeverOverwritten()
    {
        var viewModel = CreateViewModel("baseline");
        viewModel.Content = "dirty";
        var readStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bufferChanged = false;
        using var coordinator = CreateCoordinator(
            viewModel,
            readFileAsync: _ =>
            {
                readStarted.SetResult();
                return releaseRead.Task;
            },
            confirmDiscard: _ => true,
            onBufferChanged: _ => bufferChanged = true);

        var processing = coordinator.NotifyAsync(viewModel.FilePath);
        await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Content = "newer dirty edit";
        releaseRead.SetResult("disk");
        await processing;

        Assert.True(bufferChanged);
        Assert.Equal("newer dirty edit", viewModel.Content);
        Assert.True(viewModel.IsModified);
    }

    [Fact]
    public async Task Dispose_CancelsPendingReload()
    {
        var viewModel = CreateViewModel("baseline");
        var readCount = 0;
        var coordinator = CreateCoordinator(
            viewModel,
            settleDelay: TimeSpan.FromSeconds(10),
            readFileAsync: _ =>
            {
                readCount++;
                return Task.FromResult("disk");
            });

        var processing = coordinator.NotifyAsync(viewModel.FilePath);
        coordinator.Dispose();
        await processing.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, readCount);
        Assert.Equal("baseline", viewModel.Content);
    }

    [Fact]
    public async Task NotifyAsync_QueuedEventAfterCancelDoesNotRestartDisabledReload()
    {
        var viewModel = CreateViewModel("baseline");
        var autoReloadEnabled = true;
        var readCount = 0;
        using var coordinator = CreateCoordinator(
            viewModel,
            readFileAsync: _ =>
            {
                readCount++;
                return Task.FromResult("disk");
            },
            canReload: _ => autoReloadEnabled);

        autoReloadEnabled = false;
        coordinator.Cancel();
        await coordinator.NotifyAsync(viewModel.FilePath);

        Assert.Equal(0, readCount);
        Assert.Equal("baseline", viewModel.Content);
    }

    [Fact]
    public async Task NotifyAsync_AppliesIdenticalContentBeforeReloadNotification()
    {
        var viewModel = CreateViewModel("baseline");
        var callbacks = new List<string>();
        using var coordinator = CreateCoordinator(
            viewModel,
            readFileAsync: _ => Task.FromResult("baseline"),
            applyContent: (vm, content) =>
            {
                callbacks.Add("apply");
                vm.ReplaceContentFromDisk(content);
            },
            onReloaded: _ => callbacks.Add("reloaded"));

        await coordinator.NotifyAsync(viewModel.FilePath);

        Assert.Equal(["apply", "reloaded"], callbacks);
        Assert.Equal("baseline", viewModel.Content);
        Assert.False(viewModel.IsModified);
    }

    private static EditorExternalReloadCoordinator CreateCoordinator(
        EditorTabViewModel viewModel,
        TimeSpan? settleDelay = null,
        Func<string, Task<string>>? readFileAsync = null,
        Func<EditorTabViewModel, bool>? confirmDiscard = null,
        Action<EditorTabViewModel>? onBufferChanged = null,
        Action<EditorTabViewModel, string>? applyContent = null,
        Action<string>? onReloaded = null,
        Func<string, bool>? canReload = null)
    {
        return new EditorExternalReloadCoordinator(
            settleDelay ?? TimeSpan.Zero,
            readFileAsync ?? (_ => Task.FromResult("disk")),
            action =>
            {
                action();
                return Task.CompletedTask;
            },
            path => string.Equals(path, viewModel.FilePath, StringComparison.OrdinalIgnoreCase)
                ? viewModel
                : null,
            confirmDiscard ?? (_ => true),
            _ => { },
            onBufferChanged ?? (_ => { }),
            applyContent ?? ((vm, content) => vm.ReplaceContentFromDisk(content)),
            onReloaded ?? (_ => { }),
            (_, _) => { },
            canReload ?? (path =>
                string.Equals(path, viewModel.FilePath, StringComparison.OrdinalIgnoreCase)));
    }

    private static EditorTabViewModel CreateViewModel(string content)
    {
        var viewModel = new EditorTabViewModel(
            Mock.Of<IFileService>(),
            Mock.Of<IFileSystemService>(),
            Mock.Of<IDialogService>())
        {
            FilePath = @"C:\test.txt",
            FileName = "test.txt",
            Mode = FileOpenMode.Text
        };
        viewModel.SetContentBaseline(content, isModified: false);
        return viewModel;
    }
}
