namespace MarkLeaf.Commands;

public static class CommandStateResolver
{
    public static CommandState Resolve(AppCommand command, CommandContext context)
    {
        return command switch
        {
            AppCommand.Exit or AppCommand.ShowShortcuts or AppCommand.ShowPreferences
                or AppCommand.ShowAbout or AppCommand.ShowChangelog
                or AppCommand.OpenThemeFolder or AppCommand.AddTheme
                or AppCommand.OpenFolder
                or AppCommand.NewWindow or AppCommand.OpenDocumentInNewWindow
                or AppCommand.RecoverUnsavedFiles
                or AppCommand.FollowSystemColorMode => new(true),
            AppCommand.ShowCodeHighlight => new(context.EditorReady, context.ShowCodeHighlight),

            AppCommand.ToggleSidebar => new(!context.FocusMode, context.SidebarVisible),
            AppCommand.ToggleFocusMode => new(true, context.FocusMode),
            AppCommand.ViewTree => new(true, !context.ListViewActive),
            AppCommand.ViewList => new(true, context.ListViewActive),
            AppCommand.ShowStatusBar => new(true, context.StatusBarVisible),
            AppCommand.ToggleSourceMode => new(context.EditorReady && !context.IsPlainText, context.SourceMode),
            AppCommand.SwitchToWorkspace => new(!context.FocusMode && context.SidebarVisible, !context.OutlineActive),
            AppCommand.SwitchToOutline => new(!context.FocusMode && context.SidebarVisible, context.OutlineActive),

            AppCommand.SaveDocument or AppCommand.SaveDocumentAs =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.ExportWithLastSettings or AppCommand.ExportPdf or AppCommand.ExportHtml or AppCommand.Print =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.Undo => new(context.EditorReady && !context.ReadOnly && context.CanUndo),
            AppCommand.Redo => new(context.EditorReady && !context.ReadOnly && context.CanRedo),
            AppCommand.Cut => new(context.EditorReady && !context.ReadOnly && context.HasSelection),
            AppCommand.Copy or AppCommand.CopyMarkdown or AppCommand.CopyPlainText =>
                new(context.EditorReady && context.HasSelection),
            AppCommand.Paste or AppCommand.PastePlainText => new(context.EditorReady && !context.ReadOnly),
            AppCommand.Find => new(context.EditorReady),
            AppCommand.Replace => new(context.EditorReady && !context.ReadOnly),

            AppCommand.SetParagraph => new(context.EditorReady && !context.ReadOnly, context.ParagraphActive),
            AppCommand.SetHeading1 => new(context.EditorReady && !context.ReadOnly, context.HeadingLevel == 1),
            AppCommand.SetHeading2 => new(context.EditorReady && !context.ReadOnly, context.HeadingLevel == 2),
            AppCommand.SetHeading3 => new(context.EditorReady && !context.ReadOnly, context.HeadingLevel == 3),
            AppCommand.SetHeading4 => new(context.EditorReady && !context.ReadOnly, context.HeadingLevel == 4),
            AppCommand.SetHeading5 => new(context.EditorReady && !context.ReadOnly, context.HeadingLevel == 5),
            AppCommand.SetHeading6 => new(context.EditorReady && !context.ReadOnly, context.HeadingLevel == 6),
            AppCommand.PromoteHeading => new(context.EditorReady && !context.ReadOnly && context.HeadingLevel != 1),
            AppCommand.DemoteHeading => new(
                context.EditorReady && !context.ReadOnly
                && context.HeadingLevel is >= 1 and < 6),
            AppCommand.ToggleBold => new(context.EditorReady && !context.ReadOnly, context.BoldActive),
            AppCommand.ToggleItalic => new(context.EditorReady && !context.ReadOnly, context.ItalicActive),
            AppCommand.ToggleUnderline => new(context.EditorReady && !context.ReadOnly, context.UnderlineActive),
            AppCommand.ToggleStrike => new(context.EditorReady && !context.ReadOnly, context.StrikeActive),
            AppCommand.ToggleInlineCode => new(context.EditorReady && !context.ReadOnly, context.InlineCodeActive),
            AppCommand.InsertLink => new(context.EditorReady && !context.ReadOnly, context.LinkActive),
            AppCommand.InsertImage => new(context.DocumentAvailable && context.EditorReady && !context.ReadOnly),
            AppCommand.InsertImageFromUrl => new(context.DocumentAvailable && context.EditorReady && !context.ReadOnly),
            AppCommand.RotateImageClockwise => new(context.EditorReady && !context.ReadOnly && context.ImageSelected),
            AppCommand.ChangeImage or AppCommand.SaveImageAs
                or AppCommand.ResizeImage100 or AppCommand.ResizeImage50
                or AppCommand.ResizeImage75 or AppCommand.ResizeImage90
                => new(context.EditorReady && !context.ReadOnly && context.ImageSelected),
            AppCommand.ToggleQuote => new(context.EditorReady && !context.ReadOnly, context.QuoteActive),
            AppCommand.ToggleCodeBlock => new(context.EditorReady && !context.ReadOnly, context.CodeBlockActive),
            AppCommand.DeclareCodeLanguage => new(context.EditorReady && !context.ReadOnly && context.CodeBlockActive),
            AppCommand.CopyCodeBlock => new(context.EditorReady && context.CodeBlockActive && !string.IsNullOrEmpty(context.CodeBlockText)),
            AppCommand.ToggleBulletList => new(context.EditorReady && !context.ReadOnly, context.BulletListActive),
            AppCommand.ToggleOrderedList => new(context.EditorReady && !context.ReadOnly, context.OrderedListActive),
            AppCommand.ToggleTaskList => new(context.EditorReady && !context.ReadOnly, context.TaskListActive),
            AppCommand.InsertTable => new(context.EditorReady && !context.ReadOnly),
            AppCommand.InsertMermaid => new(context.EditorReady && !context.ReadOnly),
            AppCommand.EditMermaid or AppCommand.RerenderMermaid or AppCommand.DeleteMermaid =>
                new(context.EditorReady && !context.ReadOnly && context.MermaidSelected),
            AppCommand.RerenderAllMermaid =>
                new(context.EditorReady && context.MermaidCount > 0),
            AppCommand.InsertFootnote => new(context.EditorReady && !context.ReadOnly),
            AppCommand.ResetFootnoteLabel or AppCommand.GoToFootnoteReference
                or AppCommand.ClearFootnoteReferences or AppCommand.DeleteFootnote =>
                new(context.EditorReady && !context.ReadOnly && !string.IsNullOrWhiteSpace(context.FootnoteDefinitionLabel)),
            AppCommand.ClearFormat => new(context.EditorReady && !context.ReadOnly),
            AppCommand.FormatPainter => new(
                context.EditorReady && !context.ReadOnly && (context.CanStartFormatPainter || context.FormatPainterArmed),
                context.FormatPainterArmed),
            AppCommand.AddTableRowBefore or AppCommand.AddTableRowAfter or AppCommand.DeleteTableRow
                or AppCommand.AddTableColumnBefore or AppCommand.AddTableColumnAfter or AppCommand.DeleteTableColumn
                or AppCommand.DeleteTable => new(context.EditorReady && !context.ReadOnly && context.InTable),
            AppCommand.AlignTableLeft => new(context.EditorReady && !context.ReadOnly && context.InTable, context.TableAlign == "left"),
            AppCommand.AlignTableCenter => new(context.EditorReady && !context.ReadOnly && context.InTable, context.TableAlign == "center"),
            AppCommand.AlignTableRight => new(context.EditorReady && !context.ReadOnly && context.InTable, context.TableAlign == "right"),

            AppCommand.ZoomIn or AppCommand.ZoomOut or AppCommand.ZoomReset => new(context.EditorReady),

            AppCommand.NewDocument or AppCommand.NewPlainTextDocument or AppCommand.OpenDocument or AppCommand.OpenDocumentReadOnly => new(context.EditorReady),
            _ => new(context.EditorReady),
        };
    }
}
