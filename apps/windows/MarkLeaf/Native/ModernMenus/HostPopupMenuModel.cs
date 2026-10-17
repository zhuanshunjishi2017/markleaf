using MarkLeaf.Commands;
using MarkLeaf.Services;

namespace MarkLeaf.Native.ModernMenus;

internal enum WorkspacePopupTargetKind { File, Directory, Root }

/// <summary>Presentation-neutral factories for non-editor host context menus.</summary>
internal static class HostPopupMenuModel
{
    public static EditorMenuModel Tabs(
        Func<AppCommand, CommandState> state, Func<AppCommand, string> shortcut)
    {
        var items = new List<EditorMenuEntry>
        {
            Command(AppCommand.SaveDocument, "menu.file.save", state, shortcut),
            Command(AppCommand.SaveDocumentAs, "menu.file.saveAs", state, shortcut),
            EditorMenuEntry.Separator,
            Command(AppCommand.CloseCurrentDocumentTab, "documentTabMenu.close", state, shortcut),
            Command(AppCommand.CloseOtherDocumentTabs, "documentTabMenu.closeOthers", state, shortcut),
            Command(AppCommand.CloseRightDocumentTabs, "documentTabMenu.closeRight", state, shortcut),
            Command(AppCommand.ReopenClosedTab, "menu.view.reopenClosedTab", state, shortcut),
            EditorMenuEntry.Separator,
            Command(AppCommand.LocateCurrentDocumentInWorkspace, "documentTabMenu.locate", state, shortcut),
            Command(AppCommand.WorkspaceCopyPath, "documentTabMenu.copyPath", state, shortcut),
            Command(AppCommand.WorkspaceShowInFolder, "documentTabMenu.showInExplorer", state, shortcut),
        };
        return Model(items);
    }

    public static EditorMenuModel Workspace(
        WorkspacePopupTargetKind kind,
        Func<AppCommand, CommandState> state,
        Func<AppCommand, string> shortcut,
        string? targetPath = null,
        Func<string, bool>? directoryExpanded = null)
    {
        if (kind == WorkspacePopupTargetKind.File)
        {
            return Model([
                Command(AppCommand.WorkspaceOpen, "workspaceEntry.open", state, shortcut),
                Command(AppCommand.WorkspaceOpenInNewWindow, "workspaceEntry.openInNewWindow", state, shortcut),
                EditorMenuEntry.Separator,
                Command(AppCommand.WorkspaceCopyPath, "workspaceEntry.copyPath", state, shortcut),
                Command(AppCommand.WorkspaceCopyContents, "workspaceEntry.copyContents", state, shortcut),
                Command(AppCommand.WorkspaceShowInFolder, "workspaceEntry.openLocation", state, shortcut),
                EditorMenuEntry.Separator,
                Command(AppCommand.WorkspaceRename, "workspaceEntry.rename", state, shortcut),
                Command(AppCommand.WorkspaceTrash, "workspaceEntry.delete", state, shortcut),
            ]);
        }

        if (kind == WorkspacePopupTargetKind.Directory)
        {
            var create = new EditorMenuEntry(EditorMenuEntryKind.Submenu, null,
                Loc.Get("workspaceMenu.new"), "", new(true), [
                    Command(AppCommand.WorkspaceNewFile, "workspaceMenu.newFile", state, shortcut),
                    Command(AppCommand.WorkspaceNewTextFile, "workspaceMenu.newTextFile", state, shortcut),
                ]);
            return Model([
                Command(
                    AppCommand.WorkspaceOpen,
                    directoryExpanded?.Invoke(targetPath!) == true
                        ? "workspaceMenu.collapseFolder"
                        : "workspaceMenu.expandFolder",
                    state,
                    shortcut),
                Command(AppCommand.WorkspaceOpenInNewWindow, "workspaceEntry.openInNewWindow", state, shortcut),
                EditorMenuEntry.Separator,
                create,
                Command(AppCommand.WorkspaceNewFolder, "workspaceEntry.newFolder", state, shortcut),
                EditorMenuEntry.Separator,
                Command(AppCommand.WorkspaceCopyPath, "workspaceEntry.copyPath", state, shortcut),
                Command(AppCommand.WorkspaceShowInFolder, "workspaceEntry.openLocation", state, shortcut),
                EditorMenuEntry.Separator,
                Command(AppCommand.WorkspaceRename, "workspaceEntry.rename", state, shortcut),
                Command(AppCommand.WorkspaceTrash, "workspaceEntry.delete", state, shortcut),
            ]);
        }

        var sort = new EditorMenuEntry(EditorMenuEntryKind.Submenu, null,
            Loc.Get("workspaceMenu.sort"), "", new(true), [
                Command(AppCommand.WorkspaceSortByFileName, "workspaceMenu.sortByFileName", state, shortcut),
                Command(AppCommand.WorkspaceSortByModifiedTime, "workspaceMenu.sortByModifiedTime", state, shortcut),
                EditorMenuEntry.Separator,
                Command(AppCommand.WorkspaceSortAscending, "workspaceMenu.sortAscending", state, shortcut),
                Command(AppCommand.WorkspaceSortDescending, "workspaceMenu.sortDescending", state, shortcut),
            ]);
        var rootCreate = new EditorMenuEntry(EditorMenuEntryKind.Submenu, null,
            Loc.Get("workspaceMenu.new"), "", new(true), [
                Command(AppCommand.WorkspaceNewFile, "workspaceMenu.newFile", state, shortcut),
                Command(AppCommand.WorkspaceNewTextFile, "workspaceMenu.newTextFile", state, shortcut),
            ]);
        return Model([
            rootCreate,
            Command(AppCommand.WorkspaceNewFolder, "workspaceMenu.newFolder", state, shortcut),
            EditorMenuEntry.Separator,
            Command(AppCommand.WorkspaceViewTree, "workspaceMenu.treeView", state, shortcut),
            Command(AppCommand.WorkspaceViewList, "workspaceMenu.documentList", state, shortcut),
            sort,
            EditorMenuEntry.Separator,
            Command(AppCommand.WorkspaceCopyPath, "workspaceEntry.copyPath", state, shortcut),
            Command(AppCommand.WorkspaceShowInFolder, "workspaceMenu.showInExplorer", state, shortcut),
            Command(AppCommand.WorkspaceRefresh, "workspaceMenu.refresh", state, shortcut),
            EditorMenuEntry.Separator,
            Command(AppCommand.WorkspaceCloseFolder, "workspaceMenu.closeFolder", state, shortcut),
        ]);
    }

    public static EditorMenuModel Outline(
        Func<AppCommand, CommandState> state, Func<AppCommand, string> shortcut) =>
        Model([
            Command(AppCommand.OutlineExpandAll, "outlineMenu.expandAll", state, shortcut),
            Command(AppCommand.OutlineCollapseAll, "outlineMenu.collapseAll", state, shortcut),
            EditorMenuEntry.Separator,
            Command(AppCommand.OutlineLocateCurrent, "outlineMenu.locateCurrent", state, shortcut),
        ]);

    private static EditorMenuModel Model(IReadOnlyList<EditorMenuEntry> items) =>
        new(Array.Empty<EditorMenuEntry>(), items);

    private static EditorMenuEntry Command(
        AppCommand command,
        string labelKey,
        Func<AppCommand, CommandState> state,
        Func<AppCommand, string> shortcut) => new(
            EditorMenuEntryKind.Command,
            command,
            Loc.Get(labelKey).Split('\t')[0].Replace("&", ""),
            shortcut(command),
            state(command),
            Array.Empty<EditorMenuEntry>());
}
