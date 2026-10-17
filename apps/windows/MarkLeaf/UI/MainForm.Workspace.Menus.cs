using MarkLeaf.Commands;
using MarkLeaf.Native;
using MarkLeaf.Native.ModernMenus;
using MarkLeaf.Documents;
using MarkLeaf.Services;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private async Task ShowWorkspaceFolderMenuAtAsync(Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot)) return;
        var rootPath = _workspaceRoot;
        var sortModified = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.ModifiedTimeAscending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        var sortDescending = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.FileNameDescending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        var model = HostPopupMenuModel.Workspace(WorkspacePopupTargetKind.Root, command => command switch
        {
            AppCommand.WorkspaceViewTree => new CommandState(true, !_workspaceListViewActive),
            AppCommand.WorkspaceViewList => new CommandState(true, _workspaceListViewActive),
            AppCommand.WorkspaceSortByFileName => new CommandState(true, !sortModified),
            AppCommand.WorkspaceSortByModifiedTime => new CommandState(true, sortModified),
            AppCommand.WorkspaceSortAscending => new CommandState(true, !sortDescending),
            AppCommand.WorkspaceSortDescending => new CommandState(true, sortDescending),
            _ => new CommandState(true),
        }, command => _shortcutManager.GetShortcutText(command) ?? "");
        var command = await _menuService.ShowHostPopupMenuAsync(this, screenPoint, DeviceDpi, _menuDarkMode, model);
        await ExecuteWorkspaceFolderMenuCommandAsync(command, rootPath);
        ClearWorkspaceContextHighlight();
    }

    private async Task ExecuteWorkspaceFolderMenuCommandAsync(AppCommand? command, string rootPath)
    {
        switch (command)
        {
            case AppCommand.WorkspaceNewFile: await CreateUntitledWorkspaceDocumentAsync(rootPath, NewDocumentKind.Markdown); break;
            case AppCommand.WorkspaceNewTextFile: await CreateUntitledWorkspaceDocumentAsync(rootPath, NewDocumentKind.PlainText); break;
            case AppCommand.WorkspaceNewFolder: await CreateUntitledWorkspaceFolderAsync(rootPath); break;
            case AppCommand.WorkspaceCopyPath: CopyWorkspaceEntryPath(rootPath); break;
            case AppCommand.WorkspaceShowInFolder: ShowWorkspaceInExplorer(rootPath); break;
            case AppCommand.WorkspaceViewList: if (!_workspaceListViewActive) ToggleWorkspaceView(); break;
            case AppCommand.WorkspaceViewTree: if (_workspaceListViewActive) ToggleWorkspaceView(); break;
            case AppCommand.WorkspaceSortByFileName: SetWorkspaceSortField(false); break;
            case AppCommand.WorkspaceSortByModifiedTime: SetWorkspaceSortField(true); break;
            case AppCommand.WorkspaceSortAscending: SetWorkspaceSortDirection(false); break;
            case AppCommand.WorkspaceSortDescending: SetWorkspaceSortDirection(true); break;
            case AppCommand.WorkspaceRefresh: await RefreshWorkspaceViewsAsync(); break;
            case AppCommand.WorkspaceCloseFolder: CloseWorkspace(); break;
        }
    }

    private async Task ShowWorkspaceEntryMenuAsync(WorkspaceEntry entry, Point screenPoint)
    {
        var targetDirectory = entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath)!;
        var canModify = _workspaceRoot is null || !PathEquals(entry.FullPath, _workspaceRoot);
        var canCopyContents = !entry.IsDirectory
            && Path.GetExtension(entry.FullPath).ToLowerInvariant() is ".md" or ".markdown" or ".txt";
        var model = HostPopupMenuModel.Workspace(
            entry.IsDirectory ? WorkspacePopupTargetKind.Directory : WorkspacePopupTargetKind.File,
            command => command switch
            {
                AppCommand.WorkspaceCopyContents => new CommandState(canCopyContents),
                AppCommand.WorkspaceRename or AppCommand.WorkspaceTrash => new CommandState(canModify),
                _ => new CommandState(true),
            },
            command => _shortcutManager.GetShortcutText(command) ?? "");
        var command = await _menuService.ShowHostPopupMenuAsync(this, screenPoint, DeviceDpi, _menuDarkMode, model);
        switch (command)
        {
            case AppCommand.WorkspaceOpen:
                await OpenWorkspaceEntryAsync(entry);
                break;
            case AppCommand.WorkspaceOpenInNewWindow:
                StartNewWindow(entry.FullPath);
                break;
            case AppCommand.WorkspaceNewFile:
                await CreateUntitledWorkspaceDocumentAsync(targetDirectory, NewDocumentKind.Markdown);
                break;
            case AppCommand.WorkspaceNewTextFile:
                await CreateUntitledWorkspaceDocumentAsync(targetDirectory, NewDocumentKind.PlainText);
                break;
            case AppCommand.WorkspaceNewFolder:
                await CreateUntitledWorkspaceFolderAsync(targetDirectory);
                break;
            case AppCommand.WorkspaceCopyPath:
                CopyWorkspaceEntryPath(entry.FullPath);
                break;
            case AppCommand.WorkspaceCopyContents:
                await CopyWorkspaceEntryContentsAsync(entry);
                break;
            case AppCommand.WorkspaceShowInFolder:
                ShowWorkspaceEntryInExplorer(entry);
                break;
            case AppCommand.WorkspaceRename:
                BeginWorkspaceEntryRename(entry);
                break;
            case AppCommand.WorkspaceTrash:
                await DeleteWorkspaceEntryAsync(entry);
                break;
        }
        ClearWorkspaceContextHighlight();
    }
}
