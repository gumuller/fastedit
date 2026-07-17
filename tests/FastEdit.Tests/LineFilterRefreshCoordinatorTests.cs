using FastEdit.Infrastructure;
using FastEdit.Models;

namespace FastEdit.Tests;

public class LineFilterRefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_DebouncesToLatestRequest()
    {
        using var coordinator = new LineFilterRefreshCoordinator(TimeSpan.FromMilliseconds(30));
        var applied = new List<string>();

        var first = coordinator.RefreshAsync(
            CreateRequest("first"),
            (result, _) =>
            {
                applied.Add(result.Results[1].MatchingFilter!.Pattern);
                return Task.CompletedTask;
            });
        var second = coordinator.RefreshAsync(
            CreateRequest("second"),
            (result, _) =>
            {
                applied.Add(result.Results[1].MatchingFilter!.Pattern);
                return Task.CompletedTask;
            });

        await Task.WhenAll(first, second);

        Assert.Equal(["second"], applied);
    }

    [Fact]
    public async Task Dispose_CancelsPendingRefresh()
    {
        var coordinator = new LineFilterRefreshCoordinator(TimeSpan.FromSeconds(10));
        var applied = false;

        var pending = coordinator.RefreshAsync(
            CreateRequest("pending"),
            (_, _) =>
            {
                applied = true;
                return Task.CompletedTask;
            });
        coordinator.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(applied);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotApplyStaleResultWhenComputationIgnoresCancellation()
    {
        var firstComputationStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstComputation = new TaskCompletionSource<LineFilterRefreshResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var computationCount = 0;
        using var coordinator = new LineFilterRefreshCoordinator(
            TimeSpan.Zero,
            (request, _) =>
            {
                if (Interlocked.Increment(ref computationCount) == 1)
                {
                    firstComputationStarted.SetResult();
                    return releaseFirstComputation.Task;
                }

                return Task.FromResult(CreateResult(request.Lines[0]));
            });
        var applied = new List<string>();

        var first = coordinator.RefreshAsync(
            CreateRequest("stale"),
            (result, _) =>
            {
                applied.Add(result.Results[1].MatchingFilter!.Pattern);
                return Task.CompletedTask;
            });
        await firstComputationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = coordinator.RefreshAsync(
            CreateRequest("current"),
            (result, _) =>
            {
                applied.Add(result.Results[1].MatchingFilter!.Pattern);
                return Task.CompletedTask;
            });
        releaseFirstComputation.SetResult(CreateResult("stale"));

        await Task.WhenAll(first, second);

        Assert.Equal(["current"], applied);
    }

    [Fact]
    public async Task Cancel_PreventsPendingRefreshFromApplying()
    {
        using var coordinator = new LineFilterRefreshCoordinator(TimeSpan.FromSeconds(10));
        var applied = false;

        var pending = coordinator.RefreshAsync(
            CreateRequest("pending"),
            (_, _) =>
            {
                applied = true;
                return Task.CompletedTask;
            });
        coordinator.Cancel();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(applied);
    }

    private static LineFilterRefreshRequest CreateRequest(string value)
    {
        var filter = new LineFilter { Pattern = value };
        return new LineFilterRefreshRequest(
            [value],
            HasActiveFilters: true,
            ShowOnlyFilteredLines: false,
            _ => new LineFilterResult(true, false, filter));
    }

    private static LineFilterRefreshResult CreateResult(string value)
    {
        var filter = new LineFilter { Pattern = value };
        return new LineFilterRefreshResult(
            new Dictionary<int, LineFilterResult>
            {
                [1] = new LineFilterResult(true, false, filter)
            },
            HasActiveFilters: true,
            ShowOnlyFilteredLines: false);
    }
}
