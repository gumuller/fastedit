namespace FastEdit.Infrastructure;

public sealed class DebouncedActionCoordinator : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly object _sync = new();
    private CancellationTokenSource? _current;
    private bool _disposed;

    public DebouncedActionCoordinator(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));

        _delay = delay;
    }

    public async Task RunAsync(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        CancellationTokenSource request;
        CancellationToken cancellationToken;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CancelCurrent();
            request = new CancellationTokenSource();
            cancellationToken = request.Token;
            _current = request;
        }

        try
        {
            await Task.Delay(_delay, cancellationToken);
            await action(cancellationToken);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_current, request))
                    _current = null;
            }

            request.Dispose();
        }
    }

    public void Cancel()
    {
        lock (_sync)
            CancelCurrent();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            CancelCurrent();
        }
    }

    private void CancelCurrent()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
    }
}
