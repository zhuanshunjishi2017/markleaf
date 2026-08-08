using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using MarkLeaf.Commands;
using MarkLeaf.Native;
using MarkLeaf.UI.Dialogs;
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

    private IReadOnlyList<string> GetRecentWorkspaces()
    {
        if (!_settings.File.RecordRecentFolders)
        {
            return [];
        }

        return _settings.Workspace.RecentFolders
            .Prepend(_workspaceRoot ?? _settings.Workspace.LastFolder)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(TryGetFullPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private IReadOnlyList<string> GetRecentFiles()
    {
        if (!_settings.File.RecordRecentFiles)
        {
            return [];
        }

        return _settings.Workspace.RecentFiles
            .Select(TryGetFullPath)
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static string? TryGetFullPath(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private bool TryGetRecentWorkspace(AppCommand command, out string path)
    {
        path = string.Empty;
        if (command is < AppCommand.OpenRecentWorkspace1 or > AppCommand.OpenRecentWorkspace8)
        {
            return false;
        }

        var index = (int)command - (int)AppCommand.OpenRecentWorkspace1;
        var recent = GetRecentWorkspaces();
        if (index < 0 || index >= recent.Count)
        {
            return false;
        }

        path = recent[index];
        return true;
    }

    private bool TryGetRecentFile(AppCommand command, out string path)
    {
        path = string.Empty;
        if (command is < AppCommand.OpenRecentFile1 or > AppCommand.OpenRecentFile8)
        {
            return false;
        }

        var index = (int)command - (int)AppCommand.OpenRecentFile1;
        var recent = GetRecentFiles();
        if (index < 0 || index >= recent.Count)
        {
            return false;
        }

        path = recent[index];
        return true;
    }

    private async Task SelectWorkspaceFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = Loc.Get("dialog.selectWorkspace"),
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
            SelectedPath = _workspaceRoot ?? _settings.Workspace.LastFolder ?? string.Empty,
        };
        if (ShowModal(() => dialog.ShowDialog(this)) == DialogResult.OK)
        {
            await OpenWorkspaceAsync(dialog.SelectedPath);
        }
    }

    private async Task OpenWorkspaceAsync(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            ShowMessage(this, Loc.Get("workspace.directoryNotExist"), "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _workspaceLoadCancellation?.Cancel();
        _workspaceLoadCancellation?.Dispose();
        _workspaceLoadCancellation = new CancellationTokenSource();
        _workspaceRoot = fullPath;
        AddRecentWorkspace(fullPath);
        _openFolderPrompt.Visible = false;
        if (!_focusMode && _sidebarSplit.Panel1Collapsed)
        {
            ExpandSidebar();
        }

        var rootName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = fullPath;
        }
        _workspaceTree.SetRoot(new WorkspaceEntry(rootName, fullPath, true));
        await LoadWorkspaceDirectoryAsync(fullPath, _workspaceLoadCancellation.Token);
        await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation.Token);
        if (_workspaceLoadCancellation.IsCancellationRequested || !PathEquals(_workspaceRoot, fullPath))
        {
            return;
        }
        _workspaceTree.Expand(fullPath);
        TryStartWatchingWorkspace(fullPath);
        SetStatus(Loc.Format("status.workspaceOpened", Path.GetFileName(fullPath)));
        _menuService.RefreshStates();
    }

    private void CloseWorkspace()
    {
        _workspaceLoadCancellation?.Cancel();
        _workspaceLoadCancellation?.Dispose();
        _workspaceLoadCancellation = null;
        StopWatchingWorkspace();
        _workspaceRoot = null;
        _settings.Workspace.LastFolder = null;
        _sidebarVisibleBeforeFocus = false;
        ClearWorkspacePlaceholder();
        _openFolderPrompt.Visible = true;
        CollapseSidebar();
        SetStatus(Loc.Get("status.workspaceClosed"));
        _menuService.RefreshStates();
    }

    private void ClearWorkspacePlaceholder()
    {
        _workspaceTree.SetPlaceholder("");
        _workspaceDocumentList.PlaceholderText = "";
        _workspaceDocumentList.SelectedPath = null;
        _workspaceDocuments = [];
        _workspaceDocumentList.SetDocuments([]);
    }

    private void ShowNoWorkspacePlaceholder()
    {
        ClearWorkspacePlaceholder();
        _openFolderPrompt.Visible = true;
        _openFolderPrompt.BringToFront();
    }

    private void ToggleWorkspaceView()
    {
        _workspaceListViewActive = !_workspaceListViewActive;
        _workspaceTree.Visible = !_workspaceListViewActive;
        _workspaceDocumentList.Visible = _workspaceListViewActive;
        if (_workspaceListViewActive)
        {
            _workspaceDocumentList.BringToFront();
            _workspaceDocumentList.Focus();
        }
        else
        {
            _workspaceTree.BringToFront();
            _workspaceTree.Focus();
        }
        UpdateViewToggleIcon();
        if (_workspaceRoot is null && _openFolderPrompt.Visible)
            _openFolderPrompt.BringToFront();
        SetStatus(_workspaceListViewActive ? Loc.Get("status.switchedToList") : Loc.Get("status.switchedToTree"));
    }

    private async Task ShowWorkspaceFolderMenuAtAsync(Point screenPoint)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot)) return;
        var menu = BuildWorkspaceFolderMenu();
        try
        {
            switch (ShowNativeWorkspaceMenu(menu, screenPoint))
            {
                case WorkspacePopupCommand.NewFile: await CreateWorkspaceEntryAsync(_workspaceRoot, false); break;
                case WorkspacePopupCommand.NewFolder: await CreateWorkspaceEntryAsync(_workspaceRoot, true); break;
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
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.ViewTree, Loc.Get("workspaceMenu.treeView"));
            AppendNativeMenuCommand(menu, WorkspacePopupCommand.ViewList, Loc.Get("workspaceMenu.documentList"));
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

    private void SetWorkspaceSortField(bool modifiedTime)
    {
        var descending = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.FileNameDescending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        SetWorkspaceDocumentSortOrder(modifiedTime
            ? (descending ? WorkspaceDocumentSortOrder.ModifiedTimeDescending : WorkspaceDocumentSortOrder.ModifiedTimeAscending)
            : (descending ? WorkspaceDocumentSortOrder.FileNameDescending : WorkspaceDocumentSortOrder.FileNameAscending));
    }

    private void SetWorkspaceSortDirection(bool descending)
    {
        var modifiedTime = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.ModifiedTimeAscending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        SetWorkspaceDocumentSortOrder(modifiedTime
            ? (descending ? WorkspaceDocumentSortOrder.ModifiedTimeDescending : WorkspaceDocumentSortOrder.ModifiedTimeAscending)
            : (descending ? WorkspaceDocumentSortOrder.FileNameDescending : WorkspaceDocumentSortOrder.FileNameAscending));
    }

    private void SetWorkspaceDocumentSortOrder(WorkspaceDocumentSortOrder sortOrder)
    {
        _workspaceDocumentSortOrder = sortOrder;
        _workspaceTree.SetSortOrder(sortOrder);
        ApplyWorkspaceDocumentSort();
        SetStatus(Loc.Format("status.workspaceSortChanged", GetWorkspaceSortDescription(sortOrder)));
    }

    private static string GetWorkspaceSortDescription(WorkspaceDocumentSortOrder sortOrder)
    {
        return sortOrder switch
        {
            WorkspaceDocumentSortOrder.FileNameAscending => Loc.Get("workspace.sortFileNameAscending"),
            WorkspaceDocumentSortOrder.FileNameDescending => Loc.Get("workspace.sortFileNameDescending"),
            WorkspaceDocumentSortOrder.ModifiedTimeAscending => Loc.Get("workspace.sortModifiedTimeAscending"),
            _ => Loc.Get("workspace.sortModifiedTimeDescending"),
        };
    }

    private void ApplyWorkspaceDocumentSort()
    {
        IEnumerable<WorkspaceDocumentEntry> documents = _workspaceDocumentSortOrder switch
        {
            WorkspaceDocumentSortOrder.FileNameAscending => _workspaceDocuments
                .OrderBy(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
            WorkspaceDocumentSortOrder.FileNameDescending => _workspaceDocuments
                .OrderByDescending(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
            WorkspaceDocumentSortOrder.ModifiedTimeAscending => _workspaceDocuments
                .OrderBy(document => document.LastWriteTime)
                .ThenBy(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => _workspaceDocuments
                .OrderByDescending(document => document.LastWriteTime)
                .ThenBy(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        _workspaceDocumentList.SetDocuments(documents.ToArray());
        _workspaceDocumentList.SelectedPath = _document?.FilePath;
    }

    private void ShowWorkspaceInExplorer(string workspacePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add(workspacePath);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private async Task RefreshWorkspaceViewsAsync()
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            return;
        }

        await LoadWorkspaceDirectoryAsync(_workspaceRoot, _workspaceLoadCancellation?.Token ?? CancellationToken.None);
        _workspaceTree.Expand(_workspaceRoot);
        await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
        SetStatus(Loc.Get("status.workspaceRefreshed"));
    }

    private async Task CreateUntitledWorkspaceDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot) || _documentOperationInProgress)
        {
            return;
        }

        if (!await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        try
        {
            var path = _workspaceService.GetAvailableUntitledDocumentPath(_workspaceRoot);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.FlushAsync();
            }

            await RefreshWorkspaceDirectoryAsync(_workspaceRoot);
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
            await OpenDocumentPathAsync(path);
            _workspaceDocumentList.SelectedPath = path;
            SetStatus(Loc.Format("status.documentCreated", Path.GetFileName(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private async Task RefreshWorkspaceDocumentListAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot))
        {
            _workspaceDocumentList.PlaceholderText = Loc.Get("sidebar.noWorkspace");
            _workspaceDocuments = [];
            _workspaceDocumentList.SetDocuments([]);
            return;
        }

        try
        {
            var documents = await _workspaceService.GetDocumentsAsync(_workspaceRoot, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            _workspaceDocumentList.PlaceholderText = Loc.Get("sidebar.noDocuments");
            _workspaceDocuments = documents;
            ApplyWorkspaceDocumentSort();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _workspaceDocumentList.PlaceholderText = Loc.Get("workspace.readFailed");
            _workspaceDocuments = [];
            _workspaceDocumentList.SetDocuments([]);
            _logger.Warning($"Workspace documents could not be enumerated: {exception.GetType().Name}.");
        }
    }

    private async Task ActivateWorkspaceDocumentAsync(string path)
    {
        if (_document?.FilePath is not null && PathEquals(_document.FilePath, path))
        {
            _workspaceDocumentList.SelectedPath = _document.FilePath;
            return;
        }

        if (_documentOperationInProgress)
        {
            _workspaceDocumentList.SelectedPath = _document?.FilePath;
            return;
        }

        if (!await ConfirmDiscardOrSaveAsync())
        {
            _workspaceDocumentList.SelectedPath = _document?.FilePath;
            return;
        }

        await OpenDocumentPathAsync(path);
        _workspaceDocumentList.SelectedPath = _document?.FilePath;
    }

    private void AddRecentWorkspace(string path)
    {
        _settings.Workspace.LastFolder = path;
        if (!_settings.File.RecordRecentFolders)
        {
            return;
        }

        _settings.Workspace.RecentFolders = _settings.Workspace.RecentFolders
            .Where(item => !string.Equals(item, path, StringComparison.OrdinalIgnoreCase))
            .Prepend(path)
            .Take(8)
            .ToList();
    }

    private async Task LoadWorkspaceDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            var children = await _workspaceService.GetChildrenAsync(directory, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            _workspaceTree.SetChildren(directory, children);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _workspaceTree.SetLoadError(directory, Loc.Get("workspace.folderReadFailed"));
            _logger.Warning($"Workspace folder could not be enumerated: {exception.GetType().Name}.");
        }
    }

    private async Task ActivateWorkspaceTreeEntryAsync(WorkspaceEntry entry)
    {
        if (entry.IsDirectory)
        {
            return;
        }

        if (!_documentOperationInProgress && await ConfirmDiscardOrSaveAsync())
        {
            await OpenDocumentPathAsync(entry.FullPath);
        }
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
                    await CreateWorkspaceEntryAsync(targetDirectory, false);
                    break;
                case WorkspacePopupCommand.NewFolder:
                    await CreateWorkspaceEntryAsync(targetDirectory, true);
                    break;
                case WorkspacePopupCommand.CopyPath:
                    CopyWorkspaceEntryPath(entry.FullPath);
                    break;
                case WorkspacePopupCommand.OpenLocation:
                    ShowWorkspaceEntryInExplorer(entry);
                    break;
                case WorkspacePopupCommand.Rename:
                    await RenameWorkspaceEntryAsync(entry);
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

    private async Task OpenWorkspaceEntryAsync(WorkspaceEntry entry)
    {
        if (entry.IsDirectory)
        {
            await LoadWorkspaceDirectoryAsync(
                entry.FullPath,
                _workspaceLoadCancellation?.Token ?? CancellationToken.None);
            _workspaceTree.Expand(entry.FullPath);
            return;
        }

        await ActivateWorkspaceDocumentAsync(entry.FullPath);
    }

    private void CopyWorkspaceEntryPath(string path)
    {
        try
        {
            Clipboard.SetText(path);
            SetStatus(Loc.Get("status.pathCopied"));
        }
        catch (ExternalException exception)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private void ShowWorkspaceEntryInExplorer(WorkspaceEntry entry)
    {
        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
            };
            if (entry.IsDirectory)
            {
                startInfo.ArgumentList.Add(entry.FullPath);
            }
            else
            {
                startInfo.ArgumentList.Add("/select,");
                startInfo.ArgumentList.Add(entry.FullPath);
            }
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private void ClearWorkspaceContextHighlight()
    {
        _workspaceTree.ClearContextMenuHighlight();
        _workspaceDocumentList.ClearContextMenuHighlight();
    }

    private async Task ImportWorkspaceFilesAsync(IReadOnlyList<string> paths)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot)) return;
        var importedCount = 0;
        foreach (var path in paths)
        {
            try
            {
                var fileName = Path.GetFileName(path);
                var destination = Path.Combine(_workspaceRoot, fileName);
                if (PathEquals(destination, path) || File.Exists(destination))
                {
                    var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
                    var extension = Path.GetExtension(fileName);
                    destination = Path.Combine(_workspaceRoot, $"{withoutExtension} (1){extension}");
                }
                File.Copy(path, destination, overwrite: false);
                importedCount++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Could not import file into workspace: {DescribePath(path)}; {exception.Message}");
            }
        }

        if (importedCount == 0) return;
        await RefreshWorkspaceViewsAsync();

        foreach (var path in paths)
        {
            var fileName = Path.GetFileName(path);
            var destination = Path.Combine(_workspaceRoot, fileName);
            if (File.Exists(destination) && await ConfirmDiscardOrSaveAsync())
            {
                await OpenDocumentPathAsync(destination);
            }
            break; // only open the first file
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

    private async Task CreateWorkspaceEntryAsync(string directory, bool isDirectory)
    {
        using var dialog = new TextInputDialog(
            isDirectory ? Loc.Get("workspace.newFolder") : Loc.Get("workspace.newMarkdownFile"),
            Loc.Get("workspace.name"),
            isDirectory ? Loc.Get("workspace.newFolder") : Loc.Get("document.untitledMd"));
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.InputText))
        {
            return;
        }

        try
        {
            var name = !isDirectory && string.IsNullOrEmpty(Path.GetExtension(dialog.InputText))
                ? dialog.InputText + ".md"
                : dialog.InputText;
            var path = GetSafeChildPath(directory, name);
            if (isDirectory)
            {
                if (Directory.Exists(path) || File.Exists(path))
                {
                    throw new IOException(Loc.Get("workspace.nameExists"));
                }
                Directory.CreateDirectory(path);
            }
            else
            {
                await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await stream.FlushAsync();
            }
            await RefreshWorkspaceDirectoryAsync(directory);
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private async Task RenameWorkspaceEntryAsync(WorkspaceEntry entry)
    {
        using var dialog = new TextInputDialog(Loc.Get("workspace.rename"), Loc.Get("workspace.newName"), Path.GetFileName(entry.FullPath));
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.InputText))
        {
            return;
        }

        try
        {
            var parent = Path.GetDirectoryName(entry.FullPath)!;
            var target = GetSafeChildPath(parent, dialog.InputText);
            if (entry.IsDirectory)
            {
                Directory.Move(entry.FullPath, target);
            }
            else
            {
                File.Move(entry.FullPath, target);
            }
            await RefreshWorkspaceDirectoryAsync(parent);
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private async Task DeleteWorkspaceEntryAsync(WorkspaceEntry entry)
    {
        var choice = ShowMessage(
            this,
            Loc.Format("workspace.deleteConfirm", Path.GetFileName(entry.FullPath)),
            "MarkLeaf",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (choice != DialogResult.Yes)
        {
            return;
        }

        var deletesCurrentDocument = IsCurrentDocumentInsideEntry(entry);
        try
        {
            if (deletesCurrentDocument)
            {
                StopWatchingDocument();
            }
            if (entry.IsDirectory)
            {
                FileSystem.DeleteDirectory(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else
            {
                FileSystem.DeleteFile(entry.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            await RefreshWorkspaceDirectoryAsync(Path.GetDirectoryName(entry.FullPath)!);
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
            if (deletesCurrentDocument && _document is not null)
            {
                _document.FilePath = null;
                _document.IsDirty = true;
                _document.LastKnownWriteTime = null;
                _document.LastKnownFingerprint = null;
                _workspaceTree.SelectedPath = null;
                _workspaceDocumentList.SelectedPath = null;
                UpdateDocumentChrome();
                SetStatus(Loc.Get("status.documentDeleted"));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            if (deletesCurrentDocument && _document?.FilePath is not null && File.Exists(_document.FilePath))
            {
                StartWatchingDocument(_document.FilePath);
            }
            ShowWorkspaceOperationError(exception);
        }
    }

    private bool IsCurrentDocumentInsideEntry(WorkspaceEntry entry)
    {
        if (_document?.FilePath is null)
        {
            return false;
        }
        if (!entry.IsDirectory)
        {
            return PathEquals(_document.FilePath, entry.FullPath);
        }

        var directory = Path.GetFullPath(entry.FullPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var documentPath = Path.GetFullPath(_document.FilePath);
        return documentPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSafeChildPath(string directory, string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
        {
            throw new ArgumentException(Loc.Get("workspace.nameInvalid"));
        }
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullDirectory, name));
        if (!path.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(Loc.Get("workspace.nameOutsideRoot"));
        }
        return path;
    }

    private void ShowWorkspaceOperationError(Exception exception)
    {
        ShowMessage(this, Loc.Get("workspace.operationFailed") + "\r\n\r\n" + exception.Message, "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void TryStartWatchingWorkspace(string path)
    {
        StopWatchingWorkspace();
        try
        {
            _workspaceWatcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _workspaceWatcher.Changed += OnWorkspaceWatcherSignal;
            _workspaceWatcher.Created += OnWorkspaceWatcherSignal;
            _workspaceWatcher.Deleted += OnWorkspaceWatcherSignal;
            _workspaceWatcher.Renamed += OnWorkspaceWatcherSignal;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _workspaceWatcher?.Dispose();
            _workspaceWatcher = null;
            _logger.Warning($"Workspace watcher could not start: {exception.GetType().Name}.");
            SetStatus(Loc.Get("status.workspaceWatchFailed"));
        }
    }

    private void OnWorkspaceWatcherSignal(object sender, FileSystemEventArgs args) => _workspaceChangeDebouncer.Signal();

    private void QueueWorkspaceRefresh()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(RefreshExpandedWorkspaceNodes);
        }
        catch (InvalidOperationException)
        {
            // The window may close between the handle check and BeginInvoke.
        }
    }

    private void StopWatchingWorkspace()
    {
        _workspaceWatcher?.Dispose();
        _workspaceWatcher = null;
    }

    private async void RefreshExpandedWorkspaceNodes()
    {
        if (IsDisposed || Disposing || !_workspaceTree.HasRoot)
        {
            return;
        }

        foreach (var directory in _workspaceTree.GetExpandedDirectories())
        {
            await LoadWorkspaceDirectoryAsync(directory, _workspaceLoadCancellation?.Token ?? CancellationToken.None);
        }
        await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
    }

    private async Task RefreshWorkspaceDirectoryAsync(string directory)
    {
        if (_workspaceTree.ContainsPath(directory))
        {
            await LoadWorkspaceDirectoryAsync(directory, _workspaceLoadCancellation?.Token ?? CancellationToken.None);
            _workspaceTree.Expand(directory);
        }
    }
}
