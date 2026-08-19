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

        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.SetHeading2, context));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleBold, context));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.InsertLink, context));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleBulletList, context));
        Assert.AreEqual(new CommandState(true, false), CommandStateResolver.Resolve(AppCommand.ToggleItalic, context));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.InsertHorizontalRule, context).IsEnabled);
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleTaskList, context));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.InsertTable, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.DeleteTableRow, context).IsEnabled);
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.AlignTableCenter, context));
        Assert.AreEqual(new CommandState(true, false), CommandStateResolver.Resolve(AppCommand.AlignTableLeft, context));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.RotateImageClockwise, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ExportPdf, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ExportHtml, context).IsEnabled);
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
