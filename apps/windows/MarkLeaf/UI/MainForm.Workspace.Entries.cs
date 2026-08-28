using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using MarkLeaf.Documents;
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

        var fullPath = Path.GetFullPath(filePath);
        var rootFull = Path.GetFullPath(_workspaceRoot);
        var relative = Path.GetRelativePath(rootFull, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return;
        }

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

        if (!await ConfirmDiscardOrSaveAsync())
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

        if (!_documentOperationInProgress && await ConfirmDiscardOrSaveAsync())
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

    private async Task MoveWorkspaceEntryAsync(string sourcePath, string targetDirectory)
    {
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

            var isOpenDocument = _document?.FilePath is not null
                && string.Equals(_document.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase);

            if (isOpenDocument && _document!.IsDirty)
            {
                if (!await ConfirmDiscardOrSaveAsync(isDocumentSwitch: false))
                {
                    return;
                }
            }

            var isDirectory = Directory.Exists(sourcePath);
            if (isDirectory)
            {
                Directory.Move(sourcePath, target);
            }
            else
            {
                File.Move(sourcePath, target);
            }

            if (isOpenDocument)
            {
                await OpenDocumentPathAsync(target);
            }

            await RefreshWorkspaceDirectoryAsync(sourceParent);
            if (!string.Equals(sourceParent, targetDirectory, StringComparison.OrdinalIgnoreCase))
            {
                await RefreshWorkspaceDirectoryAsync(targetDirectory);
            }
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
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
            var isOpenDocument = !entry.IsDirectory
                && _document?.FilePath is not null
                && string.Equals(_document.FilePath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

            if (isOpenDocument)
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

            if (isOpenDocument)
            {
                _document!.FilePath = target;
                _document.LastKnownWriteTime = File.GetLastWriteTimeUtc(target);
                UpdateDocumentChrome();
                StartWatchingDocument(target);
                stoppedOpenDocumentWatcher = false;
            }

            await RefreshWorkspaceDirectoryAsync(parent);
            if (isOpenDocument)
            {
                _workspaceTree.SelectedPath = target;
            }
            await RefreshWorkspaceDocumentListAsync(_workspaceLoadCancellation?.Token ?? CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (stoppedOpenDocumentWatcher && File.Exists(entry.FullPath))
            {
                StartWatchingDocument(entry.FullPath);
            }
            ShowWorkspaceOperationError(exception);
            return false;
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
                var deletedDocumentKind = _document.Kind;
                _document = _documentFileService.CreateNew(
                    DefaultNewLine,
                    deletedDocumentKind,
                    DocumentEncodingPolicy.FromId(_settings.File.DefaultEncoding));
                _workspaceTree.SelectedPath = null;
                _workspaceDocumentList.SelectedPath = null;
                _recoveryService.DeleteOwnFiles();
                LoadDocumentIntoEditor(_document);
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
}
