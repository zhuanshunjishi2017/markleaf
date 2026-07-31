using System.Text.Json;

namespace MarkLeaf.Editor;

internal sealed record EditorCommandStatus(
    bool CanUndo,
    bool CanRedo,
    bool HasSelection,
    bool Paragraph,
    int? HeadingLevel,
    bool Bold,
    bool Italic,
    bool Link,
    bool Blockquote,
    bool CodeBlock,
    bool BulletList,
    bool OrderedList,
    bool TaskList,
    bool InTable,
    string? TableAlign,
    bool ImageSelected)
{
    public static EditorCommandStatus Empty { get; } = new(
        false, false, false, false, null, false, false, false, false, false, false, false,
        false, false, null, false);

    public static EditorCommandStatus FromPayload(JsonElement payload)
    {
        return new EditorCommandStatus(
            payload.GetProperty("canUndo").GetBoolean(),
            payload.GetProperty("canRedo").GetBoolean(),
            payload.GetProperty("hasSelection").GetBoolean(),
            payload.GetProperty("paragraph").GetBoolean(),
            payload.GetProperty("headingLevel").ValueKind == JsonValueKind.Null
                ? null
                : payload.GetProperty("headingLevel").GetInt32(),
            payload.GetProperty("bold").GetBoolean(),
            payload.GetProperty("italic").GetBoolean(),
            payload.GetProperty("link").GetBoolean(),
            payload.GetProperty("blockquote").GetBoolean(),
            payload.GetProperty("codeBlock").GetBoolean(),
            payload.GetProperty("bulletList").GetBoolean(),
            payload.GetProperty("orderedList").GetBoolean(),
            payload.GetProperty("taskList").GetBoolean(),
            payload.GetProperty("inTable").GetBoolean(),
            payload.GetProperty("tableAlign").ValueKind == JsonValueKind.Null
                ? null
                : payload.GetProperty("tableAlign").GetString(),
            payload.GetProperty("imageSelected").GetBoolean());
    }
}
