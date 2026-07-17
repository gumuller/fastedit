using FastEdit.Models;

namespace FastEdit.Infrastructure;

internal sealed class LineFilterRefreshCoordinator : IDisposable
{
    private readonly DebouncedActionCoordinator _debouncer;
    private readonly Func<LineFilterRefreshRequest, CancellationToken, Task<LineFilterRefreshResult>> _computeAsync;
    private long _generation;
    private bool _disposed;

    public LineFilterRefreshCoordinator(
        TimeSpan delay,
        Func<LineFilterRefreshRequest, CancellationToken, Task<LineFilterRefreshResult>>? computeAsync = null)
    {
        _debouncer = new DebouncedActionCoordinator(delay);
        _computeAsync = computeAsync ?? ((request, cancellationToken) =>
            Task.Run(() => Compute(request, cancellationToken), cancellationToken));
    }

    public Task RefreshAsync(
        Func<CancellationToken, Task<LineFilterRefreshRequest>> snapshotProviderAsync,
        Func<LineFilterRefreshResult, CancellationToken, Task> applyAsync)
    {
        ArgumentNullException.ThrowIfNull(snapshotProviderAsync);
        ArgumentNullException.ThrowIfNull(applyAsync);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var generation = Interlocked.Increment(ref _generation);
        return _debouncer.RunAsync(async cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _generation))
                return;

            var request = await snapshotProviderAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _generation))
                return;

            var result = await _computeAsync(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != Volatile.Read(ref _generation))
                return;

            await applyAsync(result, cancellationToken);
        });
    }

    public void Cancel()
    {
        Interlocked.Increment(ref _generation);
        _debouncer.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Increment(ref _generation);
        _debouncer.Dispose();
    }

    private static LineFilterRefreshResult Compute(
        LineFilterRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<int, LineFilterResult>();
        if (request.HasActiveFilters)
        {
            for (var index = 0; index < request.Lines.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = request.EvaluateLine(request.Lines[index]);
                if (result != LineFilterResult.NoMatch)
                    results[index + 1] = result;
            }
        }

        return new LineFilterRefreshResult(
            results,
            request.HasActiveFilters,
            request.ShowOnlyFilteredLines);
    }
}

internal sealed record LineFilterRefreshRequest(
    IReadOnlyList<string> Lines,
    bool HasActiveFilters,
    bool ShowOnlyFilteredLines,
    Func<string, LineFilterResult> EvaluateLine);

internal sealed record LineFilterRefreshResult(
    Dictionary<int, LineFilterResult> Results,
    bool HasActiveFilters,
    bool ShowOnlyFilteredLines);
