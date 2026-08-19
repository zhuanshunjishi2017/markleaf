using MarkLeaf.Commands;
using MarkLeaf.Services;

namespace MarkLeaf.UI;

internal static class CommandStatusFormatter
{
    public static string FormatExecuted(AppCommand command) => Loc.Format("status.commandExecuted", GetDisplayName(command));

    private static string GetDisplayName(AppCommand command)
    {
        return command switch
        {
            AppCommand.Undo => Loc.Get("cmd.undo"),
            AppCommand.Redo => Loc.Get("cmd.redo"),
            AppCommand.SetParagraph => Loc.Get("cmd.paragraph"),
            AppCommand.SetHeading1 => Loc.Get("cmd.heading1"),
            AppCommand.SetHeading2 => Loc.Get("cmd.heading2"),
            AppCommand.SetHeading3 => Loc.Get("cmd.heading3"),
            AppCommand.SetHeading4 => Loc.Get("cmd.heading4"),
            AppCommand.SetHeading5 => Loc.Get("cmd.heading5"),
            AppCommand.SetHeading6 => Loc.Get("cmd.heading6"),
            AppCommand.ToggleBold => Loc.Get("cmd.bold"),
            AppCommand.ToggleItalic => Loc.Get("cmd.italic"),
            AppCommand.InsertLink => Loc.Get("cmd.insertLink"),
            AppCommand.RotateImageClockwise => Loc.Get("cmd.rotateImage"),
            AppCommand.ToggleQuote => Loc.Get("cmd.quote"),
            AppCommand.ToggleCodeBlock => Loc.Get("cmd.codeBlock"),
            AppCommand.ToggleBulletList => Loc.Get("cmd.bulletList"),
            AppCommand.ToggleOrderedList => Loc.Get("cmd.orderedList"),
            AppCommand.ToggleTaskList => Loc.Get("cmd.taskList"),
            AppCommand.InsertHorizontalRule => Loc.Get("cmd.horizontalRule"),
            AppCommand.InsertTable => Loc.Get("cmd.insertTable"),
            AppCommand.InsertFootnote => Loc.Get("cmd.insertFootnote"),
            AppCommand.ResetFootnoteLabel => Loc.Get("cmd.resetFootnoteLabel"),
            AppCommand.AddTableRowBefore => Loc.Get("cmd.addRowAbove"),
            AppCommand.AddTableRowAfter => Loc.Get("cmd.addRowBelow"),
            AppCommand.DeleteTableRow => Loc.Get("cmd.deleteRow"),
            AppCommand.AddTableColumnBefore => Loc.Get("cmd.addColumnLeft"),
            AppCommand.AddTableColumnAfter => Loc.Get("cmd.addColumnRight"),
            AppCommand.DeleteTableColumn => Loc.Get("cmd.deleteColumn"),
            AppCommand.AlignTableLeft => Loc.Get("cmd.alignLeft"),
            AppCommand.AlignTableCenter => Loc.Get("cmd.alignCenter"),
            AppCommand.AlignTableRight => Loc.Get("cmd.alignRight"),
            AppCommand.DeleteTable => Loc.Get("cmd.deleteTable"),
            AppCommand.FormatPainter => Loc.Get("cmd.formatPainter"),
            _ => Loc.Get("cmd.operation"),
        };
    }
}
