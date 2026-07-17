namespace FastEdit.Infrastructure;

public sealed class ExternalChangeGenerationTracker
{
    private readonly object _sync = new();
    private long _generation;

    public long RecordChange()
    {
        lock (_sync)
            return ++_generation;
    }

    public long Capture()
    {
        lock (_sync)
            return _generation;
    }

    public bool TryApply(long generation, Action apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        lock (_sync)
        {
            if (_generation != generation)
                return false;

            apply();
            return true;
        }
    }
}
