namespace MarkLeaf.Workspace;

internal sealed class WorkspaceChangeDebouncer : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly TimeSpan _delay;
    private readonly Action _callback;
    private readonly object _sync = new();

    public WorkspaceChangeDebouncer(TimeSpan delay, Action callback)
    {
        _delay = delay;
        _callback = callback;
        _timer = new System.Threading.Timer(_ => _callback());
    }

    public void Signal()
    {
        lock (_sync)
        {
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose() => _timer.Dispose();
}
