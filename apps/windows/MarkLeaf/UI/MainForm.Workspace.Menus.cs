using MarkLeaf.Native;
using MarkLeaf.Documents;
using MarkLeaf.Services;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private enum WorkspacePopupCommand : uint
    {
        NewFile = 0x7001,
        NewFolder,
        ShowInExplorer,
        Refresh,
        SortByFileName,
        SortByModifiedTime,
        SortAscending,
        SortDescending,
        ViewList,
        ViewTree,
        CloseFolder,
        Rename,
        Delete,
        Open,
        OpenInNewWindow,
        CopyPath,
        OpenLocation,
    }

    private async Task ShowWorkspaceFolderMenuAtAsync(Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot)) return;
        var menu = BuildWorkspaceFolderMenu();
        try
        {
            switch (ShowNativeWorkspaceMenu(menu, screenPoint))
            {
                case WorkspacePopupCommand.NewFile: await CreateUntitledWorkspaceDocumentAsync(_workspaceRoot, NewDocumentKind.Markdown); break;
                case WorkspacePopupCommand.NewFolder: await CreateUntitledWorkspaceFolderAsync(_workspaceRoot); break;
                case WorkspacePopupCommand.ShowInExplorer: ShowWorkspaceInExplorer(_workspaceRoot); break;
                case WorkspacePopupCommand.ViewList: if (!_workspaceListViewActive) ToggleWorkspaceView(); break;
                case WorkspacePopupCommand.ViewTree: if (_workspaceListViewActive) ToggleWorkspaceView(); break;
                case WorkspacePopupCommand.SortByFileName: SetWorkspaceSortField(false); break;
                case WorkspacePopupCommand.SortByModifiedTime: SetWorkspaceSortField(true); break;
                case WorkspacePopupCommand.SortAscending: SetWorkspaceSortDirection(false); break;
                case WorkspacePopupCommand.SortDescending: SetWorkspaceSortDirection(true); break;
                case WorkspacePopupCommand.Refresh: await RefreshWorkspaceViewsAsync(); break;
                case WorkspacePopupCommand.CloseFolder: CloseWorkspace(); break;
            }
            ClearWorkspaceContextHighlight();
        }
        finally { NativeMethods.DestroyMenu(menu); }
    }

    private nint BuildWorkspaceFolderMenu()
    {
        var menu = CreateNativePopupMenu();
        try
        {
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.NewFile, Loc.Get("workspaceMenu.newFile"));
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.NewFolder, Loc.Get("workspaceMenu.newFolder"));
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.ShowInExplorer, Loc.Get("workspaceMenu.showInExplorer"));
            AppendNativeMenuSeparator(menu);
            
            AppendNativeViewCommand(menu, WorkspacePopupCommand.ViewTree, Loc.Get("workspaceMenu.treeView"), listView: false);
            AppendNativeViewCommand(menu, WorkspacePopupCommand.ViewList, Loc.Get("workspaceMenu.documentList"), listView: true);
            AppendNativeMenuSeparator(menu);

            var sortMenu = CreateNativePopupMenu();
            AppendNativeSortFieldCommand(sortMenu, WorkspacePopupCommand.SortByFileName, Loc.Get("workspaceMenu.sortByFileName"), false);
            AppendNativeSortFieldCommand(sortMenu, WorkspacePopupCommand.SortByModifiedTime, Loc.Get("workspaceMenu.sortByModifiedTime"), true);
            AppendNativeMenuSeparator(sortMenu);
            AppendNativeSortDirectionCommand(sortMenu, WorkspacePopupCommand.SortAscending, Loc.Get("workspaceMenu.sortAscending"), false);
            AppendNativeSortDirectionCommand(sortMenu, WorkspacePopupCommand.SortDescending, Loc.Get("workspaceMenu.sortDescending"), true);
            AppendNativePopup(menu, Loc.Get("workspaceMenu.sort"), sortMenu);
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.Refresh, Loc.Get("workspaceMenu.refresh"));
            AppendNativeMenuSeparator(menu);
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.CloseFolder, Loc.Get("workspaceMenu.closeFolder"));
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private void AppendNativeViewCommand(nint menu, WorkspacePopupCommand command, string text, bool listView)
    {
        var isActive = _workspaceListViewActive == listView;
        var flags = NativeMethods.MfString | (isActive ? NativeMethods.MfChecked : NativeMethods.MfUnchecked);
        AppendNativeMenu(menu, flags, (nuint)command, text);
    }

    private void AppendNativeSortFieldCommand(nint menu, WorkspacePopupCommand command, string text, bool modifiedTime)
    {
        var isModified = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.ModifiedTimeAscending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        var flags = NativeMethods.MfString | (isModified == modifiedTime ? NativeMethods.MfChecked : NativeMethods.MfUnchecked);
        AppendNativeMenu(menu, flags, (nuint)command, text);
    }

    private void AppendNativeSortDirectionCommand(nint menu, WorkspacePopupCommand command, string text, bool descending)
    {
        var isDescending = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.FileNameDescending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        var flags = NativeMethods.MfString | (isDescending == descending ? NativeMethods.MfChecked : NativeMethods.MfUnchecked);
        AppendNativeMenu(menu, flags, (nuint)command, text);
    }

    private async Task ShowWorkspaceEntryMenuAsync(WorkspaceEntry entry, Point screenPoint)
    {
        var targetDirectory = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath)!;
        var menu = CreateNativePopupMenu();
        try
        {
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.Open, Loc.Get("workspaceEntry.open"));
            if (!entry.IsDirectory)
            {
                AppendNativeMenuCommand(menu, WorkspacePopupCommand.OpenInNewWindow, Loc.Get("workspaceEntry.openInNewWindow"));
            }
            AppendNativeMenuSeparator(menu);
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.NewFile, Loc.Get("workspaceEntry.newFile"));
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.NewFolder, Loc.Get("workspaceEntry.newFolder"));
            AppendNativeMenuSeparator(menu);
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.CopyPath, Loc.Get("workspaceEntry.copyPath"));
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.OpenLocation, Loc.Get("workspaceEntry.openLocation"));
            AppendNativeMenuSeparator(menu);
            var canModify = _workspaceRoot is null || !PathEquals(entry.FullPath, _workspaceRoot);
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.Rename, Loc.Get("workspaceEntry.rename"), canModify);
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.Delete, Loc.Get("workspaceEntry.delete"), canModify);

            switch (ShowNativeWorkspaceMenu(menu, screenPoint))
            {
                case WorkspacePopupCommand.Open:
                    await OpenWorkspaceEntryAsync(entry);
                    break;
                case WorkspacePopupCommand.OpenInNewWindow:
                    StartNewWindow(entry.FullPath);
                    break;
                case WorkspacePopupCommand.NewFile:
                    await CreateUntitledWorkspaceDocumentAsync(targetDirectory, NewDocumentKind.Markdown);
                    break;
                case WorkspacePopupCommand.NewFolder:
                    await CreateUntitledWorkspaceFolderAsync(targetDirectory);
                    break;
                case WorkspacePopupCommand.CopyPath:
                    CopyWorkspaceEntryPath(entry.FullPath);
                    break;
                case WorkspacePopupCommand.OpenLocation:
                    ShowWorkspaceEntryInExplorer(entry);
                    break;
                case WorkspacePopupCommand.Rename:
                    BeginWorkspaceEntryRename(entry);
                    break;
                case WorkspacePopupCommand.Delete:
                    await DeleteWorkspaceEntryAsync(entry);
                    break;
            }
            ClearWorkspaceContextHighlight();
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private WorkspacePopupCommand? ShowNativeWorkspaceMenu(nint menu, Point screenPoint)
    {
        NativeMethods.SetForegroundWindow(Handle);
        var selectedCommand = NativeMethods.TrackPopupMenuEx(
            menu,
            NativeMethods.TpmRightButton | NativeMethods.TpmReturnCommand,
            screenPoint.X,
            screenPoint.Y,
            Handle,
            0);
        NativeMethods.PostMessage(Handle, NativeMethods.WmNull, 0, 0);
        return selectedCommand == 0 ? null : (WorkspacePopupCommand)selectedCommand;
    }

    private static nint CreateNativePopupMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        return menu != 0 ? menu : throw new System.ComponentModel.Win32Exception();
    }

    private static void AppendNativeMenuCommand(
        nint menu,
        WorkspacePopupCommand command,
        string text,
        bool enabled = true)
    {
        var flags = NativeMethods.MfString | (enabled ? NativeMethods.MfEnabled : NativeMethods.MfGrayed);
        AppendNativeMenu(menu, flags, (nuint)command, text);
    }

    private static void AppendNativeMenuSeparator(nint menu)
    {
        AppendNativeMenu(menu, NativeMethods.MfSeparator, 0, null);
    }

    private static void AppendNativePopup(nint menu, string text, nint popup)
    {
        try
        {
            AppendNativeMenu(menu, NativeMethods.MfPopup, (nuint)popup, text);
        }
        catch
        {
            NativeMethods.DestroyMenu(popup);
            throw;
        }
    }

    private static void AppendNativeMenu(nint menu, uint flags, nuint item, string? text)
    {
        if (!NativeMethods.AppendMenu(menu, flags, item, text))
        {
            throw new System.ComponentModel.Win32Exception();
        }
    }
}
