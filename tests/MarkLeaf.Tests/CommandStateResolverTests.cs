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
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ToggleFocusMode, context).IsEnabled);
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
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.CleanUnreferencedAssets, documentReady).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.CleanUnreferencedAssets, editorReady).IsEnabled);
    }

    [TestMethod]
    public void Resolve_EditorCommandsReflectEditorState()
    {
        var unavailable = CreateContext();
        var available = CreateContext(editorReady: true, canUndo: true, hasSelection: true);

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.Undo, unavailable).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Undo, available).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Copy, available).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ToggleBold, available).IsEnabled);
    }

    [TestMethod]
    public void Resolve_ViewCommandsExposeCheckedStateAndRespectFocusMode()
    {
        var normal = CreateContext(workspaceVisible: true, outlineVisible: false);
        var focused = CreateContext(workspaceVisible: false, outlineVisible: false, focusMode: true);

        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleWorkspace, normal));
        Assert.AreEqual(new CommandState(true, false), CommandStateResolver.Resolve(AppCommand.ToggleOutline, normal));
        Assert.AreEqual(new CommandState(false, false), CommandStateResolver.Resolve(AppCommand.ToggleWorkspace, focused));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleFocusMode, focused));
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
            WorkspaceVisible: true,
            OutlineVisible: true,
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
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ExportDocument, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_RotateImageRequiresSelectedImage()
    {
        var noImage = CreateContext(editorReady: true);
        var selectedImage = noImage with { ImageSelected = true };

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.RotateImageClockwise, noImage).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.RotateImageClockwise, selectedImage).IsEnabled);
    }

    private static CommandContext CreateContext(
        bool editorReady = false,
        bool canUndo = false,
        bool hasSelection = false,
        bool workspaceVisible = true,
        bool outlineVisible = true,
        bool focusMode = false,
        bool documentAvailable = false,
        bool documentSaved = false)
    {
        return new CommandContext(
            DocumentAvailable: documentAvailable,
            EditorReady: editorReady,
            CanUndo: canUndo,
            CanRedo: false,
            HasSelection: hasSelection,
            WorkspaceVisible: workspaceVisible,
            OutlineVisible: outlineVisible,
            FocusMode: focusMode,
            SourceMode: false,
            DocumentSaved: documentSaved);
    }
}
