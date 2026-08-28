using System.Diagnostics;
using MarkLeaf.Documents;
using MarkLeaf.Services;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
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
        UpdateSidebarSearchEnabled();
        _openFolderPrompt.Visible = false;
        var rootName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = fullPath;
        }
        _sidebarSearchBar.WorkspaceName = rootName;
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
        _sidebarSearchBar.WorkspaceName = string.Empty;
        _sidebarSearchBar.ClearSearch();
        UpdateSidebarSearchEnabled();
        ClearWorkspacePlaceholder();
        ShowNoWorkspacePlaceholder();
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
        UpdateSidebarSearchEnabled();
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
        _menuService.RefreshStates();
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

    private async Task CreateUntitledWorkspaceDocumentAsync(
        string directory,
        NewDocumentKind kind = NewDocumentKind.Markdown)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot)
            || _documentOperationInProgress
            || !Directory.Exists(directory))
        {
            return;
        }

        if (!await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        try
        {
            var path = _workspaceService.GetAvailableUntitledDocumentPath(directory, kind);
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.FlushAsync();
            }

            await RefreshWorkspaceDirectoryAsync(directory);
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
            await OpenDocumentPathAsync(path);
            await RevealPathInTreeAsync(path);
            _workspaceDocumentList.SelectedPath = path;
            BeginWorkspaceEntryRename(new WorkspaceEntry(Path.GetFileName(path), path, false));
            SetStatus(Loc.Format("status.documentCreated", Path.GetFileName(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private async Task CreateUntitledWorkspaceFolderAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(_workspaceRoot) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var path = _workspaceService.GetAvailableUntitledDirectoryPath(directory);
            Directory.CreateDirectory(path);
            await RefreshWorkspaceDirectoryAsync(directory);
            if (_workspaceListViewActive)
            {
                ToggleWorkspaceView();
            }
            BeginWorkspaceEntryRename(new WorkspaceEntry(Path.GetFileName(path), path, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowWorkspaceOperationError(exception);
        }
    }

    private string GetNewWorkspaceDocumentDirectory()
    {
        var root = Path.GetFullPath(_workspaceRoot!);
        if (_document?.FilePath is not { } documentPath)
        {
            return root;
        }

        var fullDocumentPath = Path.GetFullPath(documentPath);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullDocumentPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return Path.GetDirectoryName(fullDocumentPath) ?? root;
    }
}
