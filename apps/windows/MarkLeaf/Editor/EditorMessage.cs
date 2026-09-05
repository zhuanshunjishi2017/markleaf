using System.Text.Json;

namespace MarkLeaf.Editor;

public sealed record EditorMessage(
    int ProtocolVersion,
    string Type,
    string? RequestId,
    string DocumentId,
    long Revision,
    JsonElement Payload);

public sealed record EditorSelectionChanged(Guid DocumentId, int From, int To, bool SourceMode);
