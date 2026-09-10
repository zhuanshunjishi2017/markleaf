using MarkLeaf.Services;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
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

    private void OnWorkspaceWatcherSignal(object sender, FileSystemEventArgs args)
    {
        _workspaceService.InvalidatePreview(args.FullPath);
        if (args is RenamedEventArgs renamed)
        {
            _workspaceService.InvalidatePreview(renamed.OldFullPath);
        }
        _workspaceChangeDebouncer.Signal();
    }

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
}
