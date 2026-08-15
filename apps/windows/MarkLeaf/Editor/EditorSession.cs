namespace MarkLeaf.Editor;

public sealed class EditorSession
{
    private readonly Dictionary<string, string> _pendingRequests = new(StringComparer.Ordinal);

    public Guid DocumentId { get; private set; } = Guid.NewGuid();

    public long ConfirmedRevision { get; private set; }

    public EditorLifecycleState State { get; private set; } = EditorLifecycleState.NotStarted;

    public int PendingRequestCount => _pendingRequests.Count;

    public void TransitionTo(EditorLifecycleState next)
    {
        if (!IsValidTransition(State, next))
        {
            throw new InvalidOperationException($"Invalid editor lifecycle transition: {State} -> {next}.");
        }

        State = next;
    }

    public void ResetForRetry()
    {
        _pendingRequests.Clear();
        State = EditorLifecycleState.NotStarted;
    }

    public void StartDocument(Guid documentId, long revision = 0)
    {
        DocumentId = documentId;
        ConfirmedRevision = Math.Max(0, revision);
        _pendingRequests.Clear();
    }

    public bool Accept(EditorMessage message, bool allowReadyWithoutDocument = true)
    {
        if (message.Type == "ready" && allowReadyWithoutDocument)
        {
            return true;
        }

        if (!string.Equals(message.DocumentId, DocumentId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (message.Revision < ConfirmedRevision)
        {
            return false;
        }

        ConfirmedRevision = Math.Max(ConfirmedRevision, message.Revision);
        return true;
    }

    public string RegisterRequest(string responseType, string? requestId = null)
    {
        var id = requestId ?? Guid.NewGuid().ToString("N");
        _pendingRequests.Add(id, responseType);
        return id;
    }

    public bool CompleteRequest(string? requestId, string responseType)
    {
        if (string.IsNullOrWhiteSpace(requestId)
            || !_pendingRequests.TryGetValue(requestId, out var expectedType)
            || !string.Equals(expectedType, responseType, StringComparison.Ordinal))
        {
            return false;
        }

        _pendingRequests.Remove(requestId);
        return true;
    }

    private static bool IsValidTransition(EditorLifecycleState current, EditorLifecycleState next)
    {
        if (next == EditorLifecycleState.Failed && current != EditorLifecycleState.Failed)
        {
            return true;
        }

        return (current, next) switch
        {
            (EditorLifecycleState.NotStarted, EditorLifecycleState.Initializing) => true,
            (EditorLifecycleState.Initializing, EditorLifecycleState.LoadingPage) => true,
            (EditorLifecycleState.LoadingPage, EditorLifecycleState.WaitingForEditorReady) => true,
            (EditorLifecycleState.WaitingForEditorReady, EditorLifecycleState.Ready) => true,
            (EditorLifecycleState.Ready, EditorLifecycleState.LoadingPage) => true,
            _ => false,
        };
    }
}
