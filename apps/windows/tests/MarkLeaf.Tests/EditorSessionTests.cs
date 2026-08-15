using System.Text.Json;
using MarkLeaf.Editor;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorSessionTests
{
    [TestMethod]
    public void TransitionTo_FollowsRequiredLifecycle()
    {
        var session = new EditorSession();

        session.TransitionTo(EditorLifecycleState.Initializing);
        session.TransitionTo(EditorLifecycleState.LoadingPage);
        session.TransitionTo(EditorLifecycleState.WaitingForEditorReady);
        session.TransitionTo(EditorLifecycleState.Ready);

        Assert.AreEqual(EditorLifecycleState.Ready, session.State);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            session.TransitionTo(EditorLifecycleState.Initializing));
    }

    [TestMethod]
    public void Accept_RejectsForeignAndStaleMessages()
    {
        var session = new EditorSession();
        var documentId = Guid.NewGuid();
        session.StartDocument(documentId, 5);

        Assert.IsFalse(session.Accept(CreateMessage(Guid.NewGuid(), 6)));
        Assert.IsFalse(session.Accept(CreateMessage(documentId, 4)));
        Assert.IsTrue(session.Accept(CreateMessage(documentId, 6)));
        Assert.AreEqual(6, session.ConfirmedRevision);
    }

    [TestMethod]
    public void CompleteRequest_RequiresMatchingIdAndResponseType()
    {
        var session = new EditorSession();
        var requestId = session.RegisterRequest("snapshot");

        Assert.IsFalse(session.CompleteRequest(requestId, "documentLoaded"));
        Assert.IsTrue(session.CompleteRequest(requestId, "snapshot"));
        Assert.AreEqual(0, session.PendingRequestCount);
    }

    [TestMethod]
    public void ResetForRetry_ClearsFailureAndPendingRequests()
    {
        var session = new EditorSession();
        session.RegisterRequest("snapshot");
        session.TransitionTo(EditorLifecycleState.Failed);

        session.ResetForRetry();

        Assert.AreEqual(EditorLifecycleState.NotStarted, session.State);
        Assert.AreEqual(0, session.PendingRequestCount);
    }

    [TestMethod]
    public void TransitionTo_AllowsPageReloadFromReady()
    {
        var session = new EditorSession();
        session.TransitionTo(EditorLifecycleState.Initializing);
        session.TransitionTo(EditorLifecycleState.LoadingPage);
        session.TransitionTo(EditorLifecycleState.WaitingForEditorReady);
        session.TransitionTo(EditorLifecycleState.Ready);

        session.TransitionTo(EditorLifecycleState.LoadingPage);
        session.TransitionTo(EditorLifecycleState.WaitingForEditorReady);
        session.TransitionTo(EditorLifecycleState.Ready);

        Assert.AreEqual(EditorLifecycleState.Ready, session.State);
    }

    private static EditorMessage CreateMessage(Guid documentId, long revision)
    {
        using var payload = JsonDocument.Parse("{}");
        return new EditorMessage(
            EditorProtocol.Version,
            "dirtyChanged",
            null,
            documentId.ToString(),
            revision,
            payload.RootElement.Clone());
    }
}
