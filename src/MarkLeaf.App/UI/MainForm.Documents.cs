using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private const string ImageFilter =
        "图片文件 (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|所有文件 (*.*)|*.*";
    private const string DocumentFilter =
        "Markdown 文件 (*.md;*.markdown)|*.md;*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";

    private void OnEditorDirtyChanged(object? sender, EditorMessage message)
    {
        if (_document is null || !message.Payload.TryGetProperty("dirty", out var dirtyElement))
        {
            return;
        }

        _document.IsDirty = dirtyElement.GetBoolean();
        _document.Revision = message.Revision;
        UpdateDocumentChrome();
    }

    private async Task NewDocumentAsync()
    {
        if (_documentOperationInProgress || !await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        StopWatchingDocument();
        _document = _documentFileService.CreateNew();
        LoadDocumentIntoEditor(_document);
        SetStatus("已新建文档");
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
            Title = "打开 Markdown 文档",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await OpenDocumentPathAsync(dialog.FileName);
    }

    private async Task OpenDocumentPathAsync(string path)
    {
        _documentOperationInProgress = true;
        try
        {
            SetStatus("正在打开文档...");
            var opened = await _documentFileService.OpenAsync(path);
            StopWatchingDocument();
            _document = opened;
            LoadDocumentIntoEditor(opened);
            StartWatchingDocument(opened.FilePath!);
            _logger.Info($"Document opened: {DescribePath(opened.FilePath)}; encoding={opened.Encoding.WebName}; bom={opened.HasBom}; newline={DescribeNewLine(opened.NewLine)}.");
            SetStatus(opened.IsReadOnly ? "已打开只读文档" : "文档已打开");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Document open failed: {DescribePath(path)}.", exception);
            MessageBox.Show(
                this,
                "无法打开该文档。\r\n\r\n" + exception.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("打开失败");
        }
        finally
        {
            _documentOperationInProgress = false;
            _menuService.RefreshStates();
        }
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
                DefaultExt = "md",
                RestoreDirectory = true,
                OverwritePrompt = true,
                Title = "保存 Markdown 文档",
                FileName = targetPath is null ? "未命名.md" : Path.GetFileName(targetPath),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return false;
            }

            targetPath = dialog.FileName;
            saveAs = !PathEquals(_document.FilePath, targetPath);
        }

        if (!_document.IsDirty && !saveAs && !forceOverwrite)
        {
            SetStatus("文档没有需要保存的修改");
            return true;
        }

        _documentOperationInProgress = true;
        EditorSnapshot? snapshot = null;
        AssetMigration? assetMigration = null;
        var documentSaved = false;
        try
        {
            SetStatus("正在获取最新编辑内容...");
            snapshot = await _editorHost.RequestSnapshotAsync();
            assetMigration = await _imageAssetService.PrepareMigrationAsync(
                _document,
                targetPath,
                snapshot.Markdown);
            SetStatus("正在安全保存...");
            await _documentFileService.SaveAsync(
                _document,
                assetMigration.Markdown,
                snapshot.Revision,
                targetPath,
                forceOverwrite);
            documentSaved = true;

            _document.Revision = Math.Max(snapshot.Revision, _editorSession.ConfirmedRevision);
            _document.IsDirty = _document.Revision > snapshot.Revision;
            _editorHost.SetAssetDirectory(assetMigration.TargetDirectory);
            _editorHost.UpdateImagePaths(assetMigration.PathMappings);
            StopWatchingDocument();
            StartWatchingDocument(_document.FilePath!);
            UpdateDocumentChrome();
            _logger.Info($"Document saved safely: {DescribePath(_document.FilePath)}; revision={snapshot.Revision}.");
            SetStatus("文档已保存");
            return true;
        }
        catch (ExternalDocumentChangedException)
        {
            if (assetMigration is not null)
            {
                ImageAssetService.RollbackMigration(assetMigration);
            }
            _logger.Warning($"Save blocked by external modification: {DescribePath(targetPath)}.");
            return await ResolveExternalSaveConflictAsync(
                targetPath,
                snapshot ?? new EditorSnapshot(_document.Markdown, _document.Revision));
        }
        catch (OperationCanceledException exception)
        {
            if (assetMigration is not null)
            {
                ImageAssetService.RollbackMigration(assetMigration);
            }
            _logger.Error("Latest editor snapshot request timed out.", exception);
            MessageBox.Show(
                this,
                "未能及时获取编辑器的最新内容，文档没有写入磁盘。请稍后重试。",
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("保存失败：编辑器快照超时");
            return false;
        }
        catch (DocumentSaveException exception)
        {
            if (assetMigration is not null)
            {
                ImageAssetService.RollbackMigration(assetMigration);
            }
            _logger.Error($"Document save failed: {DescribePath(targetPath)}.", exception);
            var recovery = exception.RecoveryFilePath is null
                ? string.Empty
                : $"\r\n\r\n可恢复临时文件：\r\n{exception.RecoveryFilePath}";
            MessageBox.Show(
                this,
                "安全保存失败，原文件未被截断，编辑内容仍保留在当前窗口中。" + recovery +
                "\r\n\r\n" + exception.InnerException?.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("保存失败，编辑内容仍保留");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!documentSaved && assetMigration is not null)
            {
                ImageAssetService.RollbackMigration(assetMigration);
            }
            _logger.Error($"Image resource migration failed: {DescribePath(targetPath)}.", exception);
            MessageBox.Show(
                this,
                documentSaved
                    ? "文档已经保存，但编辑器未能刷新图片资源映射。请重新打开文档。\r\n\r\n" + exception.Message
                    : "无法迁移文档图片资源，文档没有写入磁盘。\r\n\r\n" + exception.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus(documentSaved ? "文档已保存，图片资源刷新失败" : "保存失败：图片资源迁移失败");
            return documentSaved;
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
            dialog.ShowDialog(this);
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
                        comparison.ShowDialog(this);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        MessageBox.Show(
                            this,
                            "无法读取磁盘版本以进行比较。\r\n\r\n" + exception.Message,
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
                    SetStatus("已取消保存，外部版本未被覆盖");
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

        var choice = MessageBox.Show(
            this,
            $"是否保存对“{_document.DisplayName}”的修改？",
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
            SaveSettings();
            return;
        }

        eventArgs.Cancel = true;
        if (_documentOperationInProgress)
        {
            return;
        }

        if (_document?.IsDirty == true && !await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        _closeApproved = true;
        StopWatchingDocument();
        BeginInvoke(Close);
    }

    private void LoadDocumentIntoEditor(MarkdownDocument document)
    {
        _editorCommandStatus = EditorCommandStatus.Empty;
        _editorHost?.SetAssetDirectory(_imageAssetService.GetAssetDirectory(document));
        _editorHost?.LoadDocument(document.Id, document.Revision, document.Markdown);
        document.IsDirty = false;
        UpdateDocumentChrome();
    }

    private void UpdateDocumentChrome()
    {
        var name = _document?.DisplayName ?? "MarkLeaf";
        Text = _document is null
            ? "MarkLeaf"
            : $"{(_document.IsDirty ? "*" : string.Empty)}{name} - MarkLeaf";
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

            SetStatus("检测到磁盘文件已在外部修改");
            if (document.IsDirty)
            {
                MessageBox.Show(
                    this,
                    "磁盘上的当前文件已被其他程序修改。MarkLeaf 不会自动覆盖或重新加载，因为当前窗口也有未保存修改。请使用“保存”处理冲突，或使用“另存为”保留当前版本。",
                    "检测到外部修改",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var reload = MessageBox.Show(
                this,
                "磁盘上的当前文件已被其他程序修改。是否重新加载？",
                "检测到外部修改",
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
        await ImportImageFilesAsync(droppedFiles.Paths, droppedFiles.ClientX, droppedFiles.ClientY);
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
                SetStatus("剪贴板中没有可用图片");
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
                _document,
                stream.ToArray(),
                ".png");
            await InsertImportedImageAsync(imported, "粘贴图片");
        }
        catch (Exception exception) when (exception is IOException or ExternalException or InvalidDataException
            or OperationCanceledException)
        {
            _logger.Error("Clipboard image import failed.", exception);
            MessageBox.Show(
                this,
                "无法导入剪贴板图片。\r\n\r\n" + exception.Message,
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
            Title = "插入图片",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
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
                var imported = await _imageAssetService.ImportFileAsync(_document, path);
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

        SetStatus(importedCount > 0 ? $"已导入 {importedCount} 张图片" : "未找到可导入的图片");
    }

    private Task CleanUnreferencedAssetsAsync()
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true || _documentOperationInProgress)
        {
            return Task.CompletedTask;
        }

        if (_document.FilePath is null || _document.IsDirty)
        {
            MessageBox.Show(
                this,
                "为避免永久删除仍被已保存版本引用的图片，请先保存文档，再执行资源清理。",
                "清理未引用资源",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            SetStatus("清理已取消：请先保存文档");
            return Task.CompletedTask;
        }

        try
        {
            var unusedAssets = _imageAssetService.FindUnreferencedAssets(_document, _document.Markdown);
            if (unusedAssets.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "当前文档没有可清理的未引用图片。",
                    "清理未引用资源",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetStatus("没有未引用图片");
                return Task.CompletedTask;
            }

            var confirmation = MessageBox.Show(
                this,
                $"将永久删除当前资源目录中的 {unusedAssets.Count} 个未引用图片文件。\r\n\r\n" +
                "此操作不会移动到回收站，且无法撤销。是否继续？",
                "永久删除未引用资源",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                SetStatus("已取消资源清理");
                return Task.CompletedTask;
            }

            var deletedAssets = _imageAssetService.DeleteUnreferencedAssets(_document, _document.Markdown);
            _logger.Info($"Permanently deleted {deletedAssets.Count} unreferenced image assets by explicit command.");
            SetStatus($"已永久删除 {deletedAssets.Count} 个未引用图片");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Manual unused image cleanup failed.", exception);
            MessageBox.Show(
                this,
                "未能完成资源清理。部分文件可能已被删除。\r\n\r\n" + exception.Message,
                "清理未引用资源",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus("资源清理失败");
        }

        return Task.CompletedTask;
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

        _editorHost.SetAssetDirectory(_imageAssetService.GetAssetDirectory(_document));
        var inserted = await _editorHost.ExecuteCommandAsync(
            "insertImage",
            imported.RelativePath + "\n" + alt,
            clientX,
            clientY);
        if (!inserted)
        {
            _logger.Warning($"Editor rejected imported image: {DescribePath(imported.PhysicalPath)}.");
            SetStatus("图片已复制，但未能插入文档");
            return false;
        }

        SetStatus("图片已插入文档");
        return true;
    }
}
