using System.Text.Json;

namespace MarkLeaf.Editor;

internal sealed record EditorStatus(
    int CharacterCount,
    int SelectedCharacterCount,
    int TotalCharacterCount,
    int NonWhitespaceCharacterCount,
    int CjkCharacterCount,
    int WesternWordCount,
    int FormulaCount,
    int CodeLineCount,
    int ParagraphCount,
    string BlockType,
    int Line,
    int Column)
{
    public static EditorStatus Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, "paragraph", 1, 1);

    public static EditorStatus FromPayload(JsonElement payload)
    {
        return new EditorStatus(
            payload.GetProperty("characterCount").GetInt32(),
            payload.GetProperty("selectedCharacterCount").GetInt32(),
            GetOptionalInt(payload, "totalCharacterCount"),
            GetOptionalInt(payload, "nonWhitespaceCharacterCount"),
            GetOptionalInt(payload, "cjkCharacterCount"),
            GetOptionalInt(payload, "westernWordCount"),
            GetOptionalInt(payload, "formulaCount"),
            GetOptionalInt(payload, "codeLineCount"),
            GetOptionalInt(payload, "paragraphCount"),
            payload.GetProperty("blockType").GetString() ?? "paragraph",
            payload.GetProperty("line").GetInt32(),
            payload.GetProperty("column").GetInt32());
    }

    private static int GetOptionalInt(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;
}

internal sealed record EditorContextMenuRequest(
    double ClientX,
    double ClientY,
    double MenuHeight,
    bool CanStartFormatPainter,
    bool FormatPainterArmed,
    bool ReadOnly);

internal sealed record EditorBlockMenuRequest(double ClientX, double ClientY, int Position);

internal sealed record UnsafeEmphasisRequest(string RequestId, string Kind);
