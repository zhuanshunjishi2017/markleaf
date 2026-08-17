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
    bool Underline,
    bool Strike,
    bool InlineCode,
    bool Link,
    bool Blockquote,
    bool CodeBlock,
    bool BulletList,
    bool OrderedList,
    bool TaskList,
    bool InTable,
    string? TableAlign,
    bool ImageSelected,
    bool MathInline,
    bool MathBlock,
    bool SourceMode,
    string? MathLatex,
    string? MathNumber,
    string? Caption,
    bool CanStartFormatPainter,
    bool FormatPainterArmed)
{
    public static EditorCommandStatus Empty { get; } = new(
        false, false, false, false, null, false, false, false, false, false, false, false, false, false, false,
        false, false, null, false, false, false, false, null, null, null, false, false);

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
            payload.GetProperty("underline").GetBoolean(),
            payload.GetProperty("strike").GetBoolean(),
            payload.GetProperty("code").GetBoolean(),
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
            payload.GetProperty("imageSelected").GetBoolean(),
            payload.GetProperty("mathInline").GetBoolean(),
            payload.GetProperty("mathBlock").GetBoolean(),
            payload.GetProperty("sourceMode").GetBoolean(),
            payload.GetProperty("mathLatex").ValueKind == JsonValueKind.Null
                ? null
                : payload.GetProperty("mathLatex").GetString(),
            payload.TryGetProperty("mathNumber", out var mathNumberProp) && mathNumberProp.ValueKind == JsonValueKind.String
                ? mathNumberProp.GetString()
                : null,
            payload.TryGetProperty("caption", out var captionProp) && captionProp.ValueKind == JsonValueKind.String
                ? captionProp.GetString()
                : null,
            payload.TryGetProperty("canStartFormatPainter", out var canStart) && canStart.ValueKind == JsonValueKind.True,
            payload.TryGetProperty("formatPainterArmed", out var armed) && armed.ValueKind == JsonValueKind.True);
    }
}
