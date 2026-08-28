using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Native;
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

        _document.IsDirty = dirtyElement.GetBoolean();
        _document.Revision = message.Revision;
        UpdateDocumentChrome();

        if (_document.IsDirty
            && _settings.File.AutoSaveEnabled
            && _document.FilePath is not null
            && !_documentOperationInProgress)
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }

    private async Task NewDocumentAsync(NewDocumentKind kind = NewDocumentKind.Markdown)
    {
        if (_documentOperationInProgress || !await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        StopWatchingDocument();
        _document = _documentFileService.CreateNew(
            DefaultNewLine,
            kind,
            DocumentEncodingPolicy.FromId(_settings.File.DefaultEncoding));
        _workspaceTree.SelectedPath = null;
        _workspaceDocumentList.SelectedPath = null;
        LoadDocumentIntoEditor(_document);
        SetStatus(Loc.Get("document.newDocument"));
    }

    private async Task OpenDocumentAsync()
    {
        if (_documentOperationInProgress || !await ConfirmDiscardOrSaveAsync())
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
        if (_documentOperationInProgress || !await ConfirmDiscardOrSaveAsync())
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

    private async Task OpenDocumentPathAsync(string path, bool readOnly = false)
    {
        _documentOperationInProgress = true;
        try
        {
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
            StopWatchingDocument();
            _document = opened;
            _workspaceTree.SelectedPath = opened.FilePath;
            _workspaceDocumentList.SelectedPath = opened.FilePath;
            LoadDocumentIntoEditor(opened);
            StartWatchingDocument(opened.FilePath!);
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
            .Take(8)
            .ToList();
    }

    private async Task OpenRecentFileAsync(string path)
    {
        if (_documentOperationInProgress || !await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        await OpenDocumentPathAsync(path);
        RecordRecentFile(path);
    }

    private void ClearHistory()
    {
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
            snapshot = await _editorHost.RequestSnapshotAsync();
            var markdown = _imageAssetService.NormalizeLocalImagePaths(
                snapshot.Markdown,
                _document.FilePath ?? targetPath,
                _settings.Image.UseRelativePaths,
                _settings.Image.PrefixRelativeWithDotSlash);
            SetStatus(Loc.Get("document.safeSaving"));
            await _documentFileService.SaveAsync(
                _document,
                markdown,
                snapshot.Revision,
                targetPath,
                forceOverwrite);

            if (!string.Equals(previousDocumentType, _document.Kind.EditorDocumentType(), StringComparison.Ordinal))
            {
                _editorHost?.SetDocumentType(_document.Kind.EditorDocumentType());
            }

            _document.Revision = Math.Max(snapshot.Revision, _editorSession.ConfirmedRevision);
            _document.IsDirty = _document.Revision > snapshot.Revision;
            StopWatchingDocument();
            StartWatchingDocument(_document.FilePath!);
            UpdateDocumentChrome();
            _logger.Info($"Document saved safely: {DescribePath(_document.FilePath)}; revision={snapshot.Revision}.");
            _recoveryService.Delete(_document.Id);
            SetStatus(Loc.Get("status.documentSaved"));
            return true;
        }
        catch (ExternalDocumentChangedException)
        {
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

    private async Task<bool> ConfirmDiscardOrSaveAsync(bool isDocumentSwitch = true)
    {
        if (_document?.IsDirty != true)
        {
            return true;
        }

        if (isDocumentSwitch
            && _settings.File.SaveOnDocumentSwitch
            && _document.FilePath is not null)
        {
            return await SaveDocumentAsync(saveAs: false);
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
            DialogResult.Yes => await SaveDocumentAsync(saveAs: false),
            DialogResult.No => true,
            _ => false,
        };
    }

    private async void OnMainFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
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

        if (_document?.IsDirty == true && !await ConfirmDiscardOrSaveAsync(isDocumentSwitch: false))
        {
            return;
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
        BeginInvoke(Close);
    }

    private void LoadDocumentIntoEditor(MarkdownDocument document)
    {
        _editorCommandStatus = EditorCommandStatus.Empty;
        _editorStatus = EditorStatus.Empty;
        _editorHost?.LoadDocument(
            document.Id,
            document.Revision,
            document.Markdown,
            document.IsReadOnly,
            document.FilePath is null ? document.Kind.EditorDocumentType() : GetDocumentType(document.FilePath));
        RefreshPersistentStatusBar();
        UpdateDocumentChrome();
        ApplyBlockHandleVisibility();
        _recoveryTimer.Start();
    }

    private void ShowEncodingMenu()
    {
        if (_document is null || _document.IsReadOnly)
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        var current = DocumentEncodingPolicy.FromId(_document.EncodingPolicyId);
        try
        {
            for (var index = 0; index < DocumentEncodingPolicy.All.Count; index++)
            {
                var encoding = DocumentEncodingPolicy.All[index];
                var isCurrent = string.Equals(current.Id, encoding.Id, StringComparison.Ordinal);
                var flags = NativeMethods.MfString
                    | (isCurrent ? NativeMethods.MfChecked | NativeMethods.MfGrayed : NativeMethods.MfUnchecked);
                NativeMethods.AppendMenu(menu, flags, (nuint)(index + 1), encoding.DisplayName);
            }

            var owner = _encodingLabel.GetCurrentParent();
            if (owner is null)
            {
                return;
            }

            var bounds = _encodingLabel.Bounds;
            var screenPoint = owner.PointToScreen(new Point(bounds.Left, bounds.Bottom));
            NativeMethods.SetForegroundWindow(Handle);
            var selected = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmLeftButton | NativeMethods.TpmReturnCommand,
                screenPoint.X,
                screenPoint.Y,
                Handle,
                0);
            NativeMethods.PostMessage(Handle, NativeMethods.WmNull, 0, 0);
            if (selected is > 0 && selected <= (uint)DocumentEncodingPolicy.All.Count)
            {
                _ = ChangeDocumentEncodingAsync(DocumentEncodingPolicy.All[(int)selected - 1]);
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void ShowNewLineMenu()
    {
        if (_document is null || _document.IsReadOnly)
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendNewLineMenuItem(menu, 1, "CRLF", _document.NewLine == "\r\n");
            AppendNewLineMenuItem(menu, 2, "LF", _document.NewLine == "\n");
            var selected = ShowStatusBarPopupMenu(menu, _newLineLabel);
            var newLine = selected switch
            {
                1 => "\r\n",
                2 => "\n",
                _ => null,
            };
            if (newLine is null || _document.NewLine == newLine)
            {
                return;
            }

            _document.NewLine = newLine;
            _document.IsDirty = true;
            RefreshPersistentStatusBar();
            UpdateDocumentChrome();
            SetStatus(Loc.Get("status.documentModified"));
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static void AppendNewLineMenuItem(nint menu, uint command, string text, bool isCurrent)
    {
        var flags = NativeMethods.MfString
            | (isCurrent ? NativeMethods.MfChecked | NativeMethods.MfGrayed : NativeMethods.MfUnchecked);
        NativeMethods.AppendMenu(menu, flags, command, text);
    }

    private async Task ChangeDocumentEncodingAsync(DocumentEncodingPolicy target)
    {
        if (_document is null || _document.IsReadOnly || _documentOperationInProgress)
        {
            return;
        }

        var current = DocumentEncodingPolicy.FromId(_document.EncodingPolicyId);
        if (string.Equals(current.Id, target.Id, StringComparison.Ordinal))
        {
            return;
        }

        using var dialog = new EncodingChangeDialog(
            current.DisplayName,
            target.DisplayName,
            _document.IsDirty);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
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
        if (_document is null || _documentOperationInProgress)
        {
            return;
        }

        if (_document.FilePath is not null)
        {
            _documentOperationInProgress = true;
            try
            {
                StopWatchingDocument();
                var reopened = await _documentFileService.OpenAsync(_document.FilePath, target);
                _document = reopened;
                LoadDocumentIntoEditor(reopened);
                StartWatchingDocument(reopened.FilePath!);
                SetStatus(Loc.Get("status.documentReloaded"));
            }
            finally
            {
                _documentOperationInProgress = false;
            }
        }
        else
        {
            _document.Encoding = target.CreateEncoding();
            _document.EncodingPolicyId = target.Id;
            _document.HasBom = target.HasBom;
            _document.IsDirty = true;
            RefreshPersistentStatusBar();
            UpdateDocumentChrome();
            SetStatus(Loc.Get("status.documentModified"));
            return;
        }

    }

    private async Task ConvertDocumentEncodingAsync(DocumentEncodingPolicy target)
    {
        if (_document is null || _documentOperationInProgress)
        {
            return;
        }

        if (_document.FilePath is null)
        {
            _document.Encoding = target.CreateEncoding();
            _document.EncodingPolicyId = target.Id;
            _document.HasBom = target.HasBom;
            _document.IsDirty = true;
            RefreshPersistentStatusBar();
            UpdateDocumentChrome();
            SetStatus(Loc.Get("status.documentModified"));
            return;
        }

        var oldPath = _document.FilePath;
        var currentMarkdown = _document.Markdown;
        _document.Encoding = target.CreateEncoding();
        _document.EncodingPolicyId = target.Id;
        _document.HasBom = target.HasBom;
        _document.IsDirty = true;
        RefreshPersistentStatusBar();
        UpdateDocumentChrome();
        if (await SaveDocumentAsync(saveAs: false, forceOverwrite: true))
        {
            SetStatus(Loc.Get("status.documentSaved"));
            return;
        }

        _document.FilePath = oldPath;
        _document.Markdown = currentMarkdown;
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
        var name = _document?.DisplayName ?? "MarkLeaf";
        if (_document?.IsReadOnly == true)
        {
            name += Loc.Get("document.readOnlySuffix");
        }
        if (_document is null)
        {
            Text = "MarkLeaf";
        }
        else if (_settings.File.AutoSaveEnabled && _document.FilePath is not null)
        {
            Text = Loc.Format("document.autoSaveTitle", name);
        }
        else
        {
            Text = $"{(_document.IsDirty ? "*" : string.Empty)}{name} - MarkLeaf";
        }

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
        _document = opened;
        LoadDocumentIntoEditor(opened);
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
            if (await ConfirmDiscardOrSaveAsync())
            {
                await OpenDocumentPathAsync(path);
                RecordRecentFile(path);
            }
            break;
        }

        if (imagePaths.Length > 0)
        {
            await ImportImageFilesAsync(imagePaths, droppedFiles.ClientX, droppedFiles.ClientY);
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
            case ClipboardImageHandling.Upload:
                if (GetDocumentAssetsDirectory() is { } dir)
                    return dir;
                SetStatus(Loc.Get("document.imageNotSavedUpload"));
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
            case FileImageHandling.Upload:
                if (GetDocumentAssetsDirectory() is { } dir)
                    return await _imageAssetService.CopyFileIntoAsync(sourcePath, dir);
                SetStatus(Loc.Get("document.imageNotSavedRefUpload"));
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
        if (_document is null || _editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        try
        {
            var selection = await _editorHost.RequestSelectionExportAsync();
            var src = ExtractImageSrc(selection.Markdown);
            if (src is null)
            {
                SetStatus(Loc.Get("status.noImageSelected"));
                return;
            }

            var absolutePath = ResolveImagePath(src);
            if (!File.Exists(absolutePath))
            {
                ShowMessage(this, Loc.Get("document.imageMissing"), "MarkLeaf",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Filter = ImageFilter,
                RestoreDirectory = true,
                Title = Loc.Get("contextMenu.image.saveAs"),
                FileName = Path.GetFileName(absolutePath),
            };
            if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
            {
                return;
            }

            File.Copy(absolutePath, dialog.FileName, overwrite: true);
            SetStatus(Loc.Get("status.imageSavedAs"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _logger.Error("Save image as failed.", exception);
        }
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
