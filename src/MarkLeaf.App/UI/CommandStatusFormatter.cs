using MarkLeaf.Commands;

namespace MarkLeaf.UI;

internal static class CommandStatusFormatter
{
    public static string FormatExecuted(AppCommand command) => $"已执行：{GetDisplayName(command)}";

    private static string GetDisplayName(AppCommand command)
    {
        return command switch
        {
            AppCommand.Undo => "撤销",
            AppCommand.Redo => "重做",
            AppCommand.SetParagraph => "正文",
            AppCommand.SetHeading1 => "一级标题",
            AppCommand.SetHeading2 => "二级标题",
            AppCommand.SetHeading3 => "三级标题",
            AppCommand.SetHeading4 => "四级标题",
            AppCommand.SetHeading5 => "五级标题",
            AppCommand.SetHeading6 => "六级标题",
            AppCommand.ToggleBold => "粗体",
            AppCommand.ToggleItalic => "斜体",
            AppCommand.InsertLink => "插入链接",
            AppCommand.RotateImageClockwise => "顺时针旋转图片",
            AppCommand.ToggleQuote => "引用",
            AppCommand.ToggleCodeBlock => "代码块",
            AppCommand.ToggleBulletList => "无序列表",
            AppCommand.ToggleOrderedList => "有序列表",
            AppCommand.ToggleTaskList => "任务列表",
            AppCommand.InsertHorizontalRule => "水平线",
            AppCommand.InsertTable => "插入表格",
            AppCommand.AddTableRowBefore => "在上方添加行",
            AppCommand.AddTableRowAfter => "在下方添加行",
            AppCommand.DeleteTableRow => "删除当前行",
            AppCommand.AddTableColumnBefore => "在左侧添加列",
            AppCommand.AddTableColumnAfter => "在右侧添加列",
            AppCommand.DeleteTableColumn => "删除当前列",
            AppCommand.AlignTableLeft => "表格左对齐",
            AppCommand.AlignTableCenter => "表格居中对齐",
            AppCommand.AlignTableRight => "表格右对齐",
            AppCommand.DeleteTable => "删除表格",
            _ => "操作",
        };
    }
}
