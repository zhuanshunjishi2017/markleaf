using System.Text;
using MarkLeaf.Services;

namespace MarkLeaf.UI;

internal static class StatusBarFormatter
{
    public static string FormatCharacterCount(Editor.EditorStatus status)
    {
        return status.SelectedCharacterCount > 0
            ? Loc.Format("statusBar.wordCountWithSelection", status.CharacterCount, status.SelectedCharacterCount)
            : Loc.Format("statusBar.wordCount", status.CharacterCount);
    }

    public static string FormatBlockType(string blockType)
    {
        return blockType switch
        {
            "heading1" => Loc.Get("statusBar.blockType.heading1"),
            "heading2" => Loc.Get("statusBar.blockType.heading2"),
            "heading3" => Loc.Get("statusBar.blockType.heading3"),
            "heading4" => Loc.Get("statusBar.blockType.heading4"),
            "heading5" => Loc.Get("statusBar.blockType.heading5"),
            "heading6" => Loc.Get("statusBar.blockType.heading6"),
            "blockquote" => Loc.Get("statusBar.blockType.quote"),
            "codeBlock" => Loc.Get("statusBar.blockType.codeBlock"),
            "bulletList" => Loc.Get("statusBar.blockType.bulletList"),
            "orderedList" => Loc.Get("statusBar.blockType.orderedList"),
            "taskList" => Loc.Get("statusBar.blockType.taskList"),
            "table" => Loc.Get("statusBar.blockType.table"),
            "image" => Loc.Get("statusBar.blockType.image"),
            "footnoteDefinition" => Loc.Get("statusBar.blockType.footnote"),
            _ => Loc.Get("statusBar.blockType.paragraph"),
        };
    }

    public static string FormatPosition(Editor.EditorStatus status) =>
        Loc.Format("statusBar.position", status.Line, status.Column);

    public static string FormatEncoding(Encoding encoding, bool hasBom)
    {
        return encoding.CodePage switch
        {
            65001 => hasBom ? "UTF-8 BOM" : "UTF-8",
            1200 => "UTF-16 LE",
            1201 => "UTF-16 BE",
            _ => encoding.WebName.ToUpperInvariant(),
        };
    }

    public static string FormatNewLine(string newLine)
    {
        return newLine switch
        {
            "\r\n" => "CRLF",
            "\n" => "LF",
            "\r" => "CR",
            _ => Loc.Get("statusBar.newline.unknown"),
        };
    }
}
