namespace MarkLeaf.Commands;

public static class CommandStateResolver
{
    public static CommandState Resolve(AppCommand command, CommandContext context)
    {
        return command switch
        {
            AppCommand.Exit or AppCommand.ShowShortcuts or AppCommand.ShowAbout or AppCommand.OpenFolder
                or AppCommand.NewWindow or AppCommand.OpenDocumentInNewWindow
                or AppCommand.RecoverUnsavedFiles => new(true),

            AppCommand.ToggleSidebar => new(!context.FocusMode, context.SidebarVisible),
            AppCommand.ViewTree or AppCommand.ViewList => new(true),
            AppCommand.ShowStatusBar => new(true, context.StatusBarVisible),
            AppCommand.ToggleSourceMode => new(context.EditorReady, context.SourceMode),

            AppCommand.SaveDocument or AppCommand.SaveDocumentAs =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.ExportDocument => new(context.DocumentAvailable && context.EditorReady),
            AppCommand.Undo => new(context.EditorReady && context.CanUndo),
            AppCommand.Redo => new(context.EditorReady && context.CanRedo),
            AppCommand.Cut or AppCommand.Copy or AppCommand.CopyMarkdown or AppCommand.CopyPlainText =>
                new(context.EditorReady && context.HasSelection),
            AppCommand.Paste or AppCommand.Find or AppCommand.Replace => new(context.EditorReady),

            AppCommand.SetParagraph => new(context.EditorReady, context.ParagraphActive),
            AppCommand.SetHeading1 => new(context.EditorReady, context.HeadingLevel == 1),
            AppCommand.SetHeading2 => new(context.EditorReady, context.HeadingLevel == 2),
            AppCommand.SetHeading3 => new(context.EditorReady, context.HeadingLevel == 3),
            AppCommand.SetHeading4 => new(context.EditorReady, context.HeadingLevel == 4),
            AppCommand.SetHeading5 => new(context.EditorReady, context.HeadingLevel == 5),
            AppCommand.SetHeading6 => new(context.EditorReady, context.HeadingLevel == 6),
            AppCommand.PromoteHeading or AppCommand.DemoteHeading => new(context.EditorReady),
            AppCommand.ToggleBold => new(context.EditorReady, context.BoldActive),
            AppCommand.ToggleItalic => new(context.EditorReady, context.ItalicActive),
            AppCommand.ToggleUnderline => new(context.EditorReady, context.UnderlineActive),
            AppCommand.ToggleStrike => new(context.EditorReady, context.StrikeActive),
            AppCommand.ToggleInlineCode => new(context.EditorReady, context.InlineCodeActive),
            AppCommand.InsertLink => new(context.EditorReady, context.LinkActive),
            AppCommand.InsertImage => new(context.DocumentAvailable && context.EditorReady),
            AppCommand.RotateImageClockwise => new(context.EditorReady && context.ImageSelected),
            AppCommand.ToggleQuote => new(context.EditorReady, context.QuoteActive),
            AppCommand.ToggleCodeBlock => new(context.EditorReady, context.CodeBlockActive),
            AppCommand.ToggleBulletList => new(context.EditorReady, context.BulletListActive),
            AppCommand.ToggleOrderedList => new(context.EditorReady, context.OrderedListActive),
            AppCommand.ToggleTaskList => new(context.EditorReady, context.TaskListActive),
            AppCommand.InsertTable => new(context.EditorReady),
            AppCommand.AddTableRowBefore or AppCommand.AddTableRowAfter or AppCommand.DeleteTableRow
                or AppCommand.AddTableColumnBefore or AppCommand.AddTableColumnAfter or AppCommand.DeleteTableColumn
                or AppCommand.DeleteTable => new(context.EditorReady && context.InTable),
            AppCommand.AlignTableLeft => new(context.EditorReady && context.InTable, context.TableAlign == "left"),
            AppCommand.AlignTableCenter => new(context.EditorReady && context.InTable, context.TableAlign == "center"),
            AppCommand.AlignTableRight => new(context.EditorReady && context.InTable, context.TableAlign == "right"),
            AppCommand.SetSerifStyle or AppCommand.SetSansStyle or AppCommand.SetPrintStyle
                or AppCommand.SetRetroPrintStyle => new(context.EditorReady),

            AppCommand.NewDocument or AppCommand.OpenDocument => new(context.EditorReady),
            _ => new(context.EditorReady),
        };
    }
}
