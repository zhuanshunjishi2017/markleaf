namespace MarkLeaf.Editor;

/// <summary>Follows awaited menu commands without scoping unrelated UI events.</summary>
internal sealed class EditorMenuCommandScope(Func<Guid> documentId)
{
    private readonly AsyncLocal<Lease?> _current = new();
    internal Lease? Current => _current.Value;

    internal IDisposable Begin(long? menuSessionId, Func<bool> isCurrent)
    {
        var previous = _current.Value;
        var capturedDocument = documentId();
        var lease = new Lease(menuSessionId, () => capturedDocument == documentId() && isCurrent());
        _current.Value = lease;
        return new Cleanup(() => { lease.Active = false; _current.Value = previous; });
    }

    internal sealed class Lease(long? menuSessionId, Func<bool> isCurrent)
    {
        internal bool Active { get; set; } = true;
        internal long? MenuSessionId { get; } = menuSessionId;
        internal bool IsCurrent => Active && isCurrent();
    }

    private sealed class Cleanup(Action cleanup) : IDisposable
    {
        public void Dispose() => cleanup();
    }
}
