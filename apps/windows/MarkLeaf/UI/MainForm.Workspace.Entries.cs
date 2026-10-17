using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using MarkLeaf.Documents;
using MarkLeaf.App;
using MarkLeaf.Services;
using MarkLeaf.UI.Controls;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private async Task RevealPathInTreeAsync(string filePath)
    {
        if (_workspaceRoot is null)
        {
            return;
        }

        if (_workspaceListViewActive)
        {
            ToggleWorkspaceView();
        }

        var fullPath = Path.GetFullPath(filePath);
        var rootFull = Path.GetFullPath(_workspaceRoot);
        var relative = Path.GetRelativePath(rootFull, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return;
        }

        if (_sidebarAnimationTargetCollapsed ?? _sidebarSplit.Panel1Collapsed)
        {
            ExpandSidebar();
        }
        ShowSidebarView(outline: false);

        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null)
        {
            return;
        }

        var ancestors = new List<string>();
        var current = directory;
        while (!PathEquals(current, rootFull))
        {
            ancestors.Insert(0, current);
            var parent = Path.GetDirectoryName(current);
            if (parent is null || PathEquals(parent, current))
            {
                break;
            }
            current = parent;
        }

        foreach (var ancestor in ancestors)
        {
            await LoadWorkspaceDirectoryAsync(
                ancestor,
                _workspaceLoadCancellation?.Token ?? CancellationToken.None);
            _workspaceTree.Expand(ancestor);
        }

        _workspaceTree.SelectedPath = fullPath;
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

        await OpenDocumentPathAsync(path);
        _workspaceDocumentList.SelectedPath = _document?.FilePath;
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

        if (!_documentOperationInProgress)
        {
            await OpenDocumentPathAsync(entry.FullPath);
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

    private async Task CopyWorkspaceEntryContentsAsync(WorkspaceEntry entry)
    {
        try
        {
            if (_document?.FilePath is { } path && DocumentPathIdentity.Equals(path, entry.FullPath))
                await SyncActiveDocumentAsync();
            var text = await WorkspaceMutationPolicy.TextForCopyAsync(_openDocuments, entry.FullPath,
                async file => (await _documentFileService.OpenAsync(file)).Markdown);
            if (text.Length == 0) Clipboard.Clear(); else Clipboard.SetText(text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException or System.Text.DecoderFallbackException)
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
            if (File.Exists(destination))
            {
                await OpenDocumentPathAsync(destination);
            }
            break; // only open the first file
        }
    }

    private async Task MoveWorkspaceEntryAsync(string sourcePath, string targetDirectory)
    {
        if (_workspaceRoot is not null && PathEquals(_workspaceRoot, sourcePath)) return;
        try
        {
            var sourceParent = Path.GetDirectoryName(sourcePath)!;
            if (string.Equals(sourceParent, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var name = Path.GetFileName(sourcePath);
            var target = Path.Combine(targetDirectory, name);

            if (File.Exists(target) || Directory.Exists(target))
            {
                ShowMessage(this, Loc.Format("workspace.moveConflict", name), "MarkLeaf",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var isDirectory = Directory.Exists(sourcePath);
            var activeAffected = _document?.FilePath is { } activePath
                && WorkspaceMutationPolicy.IsAffected(activePath, sourcePath, isDirectory);
            if (activeAffected) StopWatchingDocument();
            if (isDirectory)
            {
                Directory.Move(sourcePath, target);
            }
            else
            {
                File.Move(sourcePath, target);
            }

            WorkspaceMutationPolicy.Rename(_openDocuments, sourcePath, target, isDirectory);
            _ = WorkspaceMutationChannel.BroadcastAsync(new(sourcePath, target, isDirectory));
            if (activeAffected && _document?.FilePath is { } movedPath)
            {
                _editorHost?.SetDocumentPath(movedPath);
                StartWatchingDocument(movedPath);
                UpdateDocumentChrome();
            }
            _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);

            await RefreshWorkspaceDirectoryAsync(sourceParent);
            if (!string.Equals(sourceParent, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                await RefreshWorkspaceDirectoryAsync(targetDirectory);
            }
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (_document?.FilePath is { } currentPath && File.Exists(currentPath)) StartWatchingDocument(currentPath);
            ShowWorkspaceOperationError(exception);
        }
    }

    private void BeginWorkspaceEntryRename(WorkspaceEntry entry)
    {
        ClearWorkspaceContextHighlight();
        if (_workspaceListViewActive && !entry.IsDirectory)
        {
            _workspaceDocumentList.SelectedPath = entry.FullPath;
            _workspaceDocumentList.BeginInlineRename(entry);
        }
        else
        {
            _workspaceTree.BeginInlineRename(entry);
        }
    }

    private async void OnWorkspaceRenameRequested(object? sender, WorkspaceRenameRequestedEventArgs eventArgs)
    {
        if (!await RenameWorkspaceEntryAsync(eventArgs.Entry, eventArgs.NewName))
        {
            BeginWorkspaceEntryRename(eventArgs.Entry);
        }
    }

    private async Task<bool> RenameWorkspaceEntryAsync(WorkspaceEntry entry, string newName)
    {
        if (_workspaceRoot is not null && PathEquals(_workspaceRoot, entry.FullPath)) return false;
        if (string.IsNullOrWhiteSpace(newName))
        {
            ShowWorkspaceOperationError(new ArgumentException(Loc.Get("workspace.nameInvalid")));
            return false;
        }

        var stoppedOpenDocumentWatcher = false;
        try
        {
            var parent = Path.GetDirectoryName(entry.FullPath)!;
            var target = GetSafeChildPath(parent, newName.Trim());
            if (PathEquals(entry.FullPath, target))
            {
                return true;
            }
            var isActiveDocument = IsCurrentDocumentInsideEntry(entry);

            if (isActiveDocument)
            {
                StopWatchingDocument();
                stoppedOpenDocumentWatcher = true;
            }

            if (entry.IsDirectory)
            {
                Directory.Move(entry.FullPath, target);
            }
            else
            {
                File.Move(entry.FullPath, target);
            }

            WorkspaceMutationPolicy.Rename(_openDocuments, entry.FullPath, target, entry.IsDirectory);
            _ = WorkspaceMutationChannel.BroadcastAsync(new(entry.FullPath, target, entry.IsDirectory));
            if (isActiveDocument && _document?.FilePath is { } renamedActivePath)
            {
                UpdateDocumentChrome();
                _editorHost?.SetDocumentPath(renamedActivePath);
                StartWatchingDocument(renamedActivePath);
                stoppedOpenDocumentWatcher = false;
            }
            _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);

            await RefreshWorkspaceDirectoryAsync(parent);
            if (isActiveDocument)
            {
                _workspaceTree.SelectedPath = target;
            }
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (stoppedOpenDocumentWatcher && _document?.FilePath is { } originalPath && File.Exists(originalPath))
            {
                StartWatchingDocument(originalPath);
            }
            ShowWorkspaceOperationError(exception);
            return false;
        }
    }

    private async Task DeleteWorkspaceEntryAsync(WorkspaceEntry entry)
    {
        if (_documentOperationInProgress || _documentTabOperationInProgress
            || _workspaceRoot is not null && PathEquals(_workspaceRoot, entry.FullPath)) return;
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
            await SyncActiveDocumentAsync();
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
            DetachDeletedWorkspaceDocuments(entry.FullPath, entry.IsDirectory);
            _ = WorkspaceMutationChannel.BroadcastAsync(new(entry.FullPath, null, entry.IsDirectory));
            await RefreshWorkspaceDirectoryAsync(Path.GetDirectoryName(entry.FullPath)!);
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
            if (deletesCurrentDocument && _document is not null)
            {
                _workspaceTree.SelectedPath = null;
                _workspaceDocumentList.SelectedPath = null;
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
        return _document?.FilePath is { } path && WorkspaceMutationPolicy.IsAffected(path, entry.FullPath, entry.IsDirectory);
    }

    private void DetachDeletedWorkspaceDocuments(string path, bool isDirectory)
    {
        var activeAffected = _document?.FilePath is { } activePath
            && WorkspaceMutationPolicy.IsAffected(activePath, path, isDirectory);
        WorkspaceMutationPolicy.DetachDeleted(_openDocuments, path, isDirectory, recycled: true);
        if (activeAffected)
        {
            StopWatchingDocument();
            _editorHost?.SetDocumentPath(null);
            _editorHost?.SetReadOnly(false);
        }
        _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
        UpdateDocumentChrome();
    }

    private void ApplyPeerWorkspaceMutation(WorkspaceMutationNotice notice)
    {
        if (IsDisposed || Disposing) return;
        if (notice.Destination is null)
        {
            DetachDeletedWorkspaceDocuments(notice.Source, notice.IsDirectory);
        }
        else
        {
            var activeAffected = _document?.FilePath is { } path
                && WorkspaceMutationPolicy.IsAffected(path, notice.Source, notice.IsDirectory);
            if (activeAffected) StopWatchingDocument();
            WorkspaceMutationPolicy.Rename(_openDocuments, notice.Source, notice.Destination, notice.IsDirectory);
            if (activeAffected && _document?.FilePath is { } renamedPath)
            {
                _editorHost?.SetDocumentPath(renamedPath);
                StartWatchingDocument(renamedPath);
            }
            _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
            UpdateDocumentChrome();
        }
        _ = RefreshWorkspaceViewsAsync();
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
}
