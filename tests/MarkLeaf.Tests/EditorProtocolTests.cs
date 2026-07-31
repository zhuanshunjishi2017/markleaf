using MarkLeaf.Editor;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorProtocolTests
{
    [TestMethod]
    public void TryDeserializeEditorMessage_AcceptsWhitelistedMessage()
    {
        var documentId = Guid.NewGuid();
        var json = $$"""
            {
              "protocolVersion": 1,
              "type": "snapshot",
              "requestId": "request-1",
              "documentId": "{{documentId}}",
              "revision": 4,
              "payload": { "markdown": "# Test" }
            }
            """;

        var accepted = EditorProtocol.TryDeserializeEditorMessage(json, out var message, out var error);

        Assert.IsTrue(accepted, error);
        Assert.IsNotNull(message);
        Assert.AreEqual("snapshot", message.Type);
        Assert.AreEqual(4, message.Revision);
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_RejectsVersionTypeAndDocumentId()
    {
        const string json = """
            {
              "protocolVersion": 2,
              "type": "executeAnything",
              "documentId": "not-a-guid",
              "revision": 0,
              "payload": {}
            }
            """;

        Assert.IsFalse(EditorProtocol.TryDeserializeEditorMessage(json, out _, out var error));
        Assert.AreEqual("Unsupported protocol version.", error);
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_RejectsOversizedMessage()
    {
        var json = new string('x', EditorProtocol.MaximumMessageBytes + 1);

        Assert.IsFalse(EditorProtocol.TryDeserializeEditorMessage(json, out _, out var error));
        Assert.AreEqual("Message exceeds the size limit.", error);
    }

    [TestMethod]
    public void SerializeHostMessage_RejectsUnknownType()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            EditorProtocol.SerializeHostMessage("unknown", Guid.NewGuid(), 0));
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_RejectsInvalidSnapshotPayload()
    {
        var json = $$"""
            {
              "protocolVersion": 1,
              "type": "snapshot",
              "documentId": "{{Guid.NewGuid()}}",
              "revision": 1,
              "payload": { "markdown": 42 }
            }
            """;

        Assert.IsFalse(EditorProtocol.TryDeserializeEditorMessage(json, out _, out var error));
        Assert.AreEqual("Message payload is invalid.", error);
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_AcceptsCompleteCommandStatePayload()
    {
        var json = $$"""
            {
              "protocolVersion": 1,
              "type": "commandStateChanged",
              "documentId": "{{Guid.NewGuid()}}",
              "revision": 2,
              "payload": {
                "canUndo": true,
                "canRedo": false,
                "hasSelection": true,
                "paragraph": false,
                "headingLevel": 2,
                "bold": true,
                "italic": false,
                "link": false,
                "blockquote": false,
                "codeBlock": false,
                "bulletList": false,
                "orderedList": false,
                "taskList": true,
                "inTable": true,
                "tableAlign": "center",
                "imageSelected": true
              }
            }
            """;

        Assert.IsTrue(EditorProtocol.TryDeserializeEditorMessage(json, out var message, out var error), error);
        Assert.AreEqual("commandStateChanged", message!.Type);
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_RejectsIncompleteCommandStatePayload()
    {
        var json = $$"""
            {
              "protocolVersion": 1,
              "type": "commandStateChanged",
              "documentId": "{{Guid.NewGuid()}}",
              "revision": 2,
              "payload": { "canUndo": true }
            }
            """;

        Assert.IsFalse(EditorProtocol.TryDeserializeEditorMessage(json, out _, out var error));
        Assert.AreEqual("Message payload is invalid.", error);
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_ValidatesExternalLinkProtocol()
    {
        var documentId = Guid.NewGuid();
        var allowed = $$"""
            {
              "protocolVersion": 1,
              "type": "openLink",
              "documentId": "{{documentId}}",
              "revision": 1,
              "payload": { "url": "https://example.com" }
            }
            """;
        var blocked = $$"""
            {
              "protocolVersion": 1,
              "type": "openLink",
              "documentId": "{{documentId}}",
              "revision": 1,
              "payload": { "url": "javascript:alert(1)" }
            }
            """;

        Assert.IsTrue(EditorProtocol.TryDeserializeEditorMessage(allowed, out _, out var allowedError), allowedError);
        Assert.IsFalse(EditorProtocol.TryDeserializeEditorMessage(blocked, out _, out var blockedError));
        Assert.AreEqual("Message payload is invalid.", blockedError);
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_AcceptsCommandResultAndAdditionalObjectDropMetadata()
    {
        var documentId = Guid.NewGuid();
        var commandResult = $$"""
            {
              "protocolVersion": 1,
              "type": "commandResult",
              "requestId": "command-1",
              "documentId": "{{documentId}}",
              "revision": 2,
              "payload": { "success": true }
            }
            """;
        var dropFiles = $$"""
            {
              "protocolVersion": 1,
              "type": "dropFiles",
              "documentId": "{{documentId}}",
              "revision": 2,
              "payload": { "count": 2, "clientX": 120.5, "clientY": 240 }
            }
            """;

        Assert.IsTrue(EditorProtocol.TryDeserializeEditorMessage(commandResult, out _, out var commandError), commandError);
        Assert.IsTrue(EditorProtocol.TryDeserializeEditorMessage(dropFiles, out _, out var dropError), dropError);
    }

    [TestMethod]
    public void TryDeserializeEditorMessage_RejectsInvalidDropCount()
    {
        var json = $$"""
            {
              "protocolVersion": 1,
              "type": "dropFiles",
              "documentId": "{{Guid.NewGuid()}}",
              "revision": 1,
              "payload": { "count": 0, "clientX": 0, "clientY": 0 }
            }
            """;

        Assert.IsFalse(EditorProtocol.TryDeserializeEditorMessage(json, out _, out var error));
        Assert.AreEqual("Message payload is invalid.", error);
    }
}
