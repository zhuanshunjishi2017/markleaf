using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Native.ModernMenus;
using MarkLeaf.Services;
using MarkLeaf.Services.Recovery;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private static string ImageFilter => Loc.Get("fileFilter.images");
    private static string DocumentFilter => Loc.Get("fileFilter.markdown");

    private void OnEditorDirtyChanged(object? sender, EditorMessage message)
    {
        if (_document is null || !message.Payload.TryGetProperty("dirty", out var dirtyElement))
        {
            return;
        }

        _document.IsDirty = dirtyElement.GetBoolean() || _document.HasUnsavedMetadataChanges;
        _document.Revision = message.Revision;
        _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
        UpdateDocumentChrome();

        if (_document.IsDirty
            && _settings.File.AutoSaveEnabled
            && _document.FilePath is not null
            && !_document.IsReadOnly && !_document.IsUserReadOnly
            && !_documentOperationInProgress)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }

    private async Task NewDocumentAsync(NewDocumentKind kind = NewDocumentKind.Markdown)
    {
        if (_documentOperationInProgress || _documentTabOperationInProgress)
        {
            return;
        }

        var document = _documentFileService.CreateNew(
            DefaultNewLine,
            kind,
            DocumentEncodingPolicy.FromId(_settings.File.DefaultEncoding));
        await AddAndActivateDocumentAsync(document);
        SetStatus(Loc.Get("document.newDocument"));
    }

    private async Task OpenDocumentAsync()
    {
        if (_documentOperationInProgress || _documentTabOperationInProgress)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = DocumentFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            Title = Loc.Get("dialog.openDocument"),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        await OpenDocumentPathAsync(dialog.FileName);
        RecordRecentFile(dialog.FileName);
    }

    private async Task OpenDocumentReadOnlyAsync()
    {
        if (_documentOperationInProgress || _documentTabOperationInProgress)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = DocumentFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            Title = Loc.Get("dialog.openReadOnly"),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        await OpenDocumentPathAsync(dialog.FileName, readOnly: true);
        RecordRecentFile(dialog.FileName);
    }

    private async Task OpenDocumentPathAsync(string path, bool readOnly = false, bool forceNewTab = false)
    {
        _documentOperationInProgress = true;
        try
        {
            // De-duplicate before reading disk or resolving image paths. Normal
            // Open must never replace a dirty buffer or unlock a read-only tab.
            var existing = forceNewTab ? null : DocumentMenuPolicy.FindOpenDocument(_openDocuments, path);
            if (existing is not null)
            {
                await SwitchDocumentTabAsync(_openDocuments.IndexOf(existing));
                if (readOnly && !existing.IsReadOnly)
                {
                    if (ShowMessage(this, Loc.Get("document.convertToReadOnly"), "MarkLeaf",
                            MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK
                        && _openDocuments.Contains(existing))
                    {
                        existing.IsReadOnly = true;
                        if (ReferenceEquals(_document, existing)) _editorHost?.SetReadOnly(true);
                        _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
                        UpdateDocumentChrome();
                    }
                }
                return;
            }
            SetStatus(Loc.Get("document.opening"));
            var opened = await _documentFileService.OpenAsync(path);
            if (readOnly)
            {
                opened.IsReadOnly = true;
            }
            var originalMarkdown = opened.Markdown;
            opened.Markdown = _imageAssetService.NormalizeLocalImagePaths(
                opened.Markdown, opened.FilePath,
                _settings.Image.UseRelativePaths,
                _settings.Image.PrefixRelativeWithDotSlash);
            await ResolveMissingImagesAsync(opened);
            opened.IsDirty = !string.Equals(originalMarkdown, opened.Markdown, StringComparison.Ordinal);
            await AddAndActivateDocumentAsync(opened, forceNewTab);
            _logger.Info($"Document opened: {DescribePath(opened.FilePath)}; encoding={opened.Encoding.WebName}; bom={opened.HasBom}; newline={DescribeNewLine(opened.NewLine)}.");
            if (opened.IsDirty)
            {
                SetStatus(Loc.Get("status.imagePathUpdated"));
            }
            else if (_imageAssetService.FindMissingImages(opened.Markdown, opened.FilePath).Count == 0)
            {
                SetStatus(opened.IsReadOnly ? Loc.Get("status.documentOpenedReadOnly") : Loc.Get("status.documentOpened"));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            _logger.Error($"Document open failed: {DescribePath(path)}.", exception);
            ShowMessage(
                this,
                Loc.Get("error.openDocumentFailed") + "\r\n\r\n" + exception.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus(Loc.Get("error.openDocumentFailedTitle"));
        }
        finally
        {
            _documentOperationInProgress = false;
            _menuService.RefreshStates();
        }
    }

    internal async Task OpenDocumentFromExternalRequestAsync(string path)
    {
        await OpenDocumentPathAsync(path);
        RecordRecentFile(path);
    }

    private async Task AddAndActivateDocumentAsync(MarkdownDocument document, bool forceNewTab = false, bool restoreViewState = false)
    {
        int? existing = forceNewTab || document.FilePath is null
            ? null
            : _openDocuments.FindIndex(item => item.FilePath is not null
                && PathEquals(item.FilePath, document.FilePath));
        if (existing is >= 0)
        {
            await SwitchDocumentTabAsync(existing.Value);
            return;
        }

        if (_document is not null && _editorHost?.IsDocumentLoaded == true)
        {
            var snapshot = await _editorHost.RequestSnapshotAsync();
            _document.ApplySnapshot(snapshot);
        }

        StopWatchingDocument();
        _openDocuments.Add(document);
        _activeDocumentIndex = _openDocuments.Count - 1;
        _document = document;
        _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
        _workspaceTree.SelectedPath = document.FilePath;
        _workspaceDocumentList.SelectedPath = document.FilePath;
        LoadDocumentIntoEditor(document, restoreScrollPosition: restoreViewState);
        if (document.FilePath is not null) StartWatchingDocument(document.FilePath);
    }

    private async Task OpenTransferredDocumentAsync(string statePath)
    {
        var state = JsonSerializer.Deserialize<TransferredDocumentState>(
            await File.ReadAllTextAsync(statePath));
        if (state is null) return;
        var document = _documentFileService.CreateNew(
            state.NewLine,
            Enum.TryParse<NewDocumentKind>(state.Kind, out var kind) ? kind : NewDocumentKind.Markdown,
            DocumentEncodingPolicy.FromId(state.EncodingPolicyId));
        document.FilePath = state.FilePath;
        document.Markdown = state.Markdown;
        document.HasBom = state.HasBom;
        document.IsReadOnly = state.IsReadOnly;
        document.IsDirty = state.IsDirty;
        document.Revision = state.Revision;
        await AddAndActivateDocumentAsync(document);
        try { File.Delete(statePath); } catch { }
    }

    private sealed record TransferredDocumentState(
        string? FilePath,
        string Markdown,
        string Kind,
        string EncodingPolicyId,
        bool HasBom,
        bool IsReadOnly,
        bool IsDirty,
        long Revision,
        string NewLine);

    private void ReorderOpenDocuments(IReadOnlyList<MarkdownDocument> documents)
    {
        if (documents.Count != _openDocuments.Count) return;
        var activeDocument = _document;
        _openDocuments.Clear();
        _openDocuments.AddRange(documents);
        _activeDocumentIndex = activeDocument is null ? -1 : _openDocuments.IndexOf(activeDocument);
        _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
        SaveSettings();
    }

    private async void DetachDocumentToNewWindow(int index, Point screenLocation)
    {
        if (index < 0 || index >= _openDocuments.Count) return;
        var document = _openDocuments[index];
        if (ReferenceEquals(document, _document) && _editorHost?.IsDocumentLoaded == true)
        {
            var snapshot = await _editorHost.RequestSnapshotAsync();
            document.ApplySnapshot(snapshot);
        }

        var statePath = Path.Combine(_paths.DataDirectory, "Cache",
            $"detached-{document.Id:N}-{Guid.NewGuid():N}.json");
        var state = new TransferredDocumentState(
            document.FilePath,
            document.Markdown,
            document.Kind.ToString(),
            document.EncodingPolicyId,
            document.HasBom,
            document.IsReadOnly,
            document.IsDirty,
            document.Revision,
            document.NewLine);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state));
            var editorBounds = _editorAreaPanel.Bounds;
            var frameWidth = Math.Max(0, Bounds.Width - ClientSize.Width);
            var frameHeight = Math.Max(0, Bounds.Height - ClientSize.Height);
            var detachedWindowSize = new Size(
                editorBounds.Width + frameWidth,
                editorBounds.Height + frameHeight);
            if (StartNewWindow(
                documentPath: null,
                documentStatePath: statePath,
                location: screenLocation,
                size: detachedWindowSize))
            {
                RemoveTransferredDocument(index);
            }
            else
            {
                try { File.Delete(statePath); } catch { }
            }
        }
        catch (IOException exception)
        {
            _logger.Error("Could not transfer document to a new window.", exception);
            try { File.Delete(statePath); } catch { }
        }
    }

    private void RemoveTransferredDocument(int index)
    {
        if (index < 0 || index >= _openDocuments.Count) return;
        var wasActive = index == _activeDocumentIndex;
        _openDocuments.RemoveAt(index);
        if (_openDocuments.Count == 0)
        {
            StopWatchingDocument();
            _document = null;
            _activeDocumentIndex = -1;
            ClearDocumentOutline();
            _workspaceTree.SelectedPath = null;
            _workspaceDocumentList.SelectedPath = null;
            _documentTabBar.SetDocuments(_openDocuments, -1);
            _pendingEditorRevealDocumentId = null;
            _editorPanel.Visible = false;
            RefreshPersistentStatusBar();
            UpdateDocumentChrome();
            return;
        }

        if (index < _activeDocumentIndex) _activeDocumentIndex--;
        if (wasActive)
        {
            StopWatchingDocument();
            _activeDocumentIndex = Math.Min(index, _openDocuments.Count - 1);
            _document = _openDocuments[_activeDocumentIndex];
            LoadDocumentIntoEditor(_document);
            if (_document.FilePath is not null) StartWatchingDocument(_document.FilePath);
        }
        _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
        SaveSettings();
    }

    private async Task SwitchDocumentTabAsync(int index)
    {
        if (_documentTabOperationInProgress || index < 0 || index >= _openDocuments.Count
            || index == _activeDocumentIndex)
        {
            return;
        }

        _documentTabOperationInProgress = true;
        try
        {
            if (_document is not null && _editorHost?.IsDocumentLoaded == true)
            {
                var snapshot = await _editorHost.RequestSnapshotAsync();
                _document.ApplySnapshot(snapshot);
            }

            StopWatchingDocument();
            _activeDocumentIndex = index;
            _document = _openDocuments[index];
            _documentTabBar.SetDocuments(_openDocuments, index);
            _workspaceTree.SelectedPath = _document.FilePath;
            _workspaceDocumentList.SelectedPath = _document.FilePath;
            LoadDocumentIntoEditor(_document);
            if (_document.FilePath is not null) StartWatchingDocument(_document.FilePath);
        }
        finally
        {
            _documentTabOperationInProgress = false;
        }
    }

    private async Task<bool> CloseDocumentTabAsync(int index)
    {
        if (_documentTabOperationInProgress || _documentTabCloseOperationInProgress
            || _documentOperationInProgress || index < 0 || index >= _openDocuments.Count) return false;

        var originalActive = _document;
        _documentTabCloseOperationInProgress = true;
        try
        {
            var target = _openDocuments[index];
            if (index == _activeDocumentIndex && _editorHost?.IsDocumentLoaded == true)
            {
                var snapshot = await _editorHost.RequestSnapshotAsync();
                if (!ReferenceEquals(_document, target) || !_openDocuments.Contains(target)) return false;
                target.ApplySnapshot(snapshot);
            }
            if (target.IsDirty)
            {
                var choice = ShowMessage(
                    this,
                    Loc.Format("document.confirmDiscard", target.DisplayName),
                    "MarkLeaf",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1);
                if (choice == DialogResult.Cancel) return false;
                if (choice == DialogResult.Yes)
                {
                    if (index != _activeDocumentIndex)
                    {
                        await SwitchDocumentTabAsync(index);
                        if (!ReferenceEquals(_document, target)) return false;
                    }
                    if (!await WaitForActiveDocumentLoadedAsync(target)
                        || !await SaveDocumentAsync(saveAs: target.IsReadOnly || target.IsUserReadOnly)) return false;
                }
            }

            RecordClosedTab(target);
            index = _openDocuments.IndexOf(target);
            if (index < 0) return true;
            _openDocuments.RemoveAt(index);
            if (_openDocuments.Count == 0)
            {
                StopWatchingDocument();
                _document = null;
                _activeDocumentIndex = -1;
                ClearDocumentOutline();
                _workspaceTree.SelectedPath = null;
                _workspaceDocumentList.SelectedPath = null;
                _documentTabBar.SetDocuments(_openDocuments, -1);
                _pendingEditorRevealDocumentId = null;
                _editorPanel.Visible = false;
                RefreshPersistentStatusBar();
                UpdateDocumentChrome();
                return true;
            }

            if (index < _activeDocumentIndex)
            {
                _activeDocumentIndex--;
                _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
                return true;
            }

            if (index == _activeDocumentIndex)
            {
                var nextIndex = Math.Min(index, _openDocuments.Count - 1);
                _activeDocumentIndex = -1;
                await SwitchDocumentTabAsync(nextIndex);
                return true;
            }

            // A tab to the right of the active tab does not change the active
            // document, so it does not enter either branch above. Still push
            // the new collection to the tab bar immediately after removal.
            _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
            return true;
        }
        finally
        {
            _documentTabCloseOperationInProgress = false;
            if (originalActive is not null && _openDocuments.Contains(originalActive)
                && !ReferenceEquals(_document, originalActive))
                await SwitchDocumentTabAsync(_openDocuments.IndexOf(originalActive));
        }
    }

    /// <summary>
    /// 关闭标签历史（与 macOS ClosedTabHistoryPolicy 同一策略）：仅记录单个
    /// 标签关闭，按文件路径/未命名序号去重，上限 20 条，内存保存关闭时的
    /// 完整内容与状态——脏标签恢复后仍是脏的，不落盘。
    /// </summary>
    private void RecordClosedTab(MarkdownDocument document)
    {
        _closedTabHistory.RemoveAll(existing =>
            document.FilePath is not null
                ? PathEquals(existing.FilePath, document.FilePath)
                : existing.FilePath is null && existing.Id == document.Id);
        _closedTabHistory.Add(document);
        while (_closedTabHistory.Count > 20)
        {
            _closedTabHistory.RemoveAt(0);
        }
    }

    private bool HasClosedTabs => _closedTabHistory.Count > 0;

    private async Task ReopenLastClosedTabAsync()
    {
        if (_closedTabHistory.Count == 0)
        {
            SetStatus(Loc.Get("status.noClosedTabs"));
            return;
        }

        var record = _closedTabHistory[^1];
        _closedTabHistory.RemoveAt(_closedTabHistory.Count - 1);

        // 同路径标签仍开着时只切换过去，内容以现存标签为准（与 macOS 去重语义一致）。
        if (record.FilePath is not null)
        {
            var existingIndex = _openDocuments.FindIndex(item =>
                item.FilePath is not null && PathEquals(item.FilePath, record.FilePath));
            if (existingIndex >= 0)
            {
                await SwitchDocumentTabAsync(existingIndex);
                SetStatus(Loc.Get("status.reopenedTab"));
                return;
            }
        }

        // 恢复的文档取新 Id：旧 Id 对应的恢复快照在关闭时已删除，且编辑器
        // 会话以 Id 关联，复用会与历史状态串扰。
        var restored = _documentFileService.CreateNew(
            record.NewLine,
            record.Kind,
            DocumentEncodingPolicy.FromId(record.EncodingPolicyId));
        restored.FilePath = record.FilePath;
        restored.Markdown = record.Markdown;
        restored.HasBom = record.HasBom;
        restored.IsDirty = record.IsDirty;
        restored.IsReadOnly = record.IsReadOnly;
        restored.IsUserReadOnly = record.IsUserReadOnly;
        restored.HasUnsavedMetadataChanges = record.HasUnsavedMetadataChanges;
        restored.LastKnownFingerprint = record.LastKnownFingerprint;
        restored.LastKnownWriteTime = record.LastKnownWriteTime;
        restored.SavedMarkdown = record.SavedMarkdown;
        restored.SourceMode = record.SourceMode;
        restored.VisualSelectionFrom = record.VisualSelectionFrom;
        restored.VisualSelectionTo = record.VisualSelectionTo;
        restored.SourceSelectionFrom = record.SourceSelectionFrom;
        restored.SourceSelectionTo = record.SourceSelectionTo;
        restored.ScrollTop = record.ScrollTop;
        restored.ReadingAnchor = record.ReadingAnchor;
        restored.Revision = Math.Max(0, record.Revision);
        await AddAndActivateDocumentAsync(restored, forceNewTab: true, restoreViewState: true);
        SetStatus(Loc.Get("status.reopenedTab"));
    }

    private async void ShowDocumentTabContextMenu(int index, Point screenLocation)
    {
        if (index < 0 || index >= _openDocuments.Count) return;
        var document = _openDocuments[index];
        var model = BuildDocumentTabMenu(document, index);
        var command = await _menuService.ShowHostPopupMenuAsync(this, screenLocation, DeviceDpi, _menuDarkMode, model);
        await ExecuteDocumentTabMenuCommandAsync(command, document);
    }

    private EditorMenuModel BuildDocumentTabMenu(MarkdownDocument document, int index)
    {
        var canLocate = CanLocateDocumentInWorkspace(document);
        var canCloseOthers = _openDocuments.Count > 1;
        var canCloseRight = index < _openDocuments.Count - 1;
        var hasPath = !string.IsNullOrWhiteSpace(document.FilePath);
        return HostPopupMenuModel.Tabs(command => command switch
        {
            AppCommand.SaveDocument => new CommandState(DocumentMenuPolicy.CanSave(true, true, document.IsDirty,
                document.FilePath is not null, document.IsReadOnly || document.IsUserReadOnly)),
            AppCommand.SaveDocumentAs => new CommandState(true),
            AppCommand.CloseOtherDocumentTabs => new CommandState(canCloseOthers),
            AppCommand.CloseRightDocumentTabs => new CommandState(canCloseRight),
            AppCommand.ReopenClosedTab => new CommandState(HasClosedTabs),
            AppCommand.LocateCurrentDocumentInWorkspace => new CommandState(canLocate),
            AppCommand.WorkspaceCopyPath or AppCommand.WorkspaceShowInFolder => new CommandState(hasPath),
            _ => new CommandState(true),
        }, command => _shortcutManager.GetShortcutText(command) ?? "");
    }

    private async Task ExecuteDocumentTabMenuCommandAsync(AppCommand? command, MarkdownDocument document)
    {
        var index = _openDocuments.IndexOf(document);
        if (index < 0) return;
        switch (command)
        {
            case AppCommand.SaveDocument:
                await SaveDocumentTargetAsync(document, saveAs: false);
                break;
            case AppCommand.SaveDocumentAs:
                await SaveDocumentTargetAsync(document, saveAs: true);
                break;
            case AppCommand.CloseCurrentDocumentTab:
                await CloseDocumentTabAsync(index);
                break;
            case AppCommand.CloseOtherDocumentTabs:
                await CloseOtherDocumentTabsAsync(index);
                break;
            case AppCommand.CloseRightDocumentTabs:
                await CloseRightDocumentTabsAsync(index);
                break;
            case AppCommand.ReopenClosedTab:
                await ReopenLastClosedTabAsync();
                break;
            case AppCommand.LocateCurrentDocumentInWorkspace:
                await LocateDocumentInWorkspaceAsync(index);
                break;
            case AppCommand.WorkspaceCopyPath:
                if (!string.IsNullOrWhiteSpace(document.FilePath))
                    Clipboard.SetText(document.FilePath);
                break;
            case AppCommand.WorkspaceShowInFolder:
                if (!string.IsNullOrWhiteSpace(document.FilePath))
                    ShowDocumentInExplorer(document.FilePath);
                break;
        }
    }

    private async Task CloseRightDocumentTabsAsync(int index)
    {
        if (index < 0 || index >= _openDocuments.Count - 1) return;
        await CloseDocumentTabsAsync(_openDocuments.Skip(index + 1).ToArray());
    }

    private async Task<bool> SaveDocumentTargetAsync(MarkdownDocument document, bool saveAs)
    {
        var originalActive = _document;
        var index = _openDocuments.IndexOf(document);
        if (index < 0) return false;
        try
        {
            if (!ReferenceEquals(_document, document)) await SwitchDocumentTabAsync(index);
            if (!ReferenceEquals(_document, document) || !await WaitForActiveDocumentLoadedAsync(document)) return false;
            return await SaveDocumentAsync(saveAs);
        }
        finally
        {
            if (originalActive is not null && _openDocuments.Contains(originalActive)
                && !ReferenceEquals(_document, originalActive))
                await SwitchDocumentTabAsync(_openDocuments.IndexOf(originalActive));
        }
    }

    private void ShowDocumentInExplorer(string filePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(filePath);
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowMessage(this, exception.Message, "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CloseOtherDocumentTabsAsync(int keepIndex)
    {
        if (keepIndex < 0 || keepIndex >= _openDocuments.Count) return;
        await CloseDocumentTabsAsync(_openDocuments.Where((_, index) => index != keepIndex).ToArray());
    }

    private async Task CloseDocumentTabsAsync(IEnumerable<MarkdownDocument> documents)
    {
        if (_documentBatchInProgress) return;
        _documentBatchInProgress = true;
        try { await DocumentBatchPolicy.RunAsync(documents, async document =>
        {
            var index = _openDocuments.IndexOf(document);
            return index < 0 || await CloseDocumentTabAsync(index);
        }); }
        finally { _documentBatchInProgress = false; }
    }

    private bool IsExternalDocument(MarkdownDocument document)
    {
        if (_workspaceRoot is null || document.FilePath is null) return false;
        var relative = Path.GetRelativePath(Path.GetFullPath(_workspaceRoot), Path.GetFullPath(document.FilePath));
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative);
    }

    private bool CanLocateDocumentInWorkspace(MarkdownDocument document)
    {
        return _workspaceRoot is not null
            && document.FilePath is { Length: > 0 } filePath
            && Path.IsPathFullyQualified(filePath)
            && File.Exists(filePath)
            && !IsExternalDocument(document);
    }

    private async Task LocateDocumentInWorkspaceAsync(int index)
    {
        if (index < 0 || index >= _openDocuments.Count) return;
        var document = _openDocuments[index];
        if (!CanLocateDocumentInWorkspace(document)) return;

        if (index != _activeDocumentIndex)
        {
            await SwitchDocumentTabAsync(index);
        }

        await RevealPathInTreeAsync(document.FilePath!);
    }

    private void RecordRecentFile(string path)
    {
        if (!_settings.File.RecordRecentFiles || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var fullPath = TryGetFullPath(path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return;
        }

        _settings.Workspace.RecentFiles = _settings.Workspace.RecentFiles
            .Where(item => !string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(fullPath)
            .Take(10)
            .ToList();
    }

    private async Task OpenRecentFileAsync(string path)
    {
        if (_documentOperationInProgress)
        {
            return;
        }

        await OpenDocumentPathAsync(path);
        RecordRecentFile(path);
    }

    private void ClearHistory()
    {
        // 清除历史是不可恢复的删除操作：先确认再执行（与 macOS 1.7.7 行为一致）。
        var choice = ShowMessage(
            this,
            Loc.Get("dialog.clearHistoryConfirm") + "\r\n\r\n" + Loc.Get("dialog.clearHistoryConfirmDetail"),
            "MarkLeaf",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.Yes)
        {
            return;
        }

        _settings.Workspace.RecentFiles.Clear();
        _settings.Workspace.RecentFolders.Clear();
        _menuService.RefreshStates();
        SetStatus(Loc.Get("status.historyCleared"));
    }

    private async Task ResolveMissingImagesAsync(MarkdownDocument document)
    {
        var missingImages = _imageAssetService.FindMissingImages(document.Markdown, document.FilePath);
        if (missingImages.Count == 0)
        {
            return;
        }

        var names = string.Join(
            "\r\n",
            missingImages.Take(50).Select(image => "- " + image.FileName));
        if (missingImages.Count > 50)
        {
            names += Loc.Format("document.imageCountLess", missingImages.Count - 50);
        }

        var choice = ShowMessage(
            this,
            Loc.Format("document.imageCountMissing", missingImages.Count) + "\r\n\r\n" + names + "\r\n\r\n" +
            Loc.Get("document.imageMissingPrompt"),
            Loc.Get("document.imageMissingTitle"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Error,
            MessageBoxDefaultButton.Button1);
        if (choice != DialogResult.Yes)
        {
            SetStatus(Loc.Format("status.imageCountMissingLoaded", missingImages.Count));
            return;
        }

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var missingImage in missingImages)
        {
            using var dialog = new OpenFileDialog
            {
                Filter = ImageFilter,
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true,
                Title = Loc.Format("document.imageRelinkStatus", missingImage.FileName),
                FileName = missingImage.FileName,
            };
            var missingDirectory = Path.GetDirectoryName(missingImage.ResolvedPath);
            if (missingDirectory is not null && Directory.Exists(missingDirectory))
            {
                dialog.InitialDirectory = missingDirectory;
            }

            if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
            {
                continue;
            }

            try
            {
                await ImageAssetService.ValidateImageFileAsync(dialog.FileName);
                replacements[missingImage.Reference] = ImageAssetService.ToMarkdownPath(dialog.FileName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ShowMessage(
                    this,
                    Loc.Format("document.imageReplaceFailed", missingImage.FileName) + "\r\n\r\n" + exception.Message,
                    Loc.Get("document.imageReplaceFailedTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        document.Markdown = ImageAssetService.ReplaceImagePaths(document.Markdown, replacements);
        var remaining = _imageAssetService.FindMissingImages(document.Markdown, document.FilePath);
        SetStatus(remaining.Count == 0
            ? Loc.Format("document.imageReplacedAndSave", replacements.Count)
            : Loc.Format("document.imageReplacedStatus", replacements.Count, remaining.Count));
    }

    private async Task<bool> SaveDocumentAsync(bool saveAs, bool forceOverwrite = false)
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true || _documentOperationInProgress)
        {
            return false;
        }

        var document = _document;
        var editorHost = _editorHost;
        var readOnly = document.IsReadOnly || document.IsUserReadOnly;
        if (readOnly && !saveAs)
        {
            SetStatus(Loc.Get("status.readOnlySaveBlocked"));
            return false;
        }

        var targetPath = _document.FilePath;
        if (saveAs || targetPath is null || _document.IsReadOnly)
        {
            using var dialog = new SaveFileDialog
            {
                Filter = DocumentFilter,
                AddExtension = true,
                DefaultExt = _document.Kind.FileExtension(),
                RestoreDirectory = true,
                OverwritePrompt = true,
                Title = Loc.Get("dialog.saveDocument"),
                FileName = targetPath is null
                    ? Loc.Get(_document.Kind == NewDocumentKind.PlainText ? "document.untitledTxt" : "document.untitledMd")
                    : Path.GetFileName(targetPath),
            };
            if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
            {
                return false;
            }

            targetPath = dialog.FileName;
            saveAs = !PathEquals(_document.FilePath, targetPath);
        }

        if (!ReferenceEquals(_document, document) || !_openDocuments.Contains(document)) return false;
        if (readOnly && DocumentPathIdentity.Equals(document.FilePath, targetPath))
        {
            ShowMessage(this, Loc.Get("document.readOnlySourceOverwrite"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!_document.IsDirty && !saveAs && !forceOverwrite)
        {
            SetStatus(Loc.Get("status.documentNoChanges"));
            return true;
        }

        _documentOperationInProgress = true;
        EditorSnapshot? snapshot = null;
        var previousDocumentType = _document.Kind.EditorDocumentType();
        try
        {
            SetStatus(Loc.Get("document.saving"));
            snapshot = await editorHost.RequestSnapshotAsync();
            if (!ReferenceEquals(_document, document) || !_openDocuments.Contains(document)) return false;
            var markdown = _imageAssetService.NormalizeLocalImagePaths(
                snapshot.Markdown,
                document.FilePath ?? targetPath,
                _settings.Image.UseRelativePaths,
                _settings.Image.PrefixRelativeWithDotSlash);
            SetStatus(Loc.Get("document.safeSaving"));
            await _documentFileService.SaveAsync(
                document,
                markdown,
                snapshot.Revision,
                targetPath,
                forceOverwrite);
            document.SavedMarkdown = snapshot.Markdown;

            // A tab may have been closed/switched while the disk write awaited.
            // The file service updated only the captured document; never apply
            // that result (or markSaved) to a different editor document.
            if (!ReferenceEquals(_document, document) || !_openDocuments.Contains(document)) return true;
            if (readOnly) editorHost.SetReadOnly(false);

            if (!string.Equals(previousDocumentType, _document.Kind.EditorDocumentType(), StringComparison.Ordinal))
            {
                _editorHost?.SetDocumentType(_document.Kind.EditorDocumentType());
            }
            _editorHost?.SetDocumentPath(_document.FilePath);

            _document.Revision = Math.Max(snapshot.Revision, _editorSession.ConfirmedRevision);
            _document.IsDirty = _document.Revision > snapshot.Revision || _document.HasUnsavedMetadataChanges;
            _document.ReadingAnchor = snapshot.ReadingAnchor;
            // 无并发编辑时才回传保存基线；revision 落后说明编辑器已有新改动，
            // 此时让编辑器保持脏状态（与 macOS 宿主约定一致）。
            if (!_document.IsDirty)
            {
                _editorHost?.MarkSaved(snapshot.Markdown);
            }
            StopWatchingDocument();
            StartWatchingDocument(_document.FilePath!);
            _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
            UpdateDocumentChrome();
            _logger.Info($"Document saved safely: {DescribePath(_document.FilePath)}; revision={snapshot.Revision}.");
            _recoveryService.Delete(_document.Id);
            SetStatus(Loc.Get("status.documentSaved"));
            return true;
        }
        catch (ExternalDocumentChangedException)
        {
            if (!ReferenceEquals(_document, document) || !_openDocuments.Contains(document)) return false;
            _logger.Warning($"Save blocked by external modification: {DescribePath(targetPath)}.");
            return await ResolveExternalSaveConflictAsync(
                targetPath,
                snapshot ?? new EditorSnapshot(_document.Markdown, _document.Revision));
        }
        catch (OperationCanceledException exception)
        {
            _logger.Error("Latest editor snapshot request timed out.", exception);
            ShowMessage(
                this,
                Loc.Get("document.saveSnapshotTimeout"),
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus(Loc.Get("document.saveSnapshotFailed"));
            return false;
        }
        catch (DocumentSaveException exception)
        {
            _logger.Error($"Document save failed: {DescribePath(targetPath)}.", exception);
            var recovery = exception.RecoveryFilePath is null
                ? string.Empty
                : "\r\n\r\n" + Loc.Get("document.recoveryTempFile") + "\r\n" + exception.RecoveryFilePath;
            var saveFailedMessage = Loc.Format("document.safeSaveFailed", recovery);
            if (exception.InnerException?.Message is { } innerMsg)
                saveFailedMessage += "\r\n\r\n" + innerMsg;
            ShowMessage(
                this,
                saveFailedMessage,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus(Loc.Get("document.saveFailedKeepEdits"));
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Document save rejected; edits retained.", exception);
            ShowMessage(this, exception.Message, "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            _documentOperationInProgress = false;
            _menuService.RefreshStates();
        }
    }

    private async Task<bool> ResolveExternalSaveConflictAsync(string targetPath, EditorSnapshot snapshot)
    {
        while (true)
        {
            using var dialog = new ExternalChangeDialog(Path.GetFileName(targetPath));
            ShowModal(() => dialog.ShowDialog(this));
            switch (dialog.Choice)
            {
                case ExternalChangeChoice.Reload:
                    _documentOperationInProgress = false;
                    await OpenDocumentPathAsync(targetPath);
                    return false;
                case ExternalChangeChoice.Compare:
                    try
                    {
                        var diskDocument = await _documentFileService.OpenAsync(targetPath);
                        using var comparison = new DocumentComparisonDialog(snapshot.Markdown, diskDocument.Markdown);
                        ShowModal(() => comparison.ShowDialog(this));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        ShowMessage(
                            this,
                            Loc.Format("document.cannotCompare", exception.Message),
                            "MarkLeaf",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    break;
                case ExternalChangeChoice.SaveAs:
                    _documentOperationInProgress = false;
                    return await SaveDocumentAsync(saveAs: true);
                case ExternalChangeChoice.ForceOverwrite:
                    _documentOperationInProgress = false;
                    return await SaveDocumentAsync(saveAs: false, forceOverwrite: true);
                default:
                    SetStatus(Loc.Get("document.cancelSaveExternal"));
                    return false;
            }
        }
    }

    private async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (_document?.IsDirty != true)
        {
            return true;
        }

        var choice = ShowMessage(
            this,
            Loc.Format("document.confirmDiscard", _document.DisplayName),
            "MarkLeaf",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1);
        return choice switch
        {
            DialogResult.Yes => await SaveDocumentAsync(saveAs: _document.IsReadOnly || _document.IsUserReadOnly),
            DialogResult.No => true,
            _ => false,
        };
    }

    private async void OnMainFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_options.IsExportCommand)
        {
            _closeApproved = true;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            StopWatchingDocument();
            _recoveryTimer.Stop();
            return;
        }

        if (_editorFullScreen)
        {
            ToggleEditorFullScreen();
        }

        if (_closeApproved)
        {
            CleanOldLogs();
            SaveWindowState();
            return;
        }

        eventArgs.Cancel = true;
        if (_documentOperationInProgress)
        {
            return;
        }

        await SyncActiveDocumentAsync();
        var dirtyDocuments = _openDocuments.Where(document => document.IsDirty).ToList();
        if (dirtyDocuments.Count > 1)
        {
            var choice = ShowMessage(
                this,
                Loc.Format("document.confirmDiscardMultiple", dirtyDocuments.Count),
                "MarkLeaf",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);
            if (choice == DialogResult.Cancel)
            {
                return;
            }

            if (choice == DialogResult.Yes)
            {
                var activeIndex = _activeDocumentIndex;
                if (!await SaveDocumentsAsync(dirtyDocuments))
                {
                    if (activeIndex >= 0 && activeIndex < _openDocuments.Count)
                        await SwitchDocumentTabAsync(activeIndex);
                    return;
                }

                if (activeIndex >= 0 && activeIndex < _openDocuments.Count)
                    await SwitchDocumentTabAsync(activeIndex);
            }
        }
        else if (dirtyDocuments.Count == 1)
        {
            var dirtyIndex = _openDocuments.IndexOf(dirtyDocuments[0]);
            if (dirtyIndex >= 0 && dirtyIndex != _activeDocumentIndex)
            {
                await SwitchDocumentTabAsync(dirtyIndex);
            }

            if (!await ConfirmDiscardOrSaveAsync())
            {
                return;
            }
        }

        _closeApproved = true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        StopWatchingDocument();
        _recoveryTimer.Stop();
        // 用户选择"不保存"（文档仍为脏）时保留该文档的恢复快照，便于之后通过
        // "文件→恢复未保存的文件"找回；已保存或未修改的文档则清理本进程的恢复文件。
        if (_document?.IsDirty != true)
        {
            _recoveryService.DeleteOwnFiles();
        }
        Close();
    }

    private async Task<bool> SaveDocumentsAsync(IEnumerable<MarkdownDocument> documents)
    {
        if (_documentBatchInProgress) return false;
        _documentBatchInProgress = true;
        var originalActive = _document;
        try
        {
            return await DocumentBatchPolicy.RunAsync(documents, async document =>
            {
                var index = _openDocuments.IndexOf(document);
                if (index < 0) return true;
                if (!ReferenceEquals(_document, document)) await SwitchDocumentTabAsync(index);
                return ReferenceEquals(_document, document)
                    && await WaitForActiveDocumentLoadedAsync(document)
                    && await SaveDocumentAsync(saveAs: document.IsReadOnly || document.IsUserReadOnly);
            });
        }
        finally
        {
            _documentBatchInProgress = false;
            if (originalActive is not null && _openDocuments.Contains(originalActive)
                && !ReferenceEquals(_document, originalActive))
                await SwitchDocumentTabAsync(_openDocuments.IndexOf(originalActive));
        }
    }

    private async Task SaveAllDocumentsAsync()
    {
        if (_documentOperationInProgress || _documentTabOperationInProgress) return;
        await SaveDocumentsAsync(DocumentMenuPolicy.SaveAllTargets(_openDocuments));
    }

    private async Task<bool> WaitForActiveDocumentLoadedAsync(MarkdownDocument document)
    {
        if (_editorHost?.IsDocumentLoaded == true) return ReferenceEquals(_document, document);
        if (_editorHost is null) return false;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDocumentLoaded(object? sender, EditorMessage message) => completion.TrySetResult();
        _editorHost.DocumentLoaded += OnDocumentLoaded;
        try
        {
            if (_editorHost.IsDocumentLoaded) return ReferenceEquals(_document, document);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return _editorHost.IsDocumentLoaded && ReferenceEquals(_document, document);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            _editorHost.DocumentLoaded -= OnDocumentLoaded;
        }
    }

    private void LoadDocumentIntoEditor(MarkdownDocument document, bool restoreScrollPosition = true)
    {
        _menuService.CancelEditorContextMenu();
        _editorCommandStatus = EditorCommandStatus.Empty;
        _editorStatus = EditorStatus.Empty;
        if (!_editorPanel.Visible)
        {
            // Do not expose the previous document still held by WebView2 while
            // the newly selected document is being loaded.
            _pendingEditorRevealDocumentId = document.Id;
        }
        // New files start at the top; reactivated tabs restore their own offset.
        var restoreScrollTop = restoreScrollPosition ? document.ScrollTop : 0;
        _editorHost?.LoadDocument(
            document.Id,
            document.Revision,
            document.Markdown,
            document.IsReadOnly || document.IsUserReadOnly,
            document.FilePath is null ? document.Kind.EditorDocumentType() : GetDocumentType(document.FilePath),
            document.FilePath,
            document.VisualSelectionFrom,
            document.VisualSelectionTo,
            document.SourceSelectionFrom,
            document.SourceSelectionTo,
            restoreScrollTop,
            restoreScrollPosition,
            restoreScrollPosition ? document.ReadingAnchor : null,
            initialDirty: document.IsDirty, savedMarkdown: document.SavedMarkdown, sourceMode: document.SourceMode);
        if (_pendingEditorRevealDocumentId is null)
        {
            _editorPanel.Visible = true;
        }
        if (_activeDocumentIndex >= 0)
        {
            _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
        }
        RefreshPersistentStatusBar();
        UpdateDocumentChrome();
        ApplyBlockHandleVisibility();
        _recoveryTimer.Start();
    }

    private async Task SyncActiveDocumentAsync()
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true) return;
        var document = _document;
        var host = _editorHost;
        var snapshot = await host.RequestSnapshotAsync();
        if (!ReferenceEquals(document, _document) || !ReferenceEquals(host, _editorHost)) return;
        document.ApplySnapshot(snapshot);
    }

    private async void ShowEncodingMenu()
    {
        if (_document is null || _document.IsReadOnly || _document.IsUserReadOnly) return;
        var current = DocumentEncodingPolicy.FromId(_document.EncodingPolicyId);
        var model = StatusBarMenuModel.Encoding(current.Id, enabled: true);
        var command = await _menuService.ShowHostPopupMenuAsync(
            this, StatusBarPopupPoint(_encodingLabel), DeviceDpi, _menuDarkMode, model);
        var index = command is null ? -1 : (int)command.Value - (int)AppCommand.StatusEncodingUtf8;
        if (index >= 0 && index < DocumentEncodingPolicy.All.Count)
            _ = ChangeDocumentEncodingAsync(DocumentEncodingPolicy.All[index]);
    }

    private async void ShowNewLineMenu()
    {
        if (_document is null || _document.IsReadOnly || _document.IsUserReadOnly) return;
        var model = StatusBarMenuModel.NewLine(_document.NewLine, enabled: true);
        var command = await _menuService.ShowHostPopupMenuAsync(
            this, StatusBarPopupPoint(_newLineLabel), DeviceDpi, _menuDarkMode, model);
        var newLine = command switch
        {
            AppCommand.StatusNewLineCrlf => "\r\n",
            AppCommand.StatusNewLineLf => "\n",
            _ => null,
        };
        if (newLine is null || !DocumentEncodingChangePolicy.SetNewLine(_document, newLine)) return;
        RefreshPersistentStatusBar();
        UpdateDocumentChrome();
        SetStatus(Loc.Get("status.documentModified"));
    }

    private Point StatusBarPopupPoint(ToolStripItem item)
    {
        var owner = item.GetCurrentParent();
        return owner is null ? Point.Empty : owner.PointToScreen(new Point(item.Bounds.Left, item.Bounds.Bottom));
    }

    private async Task ChangeDocumentEncodingAsync(DocumentEncodingPolicy target)
    {
        if (_document is not { } document || _documentOperationInProgress)
        {
            return;
        }

        var action = DocumentEncodingChangePolicy.Action(document, target);
        if (action is EncodingChangeAction.None or EncodingChangeAction.RejectReadOnly)
        {
            return;
        }

        if (action == EncodingChangeAction.UpdateNextSave)
        {
            DocumentEncodingChangePolicy.SetNextSaveEncoding(document, target);
            RefreshPersistentStatusBar();
            UpdateDocumentChrome();
            SetStatus(Loc.Get("status.documentModified"));
            return;
        }

        using var dialog = new EncodingChangeDialog(
            DocumentEncodingPolicy.FromId(document.EncodingPolicyId).DisplayName,
            target.DisplayName,
            _document.IsDirty);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK || !ReferenceEquals(document, _document))
        {
            return;
        }

        switch (dialog.Choice)
        {
            case EncodingChangeChoice.DirectRead:
                await DirectReadDocumentEncodingAsync(target);
                break;
            case EncodingChangeChoice.ConvertEncoding:
                await ConvertDocumentEncodingAsync(target);
                break;
        }
    }

    private async Task DirectReadDocumentEncodingAsync(DocumentEncodingPolicy target)
    {
        if (_document is not { FilePath: { } path } document || document.IsReadOnly
            || document.IsUserReadOnly || _documentOperationInProgress)
        {
            return;
        }

            _documentOperationInProgress = true;
            try
            {
                var reopened = await _documentFileService.OpenAsync(path, target);
                if (!ReferenceEquals(document, _document) || document.IsReadOnly || document.IsUserReadOnly) return;
                var index = _openDocuments.IndexOf(document);
                if (index < 0) return;
                StopWatchingDocument();
                _openDocuments[index] = reopened;
                _document = reopened;
                _documentTabBar.SetDocuments(_openDocuments, _activeDocumentIndex);
                LoadDocumentIntoEditor(reopened);
                StartWatchingDocument(reopened.FilePath!);
                SetStatus(Loc.Get("status.documentReloaded"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or System.Text.DecoderFallbackException)
            {
                ShowMessage(this, exception.Message, "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _documentOperationInProgress = false;
            }
    }

    private async Task ConvertDocumentEncodingAsync(DocumentEncodingPolicy target)
    {
        if (_document is not { FilePath: not null } document || document.IsReadOnly
            || document.IsUserReadOnly || _documentOperationInProgress || _editorHost is not { } host)
        {
            return;
        }

        _documentOperationInProgress = true;
        try
        {
            var snapshot = await host.RequestSnapshotAsync();
            if (!ReferenceEquals(document, _document) || !ReferenceEquals(host, _editorHost)
                || document.IsReadOnly || document.IsUserReadOnly) return;
            StopWatchingDocument();
            await _documentFileService.ConvertEncodingAsync(document, snapshot.Markdown, snapshot.Revision, target);
            if (!ReferenceEquals(document, _document)) return;
            if (!document.IsDirty) host.MarkSaved(snapshot.Markdown);
            RefreshPersistentStatusBar();
            UpdateDocumentChrome();
            SetStatus(Loc.Get(document.IsDirty ? "status.documentModified" : "status.documentSaved"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or System.Text.EncoderFallbackException)
        {
            ShowMessage(this, exception.Message, "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _documentOperationInProgress = false;
            if (ReferenceEquals(document, _document) && document.FilePath is { } currentPath)
                StartWatchingDocument(currentPath);
        }
    }

    private static string GetDocumentType(string? filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".txt", StringComparison.OrdinalIgnoreCase)
            ? "plainText"
            : "markdown";
    }

    private bool IsPlainTextDocument => _document?.Kind == NewDocumentKind.PlainText;

    private void UpdateDocumentChrome()
    {
        UpdateEditorAreaBackground();
        var name = _document?.DisplayName ?? "MarkLeaf";
        if (_document is { } chromeDocument && (chromeDocument.IsReadOnly || chromeDocument.IsUserReadOnly))
        {
            name += Loc.Get("document.readOnlySuffix");
        }
        var documentName = _document?.IsDirty == true ? $"● {name}" : name;
        Text = _document is null
            ? "MarkLeaf"
            : _settings.Appearance.TitleBarTextStyle switch
            {
                TitleBarTextStyle.AppName => "MarkLeaf",
                TitleBarTextStyle.FileName => documentName,
                _ => $"{documentName} - MarkLeaf",
            };

        _menuService.RefreshStates();
    }

    private void StartWatchingDocument(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return;
        }

        _documentWatcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _documentWatcher.Changed += OnDocumentWatcherSignal;
        _documentWatcher.Created += OnDocumentWatcherSignal;
        _documentWatcher.Deleted += OnDocumentWatcherSignal;
        _documentWatcher.Renamed += OnDocumentWatcherSignal;
        _externalChangeTimer.Stop();
        _externalChangeTimer.Tick -= OnExternalChangeTimerTick;
        _externalChangeTimer.Tick += OnExternalChangeTimerTick;
    }

    private void OnDocumentWatcherSignal(object sender, FileSystemEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (_documentWatcher is null)
            {
                return;
            }

            _externalChangeTimer.Stop();
            _externalChangeTimer.Start();
        });
    }

    private async void OnExternalChangeTimerTick(object? sender, EventArgs eventArgs)
    {
        _externalChangeTimer.Stop();
        var document = _document;
        if (document?.FilePath is null || _documentOperationInProgress)
        {
            return;
        }

        try
        {
            if (!await _documentFileService.HasExternalChangeAsync(document))
            {
                return;
            }

            SetStatus(Loc.Get("document.externalChangeDetected"));
            if (document.IsDirty)
            {
                ShowMessage(
                    this,
                    Loc.Get("document.externalChangeDirtyMessage"),
                    Loc.Get("document.externalChangeTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var reload = ShowMessage(
                this,
                Loc.Get("document.externalChangeReloadClean"),
                Loc.Get("document.externalChangeTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (reload == DialogResult.Yes)
            {
                await OpenDocumentPathAsync(document.FilePath);
            }
        }
        catch (IOException exception)
        {
            _logger.Error($"External change recheck failed: {DescribePath(document.FilePath)}.", exception);
        }
    }

    private void StopWatchingDocument()
    {
        _externalChangeTimer.Stop();
        _documentWatcher?.Dispose();
        _documentWatcher = null;
    }

    private static bool PathEquals(string? first, string second)
    {
        return first is not null
            && string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribePath(string? path)
    {
        return path is null ? "<untitled>" : Path.GetFileName(path);
    }

    private static string DescribeNewLine(string newLine)
    {
        return newLine switch
        {
            "\r\n" => "CRLF",
            "\n" => "LF",
            "\r" => "CR",
            _ => "unknown",
        };
    }

    private async Task BeginDocumentSmokeIfRequestedAsync()
    {
        if (_documentSmokeStarted
            || string.IsNullOrWhiteSpace(_options.DocumentSmokeInputPath)
            || string.IsNullOrWhiteSpace(_options.DocumentSmokeOutputPath)
            || string.IsNullOrWhiteSpace(_options.DocumentSmokeReportPath)
            || _editorHost?.IsReady != true)
        {
            return;
        }

        _documentSmokeStarted = true;
        var opened = await _documentFileService.OpenAsync(_options.DocumentSmokeInputPath);
        await AddAndActivateDocumentAsync(opened);
    }

    private async Task ContinueDocumentSmokeAfterLoadAsync()
    {
        if (!_documentSmokeStarted
            || _document is null
            || string.IsNullOrWhiteSpace(_options.DocumentSmokeOutputPath)
            || string.IsNullOrWhiteSpace(_options.DocumentSmokeReportPath))
        {
            return;
        }

        _editorHost?.ExecuteCommand("appendText", "\n阶段 4 安全保存检查。\n");
        var snapshot = await _editorHost!.RequestSnapshotAsync();
        await _documentFileService.SaveAsync(
            _document,
            snapshot.Markdown,
            snapshot.Revision,
            _options.DocumentSmokeOutputPath);
        var reopened = await _documentFileService.OpenAsync(_options.DocumentSmokeOutputPath);
        var report = new
        {
            OpenedEncoding = _document.Encoding.WebName,
            OpenedBom = _document.HasBom,
            OpenedNewLine = DescribeNewLine(_document.NewLine),
            SnapshotRevision = snapshot.Revision,
            SavedAtomically = File.Exists(_options.DocumentSmokeOutputPath),
            ReopenedContainsInitialText = reopened.Markdown.Contains("初始文档", StringComparison.Ordinal),
            ReopenedContainsEditorText = reopened.Markdown.Contains("阶段 4 安全保存检查", StringComparison.Ordinal),
            ReopenedBom = reopened.HasBom,
            ReopenedNewLine = DescribeNewLine(reopened.NewLine),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DocumentSmokeReportPath)!);
        await File.WriteAllTextAsync(
            _options.DocumentSmokeReportPath,
            System.Text.Json.JsonSerializer.Serialize(
                report,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        BeginInvoke(Close);
    }

    private async void OnEditorFilesDropped(object? sender, DroppedFiles droppedFiles)
    {
        var documentPaths = droppedFiles.Paths
            .Where(path => WorkspaceTreeView.IsDroppableFile(path))
            .ToArray();
        var imagePaths = droppedFiles.Paths.Except(documentPaths).ToArray();

        foreach (var path in documentPaths)
        {
            await OpenDocumentPathAsync(path);
            RecordRecentFile(path);
            break;
        }

        if (imagePaths.Length > 0)
        {
            await ImportImageFilesAsync(imagePaths, droppedFiles.ClientX, droppedFiles.ClientY);
        }
    }

    private void OnEmptyEditorAreaDragEnter(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.Effect = eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnEmptyEditorAreaDragDrop(object? sender, DragEventArgs eventArgs)
    {
        if (eventArgs.Data?.GetData(DataFormats.FileDrop) is not string[] paths)
            return;

        var documentPaths = paths
            .Where(WorkspaceTreeView.IsDroppableFile)
            .ToArray();
        foreach (var path in documentPaths)
        {
            await OpenDocumentPathAsync(path);
            RecordRecentFile(path);
        }
    }

    private async void OnEditorPasteImageRequested(object? sender, EventArgs eventArgs)
    {
        await PasteClipboardContentAsync();
    }

    private async Task ImportClipboardBitmapAsync()
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        try
        {
            if (!Clipboard.ContainsImage())
            {
                SetStatus(Loc.Get("status.noClipboardImage"));
                return;
            }

            using var clipboardImage = Clipboard.GetImage();
            if (clipboardImage is null)
            {
                return;
            }

            using var bitmap = new Bitmap(clipboardImage);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var imported = await _imageAssetService.ImportBytesAsync(
                stream.ToArray(),
                ".png",
                ResolveClipboardImageTargetDirectory());
            await InsertImportedImageAsync(imported, Loc.Get("dialog.pasteImage"));
        }
        catch (Exception exception) when (exception is IOException or ExternalException or InvalidDataException
            or OperationCanceledException)
        {
            _logger.Error("Clipboard image import failed.", exception);
            ShowMessage(
                this,
                Loc.Get("dialog.pasteImageFailed") + "\r\n\r\n" + exception.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task SelectAndInsertImagesAsync()
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = ImageFilter,
            CheckFileExists = true,
            Multiselect = true,
            RestoreDirectory = true,
            Title = Loc.Get("dialog.insertImage"),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) == DialogResult.OK)
        {
            await ImportImageFilesAsync(dialog.FileNames);
        }
    }

    private async Task ImportImageFilesAsync(
        IEnumerable<string> paths,
        double? clientX = null,
        double? clientY = null)
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        var importedCount = 0;
        foreach (var path in paths.Take(32))
        {
            try
            {
                var imported = await ImportFileByHandlingAsync(path);
                if (await InsertImportedImageAsync(
                        imported,
                        Path.GetFileNameWithoutExtension(path),
                        clientX,
                        clientY))
                {
                    importedCount++;
                    clientX = null;
                    clientY = null;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                or OperationCanceledException)
            {
                _logger.Warning($"Dropped image rejected: {DescribePath(path)}; {exception.Message}");
            }
        }

        SetStatus(importedCount > 0 ? Loc.Format("status.imagesInserted", importedCount) : Loc.Get("status.noImagesFound"));
    }

    private string GetDefaultImageDirectory()
    {
        var directory = _settings.Image.DefaultDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = _paths.DefaultImageDirectory;
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private string? GetDocumentAssetsDirectory()
    {
        if (_document?.FilePath is not { } documentPath)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(documentPath);
        if (directory is null)
        {
            return null;
        }

        var assets = Path.Combine(directory, Path.GetFileNameWithoutExtension(documentPath) + ".assets");
        Directory.CreateDirectory(assets);
        return assets;
    }

    private string ResolveClipboardImageTargetDirectory()
    {
        switch (_settings.Image.ClipboardHandling)
        {
            case ClipboardImageHandling.CopyToAssets:
                if (GetDocumentAssetsDirectory() is { } assets)
                {
                    return assets;
                }

                SetStatus(Loc.Get("document.imageNotSaved"));
                return GetDefaultImageDirectory();
            default:
                return GetDefaultImageDirectory();
        }
    }

    private async Task<ImportedImage> ImportFileByHandlingAsync(string sourcePath)
    {
        switch (_settings.Image.FileHandling)
        {
            case FileImageHandling.CopyToAssets:
                if (GetDocumentAssetsDirectory() is { } assets)
                {
                    return await _imageAssetService.CopyFileIntoAsync(sourcePath, assets);
                }

                SetStatus(Loc.Get("document.imageNotSavedRef"));
                break;
        }

        return await _imageAssetService.ImportFileAsync(sourcePath);
    }

    private async Task<bool> InsertImportedImageAsync(
        ImportedImage imported,
        string alt,
        double? clientX = null,
        double? clientY = null)
    {
        if (_document is null || _editorHost is null)
        {
            return false;
        }

        var markdownPath = _settings.Image.UseRelativePaths
            ? ImageAssetService.ToRelativeMarkdownPath(
                imported.PhysicalPath, _document.FilePath, _settings.Image.PrefixRelativeWithDotSlash)
                ?? imported.MarkdownPath
            : imported.MarkdownPath;

        var inserted = await _editorHost.ExecuteCommandAsync(
            "insertImage",
            markdownPath + "\n" + alt,
            clientX,
            clientY);
        if (!inserted)
        {
            _logger.Warning($"Editor rejected imported image: {DescribePath(imported.PhysicalPath)}.");
            SetStatus(Loc.Get("status.imageInsertFailed"));
            return false;
        }

        SetStatus(Loc.Get("status.imageInserted"));
        return true;
    }

    private async Task ChangeImageAsync()
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = ImageFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            Title = Loc.Get("contextMenu.image.change"),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var imported = await ImportFileByHandlingAsync(dialog.FileName);
            var markdownPath = _settings.Image.UseRelativePaths
                ? ImageAssetService.ToRelativeMarkdownPath(
                    imported.PhysicalPath, _document.FilePath, _settings.Image.PrefixRelativeWithDotSlash)
                    ?? imported.MarkdownPath
                : imported.MarkdownPath;
            _editorHost.ExecuteCommand("changeImage", markdownPath);
            SetStatus(Loc.Get("status.imageChanged"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or OperationCanceledException)
        {
            _logger.Warning($"Change image rejected: {exception.Message}");
        }
    }

    private async Task SaveImageAsAsync()
    {
        if (_document is not { } document || _editorHost?.IsDocumentLoaded != true
            || !_editorCommandStatus.ImageSelected || _editorCommandStatus.ImageSource is not { } source)
        {
            return;
        }

        try
        {
            var image = await new ImageOriginalExportService().ReadAsync(source, document.FilePath, CancellationToken.None);
            if (IsDisposed || !ReferenceEquals(_document, document) || _editorCommandStatus.ImageSource != source) return;

            using var dialog = new SaveFileDialog
            {
                Filter = $"{image.FileExtension.ToUpperInvariant()}|*.{image.FileExtension}",
                DefaultExt = image.FileExtension,
                RestoreDirectory = true,
                Title = Loc.Get("contextMenu.image.saveAs"),
                FileName = $"image.{image.FileExtension}",
            };
            if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
            {
                return;
            }

            if (!ReferenceEquals(_document, document)) return;
            await ImageOriginalExportService.WriteAsync(image, dialog.FileName);
            SetStatus(Loc.Get("status.imageSavedAs"));
        }
        catch (OperationCanceledException) when (CancellationToken.None.IsCancellationRequested) { }
        catch (OperationCanceledException exception)
        {
            _logger.Error("Saving the original image timed out.", exception);
            ShowMessage(this, Loc.Get("status.imageDownloadTimedOut"), Loc.Get("contextMenu.image.saveAs"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException)
        {
            _logger.Error("Save image as failed.", exception);
            ShowMessage(this, exception.Message, Loc.Get("contextMenu.image.saveAs"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveMermaidAsAsync()
    {
        if (_document is not { } document || _editorHost is not { IsDocumentLoaded: true } host
            || !_editorCommandStatus.MermaidSelected || !_editorCommandStatus.MermaidRendered) return;
        var revision = document.Revision;
        using var dialog = new SaveFileDialog { Filter = "PNG (2×)|*.png|SVG|*.svg", DefaultExt = "png", FileName = "diagram.png", RestoreDirectory = true };
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK || !ReferenceEquals(_document, document) || document.Revision != revision) return;
        try
        {
            var format = dialog.FilterIndex == 2 ? "svg" : "png";
            if (!string.Equals(Path.GetExtension(dialog.FileName), "." + format, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The filename extension must match the selected image format.");
            var image = await host.RequestMermaidImageExportAsync(format);
            if (IsDisposed || !ReferenceEquals(_document, document) || document.Revision != revision) return;
            await ImageOriginalExportService.WriteAsync(new(image.Data, image.FileExtension, image.MediaType), dialog.FileName);
            SetStatus(Loc.Get("status.imageSavedAs"));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException or InvalidDataException)
        { ShowMessage(this, exception.Message, "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private string ResolveImagePath(string markdownSrc)
    {
        if (Path.IsPathRooted(markdownSrc))
        {
            return markdownSrc;
        }

        var baseDir = _document?.FilePath is { } docPath
            ? Path.GetDirectoryName(docPath)!
            : Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDir, markdownSrc.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string? ExtractImageSrc(string markdown)
    {
        var open = markdown.IndexOf("](", StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        var srcStart = open + 2;
        var close = markdown.IndexOf(')', srcStart);
        if (close < 0)
        {
            close = markdown.Length;
        }

        var src = markdown[srcStart..close].Trim();
        var quote = src.IndexOf('"');
        if (quote > 0)
        {
            src = src[..quote].Trim();
        }

        return src.Length > 0 ? src : null;
    }
}
