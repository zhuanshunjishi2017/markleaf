using System.Text;

namespace MarkLeaf.UI;

internal static class StatusBarFormatter
{
    public static string FormatCharacterCount(Editor.EditorStatus status)
    {
        return status.SelectedCharacterCount > 0
            ? $"字数 {status.CharacterCount}（已选 {status.SelectedCharacterCount}）"
            : $"字数 {status.CharacterCount}";
    }

    public static string FormatBlockType(string blockType)
    {
        return blockType switch
        {
            "heading1" => "标题 1",
            "heading2" => "标题 2",
            "heading3" => "标题 3",
            "heading4" => "标题 4",
            "heading5" => "标题 5",
            "heading6" => "标题 6",
            "blockquote" => "引用",
            "codeBlock" => "代码块",
            "bulletList" => "无序列表",
            "orderedList" => "有序列表",
            "taskList" => "任务列表",
            "table" => "表格",
            "image" => "图片",
            _ => "正文",
        };
    }

    public static string FormatPosition(Editor.EditorStatus status) => $"行 {status.Line}，列 {status.Column}";

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
            _ => "未知换行",
        };
    }
}
