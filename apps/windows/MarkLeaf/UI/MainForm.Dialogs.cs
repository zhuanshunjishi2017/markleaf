using System.Text.Json;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.ExternalLinks;
using MarkLeaf.Services.Recovery;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private void ShowPreferences()
    {
        var previousAssociateMarkdown = _settings.General.AssociateMarkdownFiles;
        var previousAssociateText = _settings.General.AssociateTextFiles;

        using var dialog = new PreferencesDialog(
            _settings,
            RecoverUnsavedFiles,
            ShowShortcutHelp,
            OpenThemeFolder,
            AddThemeFromFile,
            OpenCacheFolder,
            OpenLogFolder,
            ClearLogs,
            OpenSettingsJson,
            ClearHistory,
            ResetAllSettingsToDefaults);
        var previousLanguage = _settings.General.UiLanguage ?? "";
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;

        ColorThemeService.DefaultLightThemeId = _settings.Appearance.DefaultLightThemeId;
        ColorThemeService.DefaultDarkThemeId = _settings.Appearance.DefaultDarkThemeId;

        var newLanguage = _settings.General.UiLanguage ?? "";
        if (!string.Equals(previousLanguage, newLanguage, StringComparison.Ordinal))
        {
            ReloadUiLanguage(newLanguage);
        }

        _recoveryTimer.Interval = Math.Clamp(_settings.File.SnapshotIntervalSeconds, 10, 300) * 1000;
        _recoveryTimer.Stop();
        _recoveryTimer.Start();

        var editor = _settings.Editor;
        _editorHost?.ApplyCssVariables(editor.VisualLineHeight, editor.VisualFontSize, editor.VisualMaxContentWidth, editor.SourceFontSize, editor.SourceFontFamily, editor.SourceCjkFontFamily, editor.CjkLanguageTag.ToBcp47());
        _editorHost?.ApplySourceSettings(editor.SourceIndentWidth);
        ApplyBlockHandleVisibility();

        SetMarkdownStyle(_settings.MarkdownStyle);
        SetColorTheme(_settings.ColorTheme);
        SetZoomPercent(_settings.Appearance.ZoomPercent);
        TopMost = _settings.Appearance.TopMostWindow;
        _editorHost?.ApplyAutoHideScrollbar(_settings.Appearance.AutoHideScrollbars);
        ApplySidebarAutoHideScrollbar();

        // 仅在文件关联设置实际变化时才修改注册表。
        if (_settings.General.AssociateMarkdownFiles != previousAssociateMarkdown
            || _settings.General.AssociateTextFiles != previousAssociateText)
        {
            ApplyFileAssociations();
        }

        UpdateDocumentChrome();

        SaveSettings();
    }

    private void ShowAbout()
    {
        using var dialog = new AboutDialog();
        ShowModal(() => dialog.ShowDialog(this));
    }

    private void ShowShortcutHelp()
    {
        using var dialog = new ShortcutDialog();
        ShowModal(() => dialog.ShowDialog(this));
    }

    private async void ShowChangelog()
    {
        var changelogPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Changelog", "changelog.md");
        if (!File.Exists(changelogPath))
        {
            SetStatus(Loc.Get("changelog.notFound"));
            return;
        }

        var cachePath = Path.Combine(_paths.DefaultImageDirectory, "changelog.md");
        try
        {
            File.Copy(changelogPath, cachePath, overwrite: true);
        }
        catch
        {
            SetStatus(Loc.Get("changelog.openFailed"));
            return;
        }

        await OpenDocumentPathAsync(cachePath, readOnly: true);
    }

    private void OpenDocumentInNewWindow()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = DocumentFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            Title = Loc.Get("dialog.openInNewWindow"),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) == DialogResult.OK)
        {
            StartNewWindow(dialog.FileName);
        }
    }

    private async Task ExportPdfAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document is null)
        {
            return;
        }

        var docName = _document.FilePath is not null
            ? Path.GetFileName(_document.FilePath)
            : Loc.Get("common.unnamed");
        var defaultName = _document.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : Loc.Get("common.unnamed");
        using var dialog = new ExportDialog(
            docName, defaultName, _markdownStyle, StyleService.GetAllStyles(),
            _paths.WebView2UserDataDirectory, GeneratePreviewPdfAsync);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        var options = dialog.Options;
        if (options is null || string.IsNullOrWhiteSpace(options.OutputPath))
        {
            SetStatus(Loc.Get("export.emptyPath"));
            return;
        }

        if (dialog.ExportedPath is not null)
        {
            // 预览 PDF 已复制到目标路径，无需重新生成。
            var exportedName = Path.GetFileName(dialog.ExportedPath);
            var exportedDir = Path.GetDirectoryName(dialog.ExportedPath) ?? "";
            SetStatus(Loc.Get("export.complete"));
            _logger.Info($"Document exported: {options.Format}/{options.Style} → {dialog.ExportedPath}");
            ShowExportCompleteDialog(exportedName, dialog.ExportedPath, exportedDir);
            return;
        }

        await RunExportAsync(options, defaultName);
    }

    private async Task ExportHtmlAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document is null)
        {
            return;
        }

        var docName = _document.FilePath is not null
            ? Path.GetFileName(_document.FilePath)
            : Loc.Get("common.unnamed");
        var defaultName = _document.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : Loc.Get("common.unnamed");
        using var dialog = new HtmlExportDialog(docName, defaultName, _markdownStyle, StyleService.GetAllStyles());
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        var options = dialog.Options;
        if (options is null || string.IsNullOrWhiteSpace(options.OutputPath))
        {
            SetStatus(Loc.Get("export.emptyPath"));
            return;
        }

        await RunExportAsync(options, defaultName);
    }

    private async Task RunExportAsync(ExportOptions options, string defaultName)
    {
        if (_editorHost is null)
        {
            return;
        }

        var exportDir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(exportDir) && !Directory.Exists(exportDir))
        {
            Directory.CreateDirectory(exportDir);
        }

        try
        {
            SetStatus(Loc.Get("export.generating"));
            var editor = _settings.Editor;
            var colorThemeCss = ColorThemeService.GetThemeCss(options.ColorScheme);
            var html = await _editorHost.RequestExportAsync(
                options.Format,
                options.Style,
                options.HtmlHeader,
                options.HtmlFooter,
                editor.VisualFontSize,
                editor.VisualLineHeight,
                editor.VisualMaxContentWidth,
                colorThemeCss,
                defaultName);

            if (string.IsNullOrEmpty(html))
            {
                SetStatus(Loc.Get("export.noContent"));
                return;
            }

            var outputPath = options.OutputPath;
            if (!Path.HasExtension(outputPath))
            {
                outputPath = Path.ChangeExtension(
                    outputPath,
                    options.Format == "pdf" ? ".pdf" : ".html");
            }

            if (options.Format == "pdf")
            {
                SetStatus(Loc.Get("export.generatingPdf"));
                var pdfBytes = await _editorHost.PrintExportToPdfAsync(
                    html,
                    options.PaperSize,
                    options.Landscape,
                    options.MarginTop,
                    options.MarginBottom,
                    options.MarginLeft,
                    options.MarginRight);
                await File.WriteAllBytesAsync(outputPath, pdfBytes);
            }
            else
            {
                await File.WriteAllTextAsync(outputPath, html, System.Text.Encoding.UTF8);
            }

            SetStatus(Loc.Get("export.complete"));
            _logger.Info($"Document exported: {options.Format}/{options.Style} → {outputPath}");

            var exportedName = Path.GetFileName(outputPath);
            ShowExportCompleteDialog(exportedName, outputPath, exportDir!);
        }
        catch (OperationCanceledException)
        {
            _logger.Warning($"Export timed out or was cancelled: {options.OutputPath}.");
            SetStatus(Loc.Get("export.failed"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Export failed: {options.OutputPath}.", exception);
            ShowMessage(this, Loc.Get("export.failed") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception exception)
        {
            _logger.Error($"Export failed: {options.OutputPath}.", exception);
            ShowMessage(this, Loc.Get("export.failed") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PrintDocument()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document is null)
        {
            return;
        }

        _editorHost.PrintDocument();
    }

    private async Task<byte[]> GeneratePreviewPdfAsync(ExportOptions options)
    {
        if (_editorHost is null || _document is null)
        {
            return [];
        }

        var colorThemeCss = ColorThemeService.GetThemeCss(options.ColorScheme);
        var html = await GeneratePrintHtmlAsync(options.Format, options.Style, options.HtmlHeader, options.HtmlFooter, colorThemeCss);
        if (string.IsNullOrEmpty(html)) return [];

        return await _editorHost.PrintExportToPdfAsync(
            html,
            options.PaperSize,
            options.Landscape,
            options.MarginTop,
            options.MarginBottom,
            options.MarginLeft,
            options.MarginRight);
    }

    private async Task<string> GeneratePrintHtmlAsync(string format, string style, string header, string footer, string colorSchemeCss)
    {
        if (_editorHost is null || _document is null)
        {
            return "";
        }

        SetStatus(Loc.Get("print.generating"));
        var editor = _settings.Editor;
        var title = _document.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : Loc.Get("common.unnamed");
        var html = await _editorHost.RequestExportAsync(
            format,
            style,
            header,
            footer,
            editor.VisualFontSize,
            editor.VisualLineHeight,
            editor.VisualMaxContentWidth,
            colorSchemeCss,
            title);
        if (string.IsNullOrEmpty(html))
        {
            SetStatus(Loc.Get("export.noContent"));
        }
        return html;
    }

    private void ShowExportCompleteDialog(string fileName, string filePath, string folderPath)
    {
        var openButton = new TaskDialogButton(Loc.Get("export.open"));
        openButton.Click += (_, _) => ExternalLinkService.OpenLocal(filePath);

        var openFolderButton = new TaskDialogButton(Loc.Get("export.openFolder"));
        openFolderButton.Click += (_, _) => ExternalLinkService.OpenLocal(folderPath);

        var page = new TaskDialogPage
        {
            Caption = "MarkLeaf",
            Icon = TaskDialogIcon.Information,
            Heading = Loc.Get("export.complete"),
            Text = Loc.Format("status.exportCompleteWithPath", fileName, filePath),
            Buttons = { openButton, openFolderButton, TaskDialogButton.Close },
        };

        ShowModal(() => TaskDialog.ShowDialog(this, page));
    }

    private void InsertLink()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new LinkInputDialog();
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand("setLink", dialog.LinkAddress);
        SetStatus(Loc.Get("status.linkInserted"));
    }

    private void InsertTable()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new TableSizeDialog();
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand("insertTable", $"{dialog.Rows},{dialog.Columns}");
        SetStatus(CommandStatusFormatter.FormatExecuted(AppCommand.InsertTable));
    }

    private void InsertMath(bool isBlock)
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        var command = isBlock ? "insertMathBlock" : "insertMathInline";

        // 有选区：直接套 $...$ / $$...$$，不弹框
        if (_editorCommandStatus.HasSelection)
        {
            _editorHost.ExecuteCommand(command);
            SetStatus(isBlock ? Loc.Get("status.mathBlockInserted") : Loc.Get("status.mathInlineInserted"));
            return;
        }

        using var dialog = new MathInputDialog(isBlock);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dialog.Latex))
        {
            return;
        }

        _editorHost.ExecuteCommand(command, dialog.Latex);
        SetStatus(isBlock ? Loc.Get("status.mathBlockInserted") : Loc.Get("status.mathInlineInserted"));
    }

    private void EditMath()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        var isBlock = _editorCommandStatus.MathBlock;
        using var dialog = new MathInputDialog(isBlock, _editorCommandStatus.MathLatex ?? "");
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dialog.Latex))
        {
            return;
        }

        _editorHost.ExecuteCommand("updateMath", dialog.Latex);
        SetStatus(Loc.Get("status.mathUpdated"));
    }

    private void RecoverUnsavedFiles()
    {
        var pending = RecoveryService.GetPendingRecoveries(_paths.RecoveryDirectory, _logger);
        if (pending.Count == 0)
        {
            ShowMessage(this, Loc.Get("dialog.noRecoverableFiles"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new RecoveryDialog(pending);
        ShowModal(() => dialog.ShowDialog(this));

        switch (dialog.Choice)
        {
            case RecoveryChoice.Restore when dialog.SelectedSnapshot is not null:
                SaveAndOpenRecovery(dialog.SelectedSnapshot);
                break;
            case RecoveryChoice.Discard:
                foreach (var snapshot in pending)
                {
                    foreach (var file in Directory.GetFiles(
                        _paths.RecoveryDirectory,
                        $"doc-*-{snapshot.DocumentId:N}.*"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                break;
        }
    }

    private async void SaveAndOpenRecovery(RecoverySnapshot recovery)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = Loc.Get("fileFilter.markdown"),
            AddExtension = true,
            DefaultExt = "md",
            RestoreDirectory = true,
            OverwritePrompt = true,
            Title = Loc.Get("dialog.saveRecovery"),
            FileName = recovery.DocumentPath is not null
                ? Path.GetFileName(recovery.DocumentPath)
                : (recovery.DisplayName ?? Loc.Get("document.untitledMd")),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;

        _documentOperationInProgress = true;
        try
        {
            var targetPath = dialog.FileName;
            await File.WriteAllTextAsync(targetPath, recovery.Markdown, System.Text.Encoding.UTF8);

            foreach (var file in Directory.GetFiles(
                _paths.RecoveryDirectory,
                $"doc-*-{recovery.DocumentId:N}.*"))
            {
                try { File.Delete(file); } catch { }
            }

            StopWatchingDocument();
            var opened = await _documentFileService.OpenAsync(targetPath);
            _document = opened;
            _workspaceTree.SelectedPath = opened.FilePath;
            _workspaceDocumentList.SelectedPath = opened.FilePath;
            LoadDocumentIntoEditor(opened);
            StartWatchingDocument(opened.FilePath!);
            _logger.Info($"Recovery snapshot saved and opened: {targetPath}.");
            SetStatus(Loc.Get("status.recoveredUnsaved"));
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to save recovery snapshot.", exception);
            ShowMessage(this,
                Loc.Get("dialog.saveRecoveryFailed") + "\r\n\r\n" + exception.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _documentOperationInProgress = false;
        }
    }

    /// <summary>
    /// 显示模态对话框/消息框期间临时关闭窗口置顶，避免 TopMost 主窗口覆盖其
    /// 自身弹出的二级窗口；对话框关闭后恢复置顶。
    /// </summary>
    private T ShowModal<T>(Func<T> show)
    {
        var wasTopMost = TopMost;
        if (wasTopMost)
        {
            TopMost = false;
        }

        try
        {
            return show();
        }
        finally
        {
            if (wasTopMost)
            {
                TopMost = true;
            }
        }
    }

    private DialogResult ShowMessage(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        return ShowModal(() => MessageBox.Show(owner, text, caption, buttons, icon));
    }

    private DialogResult ShowMessage(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        return ShowModal(() => MessageBox.Show(owner, text, caption, buttons, icon, defaultButton));
    }
}
