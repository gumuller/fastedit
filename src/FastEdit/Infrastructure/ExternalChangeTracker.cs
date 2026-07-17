namespace FastEdit.Infrastructure;

public sealed class ExternalChangeTracker
{
    private readonly object _sync = new();
    private long _generation;
    private string? _pendingPath;

    public void Record(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        lock (_sync)
        {
            _generation++;
            _pendingPath = filePath;
        }
    }

    public ExternalChangeSnapshot Capture(string fallbackPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(fallbackPath);
        lock (_sync)
        {
            var snapshot = new ExternalChangeSnapshot(
                _generation,
                _pendingPath ?? fallbackPath);
            _pendingPath = null;
            return snapshot;
        }
    }

    public bool TryApply(long generation, Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        lock (_sync)
        {
            if (generation != _generation)
                return false;

            apply();
            return true;
        }
    }

    public string? TakePendingPath()
    {
        lock (_sync)
        {
            var pendingPath = _pendingPath;
            _pendingPath = null;
            return pendingPath;
        }
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _generation++;
            _pendingPath = null;
        }
    }
}

public readonly record struct ExternalChangeSnapshot(
    long Generation,
    string FilePath);
