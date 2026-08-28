using MarkLeaf.Commands;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CommandStateResolverTests
{
    [TestMethod]
    public void Resolve_Stage2HostCommandsAreEnabledWithoutDocument()
    {
        var context = CreateContext();

        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Exit, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ShowShortcuts, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ShowStatusBar, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_Stage4FileCommandsReflectEditorAndDocumentState()
    {
        var unavailable = CreateContext();
        var editorReady = CreateContext(editorReady: true);
        var documentReady = CreateContext(editorReady: true, documentAvailable: true, documentSaved: true);

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.NewDocument, unavailable).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.NewDocument, editorReady).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.OpenDocument, editorReady).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SaveDocument, editorReady).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SaveDocument, documentReady).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.InsertImage, documentReady).IsEnabled);
    }

    [TestMethod]
    public void Resolve_EditorCommandsReflectEditorState()
    {
        var unavailable = CreateContext();
        var available = CreateContext(editorReady: true, canUndo: true, hasSelection: true);

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.Undo, unavailable).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Undo, available).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Copy, available).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.CopyMarkdown, available).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.CopyPlainText, available).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.PastePlainText, available).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ToggleBold, available).IsEnabled);
    }

    [TestMethod]
    public void Resolve_ViewCommandsExposeCheckedStateAndRespectFocusMode()
    {
        var normal = CreateContext(sidebarVisible: true);
        var focused = CreateContext(sidebarVisible: false, focusMode: true);

        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleSidebar, normal));
        Assert.AreEqual(new CommandState(false, false), CommandStateResolver.Resolve(AppCommand.ToggleSidebar, focused));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ShowStatusBar, focused));
    }

    [TestMethod]
    public void Resolve_IndependentOutlineDisablesSidebarViewSwitching()
    {
        var context = CreateContext(sidebarVisible: true) with
        {
            IndependentOutlineSidebar = true,
        };

        Assert.AreEqual(
            new CommandState(true, true),
            CommandStateResolver.Resolve(AppCommand.UseIndependentOutlineSidebar, context));
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SwitchToWorkspace, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SwitchToOutline, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_FormattingCommandsExposeEditorCheckedState()
    {
        var context = new CommandContext(
            DocumentAvailable: true,
            EditorReady: true,
            CanUndo: true,
            CanRedo: false,
            HasSelection: true,
            SidebarVisible: true,
            FocusMode: false,
            SourceMode: false,
            ParagraphActive: false,
            HeadingLevel: 2,
            BoldActive: true,
            ItalicActive: false,
            LinkActive: true,
            QuoteActive: false,
            CodeBlockActive: false,
            BulletListActive: true,
            OrderedListActive: false,
            TaskListActive: true,
            InTable: true,
            TableAlign: "center",
            ImageSelected: true);

        Assert.AreEqual(new CommandState(false, true), CommandStateResolver.Resolve(AppCommand.SetHeading2, context));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleBold, context));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.InsertLink, context));
        Assert.AreEqual(new CommandState(false, true), CommandStateResolver.Resolve(AppCommand.ToggleBulletList, context));
        Assert.AreEqual(new CommandState(true, false), CommandStateResolver.Resolve(AppCommand.ToggleItalic, context));
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.InsertHorizontalRule, context).IsEnabled);
        Assert.AreEqual(new CommandState(false, true), CommandStateResolver.Resolve(AppCommand.ToggleTaskList, context));
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.InsertTable, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.DeleteTableRow, context).IsEnabled);
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.AlignTableCenter, context));
        Assert.AreEqual(new CommandState(true, false), CommandStateResolver.Resolve(AppCommand.AlignTableLeft, context));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.RotateImageClockwise, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ExportPdf, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ExportHtml, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_ParagraphStructureCommandsAreDisabledInsideTables()
    {
        var context = CreateContext(editorReady: true) with
        {
            InTable = true,
            ParagraphActive = true,
            HeadingLevel = 3,
            QuoteActive = true,
            CodeBlockActive = true,
            BulletListActive = true,
            MermaidCount = 1,
        };
        var disabledCommands = new[]
        {
            AppCommand.SetParagraph,
            AppCommand.SetHeading1,
            AppCommand.SetHeading2,
            AppCommand.SetHeading3,
            AppCommand.SetHeading4,
            AppCommand.SetHeading5,
            AppCommand.SetHeading6,
            AppCommand.PromoteHeading,
            AppCommand.DemoteHeading,
            AppCommand.ToggleQuote,
            AppCommand.InsertMathBlock,
            AppCommand.ToggleCodeBlock,
            AppCommand.InsertHorizontalRule,
            AppCommand.ToggleBulletList,
            AppCommand.ToggleOrderedList,
            AppCommand.ToggleTaskList,
            AppCommand.IncreaseListIndent,
            AppCommand.DecreaseListIndent,
            AppCommand.InsertTable,
            AppCommand.InsertMermaid,
            AppCommand.RerenderAllMermaid,
        };

        foreach (var command in disabledCommands)
        {
            Assert.IsFalse(CommandStateResolver.Resolve(command, context).IsEnabled, command.ToString());
        }

        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.AddTableRowAfter, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.AddTableColumnAfter, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.DeleteTable, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_ListIndentCommandsRequireListParagraph()
    {
        var paragraph = CreateContext(editorReady: true);
        var bulletList = paragraph with { BulletListActive = true };
        var orderedList = paragraph with { OrderedListActive = true };
        var taskList = paragraph with { TaskListActive = true };

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.IncreaseListIndent, paragraph).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.DecreaseListIndent, paragraph).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.IncreaseListIndent, bulletList).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.DecreaseListIndent, orderedList).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.IncreaseListIndent, taskList).IsEnabled);
    }

    [TestMethod]
    public void Resolve_RotateImageRequiresSelectedImage()
    {
        var noImage = CreateContext(editorReady: true);
        var selectedImage = noImage with { ImageSelected = true };

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.RotateImageClockwise, noImage).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.RotateImageClockwise, selectedImage).IsEnabled);
    }

    [TestMethod]
    public void Resolve_SourceModeAndNewWindowCommandsExposeExpectedState()
    {
        var context = CreateContext(editorReady: true, sourceMode: true);

        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.NewWindow, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.OpenDocumentInNewWindow, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ToggleSourceMode, context).IsChecked);
    }

    [TestMethod]
    public void Resolve_FindReplaceAndSourceModeAreEnabledWhenEditorIsReady()
    {
        var context = CreateContext(editorReady: true);

        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Find, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Replace, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ToggleSourceMode, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_ReadOnlyDisablesEditingButAllowsCopyFindAndSourceToggle()
    {
        var context = CreateContext(editorReady: true, hasSelection: true) with { ReadOnly = true };

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.Cut, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.Paste, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.PastePlainText, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleBold, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.Replace, context).IsEnabled);

        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Copy, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.CopyMarkdown, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.CopyPlainText, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Find, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ToggleSourceMode, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SelectAll, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_HeadingBoundaryCommandsAreDisabledAtLevelOneAndSix()
    {
        var levelOne = CreateContext(editorReady: true) with { HeadingLevel = 1 };
        var levelSix = CreateContext(editorReady: true) with { HeadingLevel = 6 };
        var levelThree = CreateContext(editorReady: true) with { HeadingLevel = 3 };

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.PromoteHeading, levelOne).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.DemoteHeading, levelSix).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.PromoteHeading, levelThree).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.DemoteHeading, levelThree).IsEnabled);
    }

    [TestMethod]
    public void Resolve_FormatPainterReflectsArmState()
    {
        var unavailable = CreateContext();
        var canStart = CreateContext(editorReady: true) with { CanStartFormatPainter = true };
        var armed = CreateContext(editorReady: true) with { FormatPainterArmed = true };

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.FormatPainter, unavailable).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.FormatPainter, canStart).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.FormatPainter, canStart).IsChecked);
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.FormatPainter, armed));
    }

    private static CommandContext CreateContext(
        bool editorReady = false,
        bool canUndo = false,
        bool hasSelection = false,
        bool sidebarVisible = true,
        bool focusMode = false,
        bool documentAvailable = false,
        bool documentSaved = false,
        bool sourceMode = false)
    {
        return new CommandContext(
            DocumentAvailable: documentAvailable,
            EditorReady: editorReady,
            CanUndo: canUndo,
            CanRedo: false,
            HasSelection: hasSelection,
            SidebarVisible: sidebarVisible,
            FocusMode: focusMode,
            SourceMode: sourceMode,
            DocumentSaved: documentSaved);
    }
}
