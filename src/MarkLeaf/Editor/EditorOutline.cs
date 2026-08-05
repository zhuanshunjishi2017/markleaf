using System.Text.Json;

namespace MarkLeaf.Editor;

internal sealed record EditorOutlineItem(int Level, string Text, int Position);

internal sealed record EditorOutline(IReadOnlyList<EditorOutlineItem> Headings)
{
    public static EditorOutline FromPayload(JsonElement payload)
    {
        return new EditorOutline(
            payload.GetProperty("headings")
                .EnumerateArray()
                .Select(heading => new EditorOutlineItem(
                    heading.GetProperty("level").GetInt32(),
                    heading.GetProperty("text").GetString() ?? string.Empty,
                    heading.GetProperty("position").GetInt32()))
                .ToArray());
    }
}
