using MarkLeaf.Commands;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class HostPopupMenuModelTests
{
    [TestMethod]
    public void Tabs_UseSecondaryOnlyAndApprovedCommands()
    {
        var model = HostPopupMenuModel.Tabs(_ => new CommandState(true), _ => "Ctrl+K");
        var commands = Commands(model);

        Assert.IsEmpty(model.Primary);
        CollectionAssert.AreEqual(new[]
        {
            AppCommand.SaveDocument, AppCommand.SaveDocumentAs,
            AppCommand.CloseCurrentDocumentTab, AppCommand.CloseOtherDocumentTabs, AppCommand.CloseRightDocumentTabs,
            AppCommand.ReopenClosedTab,
            AppCommand.LocateCurrentDocumentInWorkspace, AppCommand.WorkspaceCopyPath, AppCommand.WorkspaceShowInFolder,
        }, commands);
        Assert.AreEqual("Ctrl+K", Flatten(model.Secondary).Single(x => x.Command == AppCommand.SaveDocument).Shortcut);
    }

    [TestMethod]
    public void Workspace_RootCannotRenameOrTrash()
    {
        var model = HostPopupMenuModel.Workspace(WorkspacePopupTargetKind.Root,
            _ => new CommandState(true), _ => "");
        var commands = Commands(model);

        Assert.IsEmpty(model.Primary);
        Assert.Contains(AppCommand.WorkspaceNewFile, commands);
        Assert.Contains(AppCommand.WorkspaceNewTextFile, commands);
        Assert.Contains(AppCommand.WorkspaceViewTree, commands);
        Assert.Contains(AppCommand.WorkspaceSortByFileName, commands);
        Assert.Contains(AppCommand.WorkspaceCloseFolder, commands);
        Assert.DoesNotContain(AppCommand.WorkspaceRename, commands);
        Assert.DoesNotContain(AppCommand.WorkspaceTrash, commands);
    }

    [TestMethod]
    public void WorkspaceDirectoryOffersDynamicExpandCollapse()
    {
        LocTestHelper.EnsureInitialized();
        var expanded = false;
        var model = HostPopupMenuModel.Workspace(
            WorkspacePopupTargetKind.Directory,
            _ => new CommandState(true),
            _ => "",
            @"C:\workspace\folder",
            _ => expanded);

        Assert.AreEqual("展开(E)", model.Secondary[0].Label);
        expanded = true;
        model = HostPopupMenuModel.Workspace(
            WorkspacePopupTargetKind.Directory,
            _ => new CommandState(true),
            _ => "",
            @"C:\workspace\folder",
            _ => expanded);
        Assert.AreEqual("折叠(C)", model.Secondary[0].Label);
    }

    [TestMethod]
    public void Workspace_TargetsExposeOnlyRelevantActionsAndState()
    {
        var file = Commands(HostPopupMenuModel.Workspace(WorkspacePopupTargetKind.File,
            command => new CommandState(command != AppCommand.WorkspaceCopyContents), _ => ""));
        var directory = Commands(HostPopupMenuModel.Workspace(WorkspacePopupTargetKind.Directory,
            _ => new CommandState(true), _ => ""));

        CollectionAssert.AreEqual(new[]
        {
            AppCommand.WorkspaceOpen, AppCommand.WorkspaceOpenInNewWindow,
            AppCommand.WorkspaceCopyPath, AppCommand.WorkspaceCopyContents, AppCommand.WorkspaceShowInFolder,
            AppCommand.WorkspaceRename, AppCommand.WorkspaceTrash,
        }, file);
        Assert.Contains(AppCommand.WorkspaceNewFile, directory);
        Assert.Contains(AppCommand.WorkspaceNewTextFile, directory);
        Assert.Contains(AppCommand.WorkspaceNewFolder, directory);
        Assert.DoesNotContain(AppCommand.WorkspaceCopyContents, directory);

        var fileModel = HostPopupMenuModel.Workspace(WorkspacePopupTargetKind.File,
            command => new CommandState(command != AppCommand.WorkspaceCopyContents), _ => "");
        Assert.IsFalse(Flatten(fileModel.Secondary).Single(x => x.Command == AppCommand.WorkspaceCopyContents).State.IsEnabled);
    }

    [TestMethod]
    public void Outline_ExposesNavigationAndRefreshCommands()
    {
        var model = HostPopupMenuModel.Outline(_ => new CommandState(true), _ => "");
        var commands = Commands(model);

        Assert.IsEmpty(model.Primary);
        CollectionAssert.AreEqual(new[]
        {
            AppCommand.OutlineExpandAll, AppCommand.OutlineCollapseAll, AppCommand.OutlineLocateCurrent,
        }, commands);
    }

    private static AppCommand[] Commands(EditorMenuModel model) =>
        Flatten(model.Secondary).Where(x => x.Command.HasValue).Select(x => x.Command!.Value).ToArray();

    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> entries) =>
        entries.SelectMany(x => new[] { x }.Concat(Flatten(x.Children)));
}
