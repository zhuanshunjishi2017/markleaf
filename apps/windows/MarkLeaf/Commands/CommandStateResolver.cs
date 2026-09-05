namespace MarkLeaf.Commands;

public static class CommandStateResolver
{
    public static CommandState Resolve(AppCommand command, CommandContext context)
    {
        // The editor host can remain initialized briefly after the last tab is
        // closed. Do not let its stale selection/status re-enable editing
        // commands when there is no document to edit.
        if (!context.DocumentAvailable && IsDocumentEditingCommand(command))
        {
            return new(false);
        }

        return command switch
        {
            AppCommand.Exit or AppCommand.ShowShortcuts or AppCommand.ShowPreferences
                or AppCommand.ShowAbout or AppCommand.ShowChangelog or AppCommand.ShowWelcome
                or AppCommand.LearnMarkdown
                or AppCommand.CheckForUpdates
                or AppCommand.OpenThemeFolder or AppCommand.AddTheme
                or AppCommand.OpenFolder
                or AppCommand.NewWindow or AppCommand.OpenDocumentInNewWindow
                or AppCommand.RecoverUnsavedFiles
                or AppCommand.FollowSystemColorMode => new(true),
            AppCommand.ShowCodeHighlight => new(context.EditorReady, context.ShowCodeHighlight),

            AppCommand.ToggleSidebar => new(!context.FocusMode, context.SidebarVisible),
            AppCommand.ToggleFocusMode => new(true, context.FocusMode),
            AppCommand.ToggleEditorFullScreen => new(true, context.EditorFullScreen),
            AppCommand.ToggleEditorFocusMode => new(
                context.EditorReady && !context.SourceMode,
                context.EditorFocusMode),
            AppCommand.ToggleEditorTypewriterMode => new(
                context.EditorReady && !context.IsPlainText && !context.SourceMode,
                context.EditorTypewriterMode),
            AppCommand.ViewTree => new(true, !context.ListViewActive),
            AppCommand.ViewList => new(true, context.ListViewActive),
            AppCommand.UseIndependentOutlineSidebar => new(true, context.IndependentOutlineSidebar),
            AppCommand.ShowStatusBar => new(true, context.StatusBarVisible),
            AppCommand.ToggleSourceMode => new(context.EditorReady && !context.IsPlainText, context.SourceMode),
            AppCommand.SwitchToWorkspace => new(
                !context.FocusMode && context.SidebarVisible && !context.IndependentOutlineSidebar,
                !context.OutlineActive),
            AppCommand.SwitchToOutline => new(
                !context.FocusMode && context.SidebarVisible && !context.IndependentOutlineSidebar,
                context.OutlineActive),

            AppCommand.SaveDocument or AppCommand.SaveDocumentAs =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.ExportWithLastSettings or AppCommand.ExportPdf or AppCommand.ExportHtml
                or AppCommand.ExportImage or AppCommand.Print =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.Undo => new(context.EditorReady && !context.ReadOnly && context.CanUndo),
            AppCommand.Redo => new(context.EditorReady && !context.ReadOnly && context.CanRedo),
            AppCommand.Cut => new(context.EditorReady && !context.ReadOnly && context.HasSelection),
            AppCommand.Copy or AppCommand.CopyMarkdown or AppCommand.CopyPlainText =>
                new(context.EditorReady && context.HasSelection),
            AppCommand.CopyHtml => new(context.EditorReady && context.HasSelection && !context.SourceMode),
            AppCommand.Paste or AppCommand.PastePlainText => new(context.EditorReady && !context.ReadOnly),
            AppCommand.Find => new(context.EditorReady),
            AppCommand.Replace => new(context.EditorReady && !context.ReadOnly),

            AppCommand.SetParagraph => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.ParagraphActive),
            AppCommand.SetHeading1 => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.HeadingLevel == 1),
            AppCommand.SetHeading2 => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.HeadingLevel == 2),
            AppCommand.SetHeading3 => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.HeadingLevel == 3),
            AppCommand.SetHeading4 => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.HeadingLevel == 4),
            AppCommand.SetHeading5 => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.HeadingLevel == 5),
            AppCommand.SetHeading6 => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.HeadingLevel == 6),
            AppCommand.PromoteHeading => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable && context.HeadingLevel != 1),
            AppCommand.DemoteHeading => new(
                context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable
                && context.HeadingLevel is >= 1 and < 6),
            AppCommand.ToggleBold => new(context.EditorReady && !context.ReadOnly && !context.SourceMode, context.BoldActive),
            AppCommand.ToggleItalic => new(context.EditorReady && !context.ReadOnly && !context.SourceMode, context.ItalicActive),
            AppCommand.ToggleUnderline => new(context.EditorReady && !context.ReadOnly && !context.SourceMode, context.UnderlineActive),
            AppCommand.ToggleStrike => new(context.EditorReady && !context.ReadOnly && !context.SourceMode, context.StrikeActive),
            AppCommand.ToggleHighlight => new(context.EditorReady && !context.ReadOnly && !context.SourceMode, context.HighlightActive),
            AppCommand.ToggleInlineCode => new(context.EditorReady && !context.ReadOnly && !context.SourceMode, context.InlineCodeActive),
            AppCommand.InsertLink => new(context.EditorReady && !context.ReadOnly && !context.SourceMode, context.LinkActive),
            AppCommand.InsertImage => new(context.DocumentAvailable && context.EditorReady && !context.ReadOnly && !context.SourceMode),
            AppCommand.InsertImageFromUrl => new(context.DocumentAvailable && context.EditorReady && !context.ReadOnly && !context.SourceMode),
            AppCommand.RotateImageClockwise => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.ImageSelected),
            AppCommand.ChangeImage or AppCommand.SaveImageAs
                or AppCommand.ResizeImage100 or AppCommand.ResizeImage50
                or AppCommand.ResizeImage75 or AppCommand.ResizeImage90
                => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.ImageSelected),
            AppCommand.ToggleQuote => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.QuoteActive),
            AppCommand.ToggleCodeBlock => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.CodeBlockActive),
            AppCommand.DeclareCodeLanguage => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.CodeBlockActive),
            AppCommand.CopyCodeBlock => new(
                context.EditorReady && (context.CodeBlockActive || context.FrontMatterActive)
                && !string.IsNullOrEmpty(context.CodeBlockText)),
            AppCommand.ExitCode => new(
                context.EditorReady && !context.ReadOnly && !context.SourceMode
                && (context.CodeBlockActive || context.FrontMatterActive)),
            AppCommand.ToggleBulletList => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.BulletListActive),
            AppCommand.ToggleOrderedList => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.OrderedListActive),
            AppCommand.ToggleTaskList => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable, context.TaskListActive),
            AppCommand.IncreaseListIndent or AppCommand.DecreaseListIndent => new(
                context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable
                && (context.BulletListActive || context.OrderedListActive || context.TaskListActive)),
            // Inline formulas are valid inline content in table cells. Only
            // block-level insertion remains unavailable inside a table.
            AppCommand.InsertMathInline =>
                new(context.EditorReady && !context.ReadOnly && !context.SourceMode),
            AppCommand.InsertMathBlock or AppCommand.InsertHorizontalRule =>
                new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable),
            AppCommand.InsertTable => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable),
            AppCommand.InsertMermaid => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable),
            AppCommand.ShowFrontMatter => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.IsPlainText),
            AppCommand.InsertAlertNote or AppCommand.InsertAlertTip or AppCommand.InsertAlertImportant
                or AppCommand.InsertAlertWarning or AppCommand.InsertAlertCaution =>
                new(context.EditorReady && !context.ReadOnly && !context.SourceMode && !context.InTable),
            AppCommand.EditMermaid or AppCommand.RerenderMermaid or AppCommand.DeleteMermaid =>
                new(context.EditorReady && !context.ReadOnly && context.MermaidSelected),
            AppCommand.SetMathNumber => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.MathBlock),
            AppCommand.RerenderAllMermaid =>
                new(context.EditorReady && !context.SourceMode && !context.InTable && context.MermaidCount > 0),
            AppCommand.InsertFootnote => new(context.EditorReady && !context.ReadOnly && !context.SourceMode),
            AppCommand.ResetFootnoteLabel or AppCommand.GoToFootnoteReference
                or AppCommand.ClearFootnoteReferences or AppCommand.DeleteFootnote =>
                new(context.EditorReady && !context.ReadOnly && !string.IsNullOrWhiteSpace(context.FootnoteDefinitionLabel)),
            AppCommand.ClearFormat => new(context.EditorReady && !context.ReadOnly && !context.SourceMode),
            AppCommand.FormatPainter => new(
                context.EditorReady && !context.ReadOnly && !context.SourceMode
                && (context.CanStartFormatPainter || context.FormatPainterArmed),
                context.FormatPainterArmed),
            AppCommand.AddTableRowBefore or AppCommand.AddTableRowAfter or AppCommand.DeleteTableRow
                or AppCommand.AddTableColumnBefore or AppCommand.AddTableColumnAfter or AppCommand.DeleteTableColumn
                or AppCommand.DeleteTable => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.InTable),
            AppCommand.AlignTableLeft => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.InTable, context.TableAlign == "left"),
            AppCommand.AlignTableCenter => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.InTable, context.TableAlign == "center"),
            AppCommand.AlignTableRight => new(context.EditorReady && !context.ReadOnly && !context.SourceMode && context.InTable, context.TableAlign == "right"),

            AppCommand.InsertLineBefore or AppCommand.InsertLineAfter
                or AppCommand.DuplicateParagraph or AppCommand.DeleteParagraph =>
                new(context.EditorReady && !context.ReadOnly && !context.SourceMode),

            AppCommand.ZoomIn or AppCommand.ZoomOut or AppCommand.ZoomReset => new(context.EditorReady),
            AppCommand.RestartEditor => new(context.EditorReady),

            AppCommand.SwitchDocumentTab1 or AppCommand.SwitchDocumentTab2
                or AppCommand.SwitchDocumentTab3 or AppCommand.SwitchDocumentTab4
                or AppCommand.SwitchDocumentTab5 or AppCommand.SwitchDocumentTab6
                or AppCommand.SwitchDocumentTab7 or AppCommand.SwitchDocumentTab8
                or AppCommand.SwitchDocumentTab9 or AppCommand.CloseCurrentDocumentTab
                or AppCommand.CloseOtherDocumentTabs or AppCommand.SwitchToNextDocumentTab
                or AppCommand.LocateCurrentDocumentInWorkspace
                => new(context.DocumentAvailable),

            AppCommand.NewDocument or AppCommand.OpenDocument or AppCommand.OpenDocumentReadOnly => new(true),
            _ => new(context.EditorReady),
        };
    }

    private static bool IsDocumentEditingCommand(AppCommand command) =>
        command is >= AppCommand.Undo and <= AppCommand.Replace
            or >= AppCommand.SetParagraph and <= AppCommand.DeleteTable
            or AppCommand.ToggleUnderline or AppCommand.ToggleStrike or AppCommand.ToggleHighlight
            or AppCommand.ToggleInlineCode or AppCommand.PromoteHeading or AppCommand.DemoteHeading
            or AppCommand.InsertLineBefore or AppCommand.InsertLineAfter
            or AppCommand.DuplicateParagraph or AppCommand.DeleteParagraph
            or AppCommand.InsertMathInline or AppCommand.InsertMathBlock
            or AppCommand.InsertMermaid or AppCommand.InsertFootnote
            or AppCommand.CopyHtml or AppCommand.SelectAll or AppCommand.ToggleSourceMode
            or AppCommand.ShowFrontMatter
            or AppCommand.InsertAlertNote or AppCommand.InsertAlertTip
            or AppCommand.InsertAlertImportant or AppCommand.InsertAlertWarning
            or AppCommand.InsertAlertCaution
            or AppCommand.ResetFootnoteLabel or AppCommand.GoToFootnoteReference
            or AppCommand.ClearFootnoteReferences or AppCommand.DeleteFootnote
            or AppCommand.ClearFormat or AppCommand.FormatPainter
            or AppCommand.EditMath or AppCommand.ConvertMath or AppCommand.DeleteMath
            or AppCommand.SetMathNumber or AppCommand.EditMermaid
            or AppCommand.RerenderMermaid or AppCommand.DeleteMermaid
            or AppCommand.RerenderAllMermaid or AppCommand.DeclareCodeLanguage
            or AppCommand.CopyCodeBlock or AppCommand.ExitCode
            or AppCommand.ChangeImage or AppCommand.SaveImageAs
            or AppCommand.ResizeImage100 or AppCommand.ResizeImage50
            or AppCommand.ResizeImage75 or AppCommand.ResizeImage90;

}
