using System.Text.Json;
using MarkLeaf.Editor;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorCommandStatusTests
{
    [TestMethod]
    public void FromPayload_ParsesFormatPainterFields()
    {
        var payload = CommandStatePayload(canStartFormatPainter: true, formatPainterArmed: false);

        var status = EditorCommandStatus.FromPayload(payload);

        Assert.IsTrue(status.CanStartFormatPainter);
        Assert.IsFalse(status.FormatPainterArmed);
    }

    [TestMethod]
    public void FromPayload_DefaultsFormatPainterFieldsToFalseWhenMissing()
    {
        var payload = CommandStatePayload();

        var status = EditorCommandStatus.FromPayload(payload);

        Assert.IsFalse(status.CanStartFormatPainter);
        Assert.IsFalse(status.FormatPainterArmed);
    }

    private static JsonElement CommandStatePayload(
        bool? canStartFormatPainter = null,
        bool? formatPainterArmed = null)
    {
        var tail = canStartFormatPainter is bool canStart
            ? $",\n      \"canStartFormatPainter\": {canStart.ToString().ToLowerInvariant()}"
            : string.Empty;
        if (formatPainterArmed is bool armed)
        {
            tail += $",\n      \"formatPainterArmed\": {armed.ToString().ToLowerInvariant()}";
        }

        var json = """
            {
              "canUndo": true, "canRedo": false, "hasSelection": false, "paragraph": true,
              "headingLevel": null, "bold": false, "italic": false, "underline": false, "strike": false,
              "code": false, "link": false, "blockquote": false, "codeBlock": false,
              "bulletList": false, "orderedList": false, "taskList": false, "inTable": false,
              "tableAlign": null, "imageSelected": false, "mathInline": false, "mathBlock": false,
              "sourceMode": false, "mathLatex": null
            """ + tail + "\n    }";

        return JsonDocument.Parse(json).RootElement;
    }
}
