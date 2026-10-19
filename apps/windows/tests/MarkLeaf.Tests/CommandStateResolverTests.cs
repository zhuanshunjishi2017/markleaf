using MarkLeaf.Commands;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CommandStateResolverTests
{
    [TestMethod]
    public void CopyFileContentsDependsOnContentRatherThanWhetherTheDocumentIsSaved()
    {
        var untitled = new CommandContext(DocumentAvailable: true, EditorReady: true,
            DocumentHasPath: false, DocumentHasContent: true);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.CopyCurrentFileContents, untitled).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.CopyCurrentFileContents,
            untitled with { DocumentHasPath = true, ReadOnlyMode = true }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.CopyCurrentFileContents,
            untitled with { DocumentHasContent = false }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.CopyCurrentFileContents,
            untitled with { DocumentAvailable = false }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.CopyCurrentFileContents,
            untitled with { EditorReady = false }).IsEnabled);
    }

    [TestMethod]
    public void Resolve_HostCommandsUseWindowAndDocumentFacts()
    {
        var empty = new CommandContext(DocumentAvailable: false, EditorReady: false);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.NewDocument, empty).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Exit, empty).IsEnabled);
        Assert.AreEqual(new CommandState(true, false), CommandStateResolver.Resolve(AppCommand.ShowCodeHighlight, empty));
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(
            AppCommand.FollowSystemColorMode, empty with { FollowSystemColorMode = true }));
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SaveDocument, empty).IsEnabled);
        var ready = empty with { DocumentAvailable = true, EditorReady = true, SidebarVisible = true };
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(
            AppCommand.ShowCodeHighlight, ready with { ShowCodeHighlight = true }));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SaveDocument, ready).IsEnabled);
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleSidebar, ready));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.ToggleSidebar, ready with { FocusMode = true }).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SwitchToOutline, ready with { IndependentOutlineSidebar = true }).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SwitchToWorkspace, ready with { SidebarVisible = false, FocusMode = true }).IsEnabled);
    }

    [TestMethod]
    public void Resolve_EmptyWindowUsesTheSharedAvailabilityMatrix()
    {
        var empty = new CommandContext(DocumentAvailable: false, EditorReady: false);
        foreach (var command in new[]
        {
            AppCommand.NewDocument,
            AppCommand.NewPlainText,
            AppCommand.OpenDocument,
            AppCommand.OpenDocumentReadOnly,
            AppCommand.OpenDocumentInNewWindow,
            AppCommand.OpenFolder,
            AppCommand.ShowThemeSettings,
            AppCommand.ToggleFocusMode,
            AppCommand.ToggleEditorFullScreen,
            AppCommand.ToggleSidebar,
            AppCommand.ShowStatusBar,
        })
        {
            Assert.IsTrue(CommandStateResolver.Resolve(command, empty).IsEnabled, $"{command} must stay enabled without tabs.");
        }

        foreach (var command in new[]
        {
            AppCommand.SaveDocument,
            AppCommand.SaveAllDocuments,
            AppCommand.ExportDocument,
            AppCommand.Print,
            AppCommand.ToggleBold,
            AppCommand.ToggleReadOnlyMode,
            AppCommand.ToggleEditorFocusMode,
            AppCommand.ZoomIn,
            AppCommand.RestartEditor,
            AppCommand.CloseCurrentDocumentTab,
            AppCommand.CopyCurrentFilePath,
        })
        {
            Assert.IsFalse(CommandStateResolver.Resolve(command, empty).IsEnabled, $"{command} must be disabled without tabs.");
        }

        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.CloseFolder, empty).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(
            AppCommand.CloseFolder, empty with { WorkspaceAvailable = true }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ReopenClosedTab, empty).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(
            AppCommand.ReopenClosedTab, empty with { ClosedTabsAvailable = true }).IsEnabled);
    }

    [TestMethod]
    public void Resolve_EditorCommandsConsumeProjectedEnabledAndCheckedState()
    {
        var context = new CommandContext(DocumentAvailable: true, EditorReady: true, SelectionScope: "cells",
            EditorActions: new Dictionary<string, CommandState>
            {
                ["toggleBold"] = new(true, true),
                ["setHeading2"] = new(false, true),
                ["setLink"] = new(true),
                ["addRowAfter"] = new(true),
            });
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleBold, context));
        Assert.AreEqual(new CommandState(false, true), CommandStateResolver.Resolve(AppCommand.SetHeading2, context));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.InsertLink, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.AddTableRowAfter, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.AddTableRowAfter, context with { SelectionScope = null }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.AddTableRowAfter, context with { SelectionScope = "mixed" }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.DeleteTable, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_MissingProjectionOrUnloadedDocumentCannotEnableEditorCommands()
    {
        var context = new CommandContext(DocumentAvailable: true, EditorReady: true);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleBold, context).IsEnabled);
        context = context with { EditorActions = new Dictionary<string, CommandState> { ["toggleBold"] = new(true) } };
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleBold, context with { DocumentAvailable = false }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleBold, context with { EditorReady = false }).IsEnabled);
    }

    [TestMethod]
    public void Resolve_FocusAndTypewriterTogglesUseHostModeForCheckedState()
    {
        var context = new CommandContext(
            DocumentAvailable: true,
            EditorReady: true,
            EditorActions: new Dictionary<string, CommandState>
            {
                ["setEditorFocusMode"] = new(true),
                ["setEditorTypewriterMode"] = new(true),
            },
            EditorFocusMode: true,
            EditorTypewriterMode: false);

        Assert.AreEqual(new CommandState(true, true),
            CommandStateResolver.Resolve(AppCommand.ToggleEditorFocusMode, context));
        Assert.AreEqual(new CommandState(true, false),
            CommandStateResolver.Resolve(AppCommand.ToggleEditorTypewriterMode, context));
        Assert.AreEqual(new CommandState(true, false),
            CommandStateResolver.Resolve(AppCommand.ToggleEditorFocusMode,
                context with { EditorFocusMode = false }));
    }
}
