using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.CodeFormatting;
using MarkLeaf.Services.ExternalLinks;
using MarkLeaf.Services.Recovery;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private void ShowPreferences()
    {
        var previousAssociateMarkdown = _settings.General.AssociateMarkdownFiles;
        var previousAssociateText = _settings.General.AssociateTextFiles;
        var previousLanguage = _settings.General.UiLanguage ?? "";

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
            () => _ = ClearHistoryAsync(),
            () => _ = CheckForUpdatesAsync());
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;
        ApplyPreferencesChanges(previousAssociateMarkdown, previousAssociateText, previousLanguage);
    }

    private void ApplyPreferencesChanges(
        bool previousAssociateMarkdown,
        bool previousAssociateText,
        string previousLanguage)
    {
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
            _editorHost?.ApplyCssVariables(editor.VisualLineHeight, editor.VisualFontSize, editor.VisualMaxContentWidth, editor.SourceFontSize, editor.SourceFontFamily, editor.SourceCjkFontFamily, editor.CjkLanguageTag.ToBcp47(), editor.VisualCjkAutoSpacing, editor.VisualIgnoreMaxContentWidth);
        _editorHost?.ApplySourceSettings(editor.SourceIndentWidth);
        _editorHost?.ApplyAutoConvertUnsafeEmphasis(editor.AutoConvertUnsafeEmphasis);
        _editorHost?.ApplyMarkdownEditingSettings(editor);
        ApplyCodeFormatterSettings();
        ApplyCodeHighlightVisibility();
        ApplyBlockHandleVisibility();

        SetMarkdownStyle(_settings.MarkdownStyle);
        ApplyEffectiveColorTheme();
        SetZoomPercent(_settings.Appearance.ZoomPercent);
        TopMost = _settings.Appearance.TopMostWindow;
        _editorHost?.ApplyAutoHideScrollbar(_settings.Appearance.AutoHideScrollbars);
        ApplySidebarAutoHideScrollbar();
        RefreshPersistentStatusBar();
        ApplyMenuPresentation();
        _shortcutManager.ImportSettings(_settings.Shortcut);
        _menuService.RebuildMenu();

        // 仅在文件关联设置实际变化时才修改注册表。
        if (_settings.General.AssociateMarkdownFiles != previousAssociateMarkdown
            || _settings.General.AssociateTextFiles != previousAssociateText)
        {
            ApplyFileAssociations();
        }

        UpdateDocumentChrome();

        SaveSettings();
    }

    /// <summary>
    /// 打开外部代码格式化器管理对话框。变更立即写入活动设置（管理器语义，
    /// 与 macOS 一致），并即时下发可用语言与持久化。
    /// </summary>
    private void ShowCodeFormatterManager()
    {
        // GUI 应用收不到终端新装工具的事件：打开管理器前重新探测一次，
        // 若出现新工具立即下发并提醒（对应 macOS 的同款契约）。
        var before = _lastFormatterLanguages is not null
            ? new HashSet<string>(_lastFormatterLanguages, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(ExternalCodeFormatterCatalog.AvailableLanguages(_settings.CodeFormatter.ToolPaths), StringComparer.OrdinalIgnoreCase);
        ApplyCodeFormatterSettings();
        if (_lastFormatterLanguages is not null
            && _lastFormatterLanguages.Except(before, StringComparer.OrdinalIgnoreCase).Any())
        {
            ShowMessage(
                this,
                Loc.Get("codeFormatter.newToolsDetected"),
                Loc.Get("codeFormatter.dialogTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        using var dialog = new CodeFormatterSettingsDialog(_settings.CodeFormatter, () =>
        {
            ApplyCodeFormatterSettings();
            SaveSettings();
        });
        ShowModal(() => dialog.ShowDialog(this));
    }

    private void ShowAbout()
    {
        using var dialog = new AboutDialog();
        ShowModal(() => dialog.ShowDialog(this));
    }

    private void ShowShortcutHelp()
    {
        using var dialog = new ShortcutDialog(_shortcutManager);
        ShowModal(() => dialog.ShowDialog(this));
    }

    private async void ShowChangelog()
    {
        await ShowChangelogAsync();
    }

    private async Task ShowChangelogAsync()
    {
        var changelogPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Changelog", "changelog.md");
        if (!File.Exists(changelogPath))
        {
            SetStatus(Loc.Get("changelog.notFound"));
            return;
        }

        var cachePath = Path.Combine(_paths.DataDirectory, "Cache", "changelog.md");
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

    private void ShowOptionalFonts()
    {
        using var dialog = new OptionalFontsDialog();
        ShowModal(() => dialog.ShowDialog(this));
    }

    private async Task OpenBundledSampleAsync(string fileName)
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Samples", fileName);
        if (!File.Exists(sourcePath))
        {
            SetStatus(Loc.Get("welcome.notFound"));
            return;
        }

        string markdown;
        try
        {
            markdown = File.ReadAllText(sourcePath);
        }
        catch
        {
            SetStatus(Loc.Get("welcome.openFailed"));
            return;
        }

        // Help samples are practice documents. Keep them editable but untitled so
        // opening another sample never overwrites a previous exercise.
        var document = _documentFileService.CreateNew();
        document.Markdown = markdown;
        document.SavedMarkdown = markdown;
        await AddAndActivateDocumentAsync(document);
    }

    private async Task ShowWelcomeAsync()
    {
        var culture = _settings.General.UiLanguage;
        if (string.IsNullOrWhiteSpace(culture))
        {
            culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        }

        var welcomeFileName = culture switch
        {
            "zh-TW" => "welcome.zh-TW.md",
            "en-US" => "welcome.en-US.md",
            "ja-JP" => "welcome.ja-JP.md",
            _ => "welcome.md",
        };
        var welcomePath = Path.Combine(AppContext.BaseDirectory, "Resources", welcomeFileName);
        if (!File.Exists(welcomePath))
        {
            welcomePath = Path.Combine(AppContext.BaseDirectory, "Resources", "welcome.md");
        }
        if (!File.Exists(welcomePath))
        {
            SetStatus(Loc.Get("welcome.notFound"));
            return;
        }

        var cachePath = Path.Combine(_paths.DataDirectory, "Cache", "welcome.md");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.Copy(welcomePath, cachePath, overwrite: true);
            var markdown = File.ReadAllText(cachePath);
            var document = _documentFileService.CreateNew();
            document.FilePath = cachePath;
            document.Markdown = markdown;
            document.SavedMarkdown = markdown;
            document.IsReadOnly = true;
            await AddAndActivateDocumentAsync(document);
        }
        catch
        {
            SetStatus(Loc.Get("welcome.openFailed"));
            return;
        }
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

    private Task ExportPdfAsync() => ShowExportDialogAsync(ExportDialogMode.Pdf);

    private Task ExportHtmlAsync() => ShowExportDialogAsync(ExportDialogMode.Html);

    private Task ExportImageAsync() => ShowExportDialogAsync(ExportDialogMode.Image);

    /// <summary>Opens the unified macOS-compatible export window with the saved format preselected.</summary>
    private Task ExportDocumentAsync()
    {
        var format = NormalizeExportFormat(_settings.Export.Format);
        var mode = format switch
        {
            "html" => ExportDialogMode.Html,
            "image" => ExportDialogMode.Image,
            _ => ExportDialogMode.Pdf,
        };
        return ShowExportDialogAsync(mode);
    }

    private async Task ShowExportDialogAsync(ExportDialogMode mode)
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
            _paths.WebView2UserDataDirectory, GeneratePreviewPdfAsync, GeneratePreviewHtmlAsync,
            mode, _settings.Export, lastSettingsOptionsProvider: () => BuildLastExportOptions(""));
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
            SaveLastExportOptions(options);
            // The PDF preview is already copied to the confirmed destination;
            // rerunning the export would duplicate expensive rendering work.
            var exportedName = Path.GetFileName(dialog.ExportedPath);
            var exportedDir = Path.GetDirectoryName(dialog.ExportedPath) ?? "";
            SetStatus(Loc.Get("export.complete"));
            _logger.Info($"Document exported: {options.Format}/{options.Style} → {dialog.ExportedPath}");
            ShowExportCompleteDialog(exportedName, dialog.ExportedPath, exportedDir);
            return;
        }

        SaveLastExportOptions(options);
        await RunExportAsync(options, defaultName);
    }

    private async Task ExportWithLastSettingsAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document is null)
        {
            return;
        }

        var defaultName = _document.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : Loc.Get("common.unnamed");
        var format = NormalizeExportFormat(_settings.Export.Format);
        var outputPath = PromptExportPath(format, defaultName);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var options = BuildLastExportOptions(outputPath);
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
            var exportWidth = options.Format == "image"
                ? options.ImageContentWidth
                : editor.VisualMaxContentWidth;
            var colorThemeCss = ColorThemeService.GetThemeCss(options.ColorScheme);
            var html = await _editorHost.RequestExportAsync(
                options.Format,
                options.Style,
                options.HtmlHeader,
                options.HtmlFooter,
                editor.VisualFontSize,
                editor.VisualLineHeight,
                exportWidth,
                editor.VisualCjkAutoSpacing,
                colorThemeCss,
                defaultName,
                options.KeepTablesTogether,
                options.KeepHeadingsWithNextBlock);

            if (string.IsNullOrEmpty(html))
            {
                SetStatus(Loc.Get("export.noContent"));
                return;
            }

            // Export HTML is rendered in a separate WebView/file context where
            // the in-app assets.local virtual host does not exist. Resolve
            // relative image references against the source document and embed
            // local files as data URIs so both HTML and PDF exports retain them.
            html = EmbedExportImages(html, _document?.FilePath);

            var outputPath = options.OutputPath;
            if (!Path.HasExtension(outputPath))
            {
                outputPath = Path.ChangeExtension(
                    outputPath,
                    options.Format == "pdf"
                        ? ".pdf"
                        : options.Format == "image"
                            ? (options.ImageFormat == "jpg" ? ".jpg" : ".png")
                            : ".html");
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
                    options.MarginRight,
                    ResolvePdfHeaderFooterPlaceholders(options.PdfHeaderText, defaultName),
                    options.PdfHeaderAlignment,
                    ResolvePdfHeaderFooterPlaceholders(options.PdfFooterText, defaultName),
                    options.PdfFooterAlignment,
                    ResolveHeaderFooterFontFamily(options.Style));
                await File.WriteAllBytesAsync(outputPath, pdfBytes);
            }
            else if (options.Format == "image")
            {
                SetStatus(Loc.Get("export.generatingImage"));
                var paths = await _editorHost.CaptureExportImagesAsync(
                    html,
                    outputPath,
                    options.ImageContentWidth,
                    options.ImageMaxHeight,
                    options.ImageScale,
                    options.ImageFormat,
                    options.ImageJpegQuality);
                if (paths.Count == 0)
                {
                    SetStatus(Loc.Get("export.noContent"));
                    return;
                }
                outputPath = paths[0];
                exportDir = Path.GetDirectoryName(outputPath);
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

    private async Task RunCommandLineExportAsync()
    {
        try
        {
            var config = string.IsNullOrWhiteSpace(_options.ExportConfigPath)
                ? null
                : await CommandLineExportOptions.LoadAsync(_options.ExportConfigPath);
            var configDirectory = string.IsNullOrWhiteSpace(_options.ExportConfigPath)
                ? null
                : Path.GetDirectoryName(_options.ExportConfigPath);
            var requestedFormat = config?.Format ?? _settings.Export.Format;
            var format = requestedFormat.Trim().ToLowerInvariant() switch
            {
                "pdf" => "pdf",
                "html" => "html",
                "png" or "jpg" or "image" => "image",
                _ => throw new InvalidDataException("format must be pdf, html, png or jpg"),
            };
            var input = _options.ExportInputPath is not null
                ? CommandLineExportOptions.ResolvePath(_options.ExportInputPath)
                : config?.Input is not null
                    ? CommandLineExportOptions.ResolvePath(config.Input, configDirectory)
                    : CommandLineExportOptions.ResolvePath(_document?.FilePath
                        ?? throw new InvalidDataException("Export input is required."));
            var imageFormat = requestedFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                ? "jpg"
                : config?.Image.Format ?? _settings.Export.ImageFormat;
            var extension = format == "pdf" ? ".pdf" : format == "html" ? ".html" : imageFormat == "jpg" ? ".jpg" : ".png";
            var requestedOutput = _options.ExportOutputPath is not null
                ? CommandLineExportOptions.ResolvePath(_options.ExportOutputPath)
                : config?.Output is not null
                    ? CommandLineExportOptions.ResolvePath(config.Output, configDirectory)
                    : null;
            var output = ResolveCommandLineExportOutput(input, requestedOutput, extension);
            if (!(config?.Overwrite ?? false) && File.Exists(output)) throw new IOException("Output file already exists.");
            var export = config is null
                ? BuildLastExportOptions(output)
                : new ExportOptions(
                    format, config.Paper.Size, string.Equals(config.Paper.Orientation, "landscape", StringComparison.OrdinalIgnoreCase),
                    config.Paper.Margin.Top, config.Paper.Margin.Bottom, config.Paper.Margin.Left, config.Paper.Margin.Right,
                    config.Html.Header, config.Html.Footer, config.Header.Text, config.Header.Alignment,
                    config.Footer.Text, config.Footer.Alignment, "", "", "", "", config.Style ?? _markdownStyle,
                    config.ColorScheme ?? _colorTheme, config.Layout.KeepTablesTogether, config.Layout.KeepHeadingsWithNextBlock,
                    Math.Clamp(config.Image.MaxHeight, 1000, 30000), Math.Clamp(config.Image.ContentWidth, 320, 4000),
                    float.IsFinite(config.Image.Scale) ? Math.Clamp(config.Image.Scale, 1f, 4f) : 2f,
                    imageFormat, Math.Clamp(config.Image.JpegQuality, 1, 100), output);
            var title = string.IsNullOrWhiteSpace(config?.Html.Title)
                ? Path.GetFileNameWithoutExtension(_document?.FilePath ?? output)
                : config.Html.Title;
            var success = await RunExportCoreAsync(export, title);
            var report = new { success, input, output, format, warnings = Array.Empty<string>() };
            if (!string.IsNullOrWhiteSpace(config?.Report))
            {
                var reportPath = CommandLineExportOptions.ResolvePath(config.Report, configDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }
            Environment.ExitCode = success ? 0 : 5;
        }
        catch (Exception exception)
        {
            _logger.Error("Command-line export failed.", exception);
            Environment.ExitCode = exception is InvalidDataException ? 1 : 5;
        }
        finally
        {
            _commandLineExportCompleted = true;
            BeginInvoke(Close);
        }
    }

    private static string ResolveCommandLineExportOutput(string input, string? requestedOutput, string extension)
    {
        if (string.IsNullOrWhiteSpace(requestedOutput))
            return Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + extension);

        var output = Path.GetFullPath(requestedOutput);
        if (Directory.Exists(output) || Path.EndsInDirectorySeparator(requestedOutput))
            return Path.Combine(output, Path.GetFileNameWithoutExtension(input) + extension);
        return Path.HasExtension(output) ? output : Path.ChangeExtension(output, extension);
    }

    private async Task WatchCommandLineExportTimeoutAsync()
    {
        var seconds = 120;
        try
        {
            if (!string.IsNullOrWhiteSpace(_options.ExportConfigPath))
            {
                var config = await CommandLineExportOptions.LoadAsync(_options.ExportConfigPath);
                seconds = Math.Clamp(config.Runtime.TimeoutSeconds, 10, 1800);
            }
        }
        catch
        {
            // Configuration errors are handled by the normal export path.
        }
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        if (_commandLineExportCompleted || IsDisposed) return;
        _logger.Warning($"Command-line export timed out after {seconds} seconds.");
        Environment.ExitCode = 7;
        _commandLineExportCompleted = true;
        if (IsHandleCreated) BeginInvoke(Close);
    }

    private async Task BeginCommandLineExportIfRequestedAsync()
    {
        if (_commandLineExportStarted || !_options.IsExportCommand || _editorHost?.IsDocumentLoaded != true)
            return;
        _commandLineExportStarted = true;
        await RunCommandLineExportAsync();
    }

    private async Task<bool> RunExportCoreAsync(ExportOptions options, string defaultName)
    {
        var exportDir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(exportDir)) Directory.CreateDirectory(exportDir);
        var editor = _settings.Editor;
        var exportWidth = options.Format == "image" ? options.ImageContentWidth : editor.VisualMaxContentWidth;
        var html = await _editorHost!.RequestExportAsync(options.Format, options.Style, options.HtmlHeader, options.HtmlFooter,
            editor.VisualFontSize, editor.VisualLineHeight, exportWidth, editor.VisualCjkAutoSpacing,
            ColorThemeService.GetThemeCss(options.ColorScheme), defaultName, options.KeepTablesTogether, options.KeepHeadingsWithNextBlock);
        if (string.IsNullOrEmpty(html)) return false;
        html = EmbedExportImages(html, _document?.FilePath);
        var output = options.OutputPath;
        if (!Path.HasExtension(output)) output = Path.ChangeExtension(output, options.Format == "pdf" ? ".pdf" : options.Format == "image" ? (options.ImageFormat == "jpg" ? ".jpg" : ".png") : ".html");
        if (options.Format == "pdf")
        {
            var bytes = await _editorHost.PrintExportToPdfAsync(html, options.PaperSize, options.Landscape, options.MarginTop, options.MarginBottom, options.MarginLeft, options.MarginRight,
                ResolvePdfHeaderFooterPlaceholders(options.PdfHeaderText, defaultName), options.PdfHeaderAlignment,
                ResolvePdfHeaderFooterPlaceholders(options.PdfFooterText, defaultName), options.PdfFooterAlignment, ResolveHeaderFooterFontFamily(options.Style));
            await File.WriteAllBytesAsync(output, bytes);
        }
        else if (options.Format == "image")
        {
            var paths = await _editorHost.CaptureExportImagesAsync(html, output, options.ImageContentWidth, options.ImageMaxHeight, options.ImageScale, options.ImageFormat, options.ImageJpegQuality);
            if (paths.Count == 0) return false;
        }
        else await File.WriteAllTextAsync(output, html, System.Text.Encoding.UTF8);
        return true;
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
        var html = await GeneratePrintHtmlAsync(options.Format, options.Style, options.HtmlHeader, options.HtmlFooter, colorThemeCss, options.KeepTablesTogether, options.KeepHeadingsWithNextBlock);
        if (string.IsNullOrEmpty(html)) return [];

        return await _editorHost.PrintExportToPdfAsync(
            html,
            options.PaperSize,
            options.Landscape,
            options.MarginTop,
            options.MarginBottom,
            options.MarginLeft,
            options.MarginRight,
            ResolvePdfHeaderFooterPlaceholders(options.PdfHeaderText, GetExportTitle()),
            options.PdfHeaderAlignment,
            ResolvePdfHeaderFooterPlaceholders(options.PdfFooterText, GetExportTitle()),
            options.PdfFooterAlignment,
            ResolveHeaderFooterFontFamily(options.Style));
    }

    private ExportOptions BuildLastExportOptions(string outputPath)
    {
        var export = _settings.Export;
        return new ExportOptions(
            Format: NormalizeExportFormat(export.Format),
            PaperSize: NormalizePaperSize(export.PaperSize),
            Landscape: export.Landscape,
            MarginTop: NormalizeMargin(export.MarginTop, 25.4f),
            MarginBottom: NormalizeMargin(export.MarginBottom, 25.4f),
            MarginLeft: NormalizeMargin(export.MarginLeft, 31.7f),
            MarginRight: NormalizeMargin(export.MarginRight, 31.7f),
            HtmlHeader: export.HtmlHeader ?? "",
            HtmlFooter: export.HtmlFooter ?? "",
            PdfHeaderText: ResolvePdfHeaderFooterText(export.PdfHeaderPreset, export.PdfHeaderCustom),
            PdfHeaderAlignment: ResolvePdfHeaderFooterAlignment(export.PdfHeaderPreset),
            PdfFooterText: ResolvePdfHeaderFooterText(export.PdfFooterPreset, export.PdfFooterCustom),
            PdfFooterAlignment: ResolvePdfHeaderFooterAlignment(export.PdfFooterPreset),
            PdfHeaderPreset: NormalizeHeaderFooterPreset(export.PdfHeaderPreset),
            PdfFooterPreset: NormalizeHeaderFooterPreset(export.PdfFooterPreset),
            PdfHeaderCustom: export.PdfHeaderCustom ?? "",
            PdfFooterCustom: export.PdfFooterCustom ?? "",
            Style: ResolveExportStyle(export.Style),
            ColorScheme: ResolveExportColorScheme(export.ColorScheme),
            KeepTablesTogether: export.KeepTablesTogether,
            KeepHeadingsWithNextBlock: export.KeepHeadingsWithNextBlock,
            ImageMaxHeight: Math.Clamp(export.ImageMaxHeight, 1000, 30000),
            ImageContentWidth: Math.Clamp(export.ImageContentWidth, 320, 4000),
            ImageScale: float.IsFinite(export.ImageScale) ? Math.Clamp(export.ImageScale, 1f, 4f) : 2f,
            ImageFormat: string.Equals(export.ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png",
            ImageJpegQuality: Math.Clamp(export.ImageJpegQuality, 1, 100),
            OutputPath: outputPath);
    }

    private void SaveLastExportOptions(ExportOptions options)
    {
        _settings.Export = new ExportSettings
        {
            Format = NormalizeExportFormat(options.Format),
            PaperSize = NormalizePaperSize(options.PaperSize),
            Landscape = options.Landscape,
            MarginTop = NormalizeMargin(options.MarginTop, 25.4f),
            MarginBottom = NormalizeMargin(options.MarginBottom, 25.4f),
            MarginLeft = NormalizeMargin(options.MarginLeft, 31.7f),
            MarginRight = NormalizeMargin(options.MarginRight, 31.7f),
            HtmlHeader = options.HtmlHeader ?? "",
            HtmlFooter = options.HtmlFooter ?? "",
            PdfHeaderPreset = NormalizeHeaderFooterPreset(options.PdfHeaderPreset),
            PdfFooterPreset = NormalizeHeaderFooterPreset(options.PdfFooterPreset),
            PdfHeaderCustom = options.PdfHeaderCustom ?? "",
            PdfFooterCustom = options.PdfFooterCustom ?? "",
            PdfHeaderText = options.PdfHeaderText ?? "",
            PdfHeaderAlignment = options.PdfHeaderAlignment ?? "",
            PdfFooterText = options.PdfFooterText ?? "",
            PdfFooterAlignment = options.PdfFooterAlignment ?? "",
            Style = ResolveExportStyle(options.Style),
            ColorScheme = ResolveExportColorScheme(options.ColorScheme),
            KeepTablesTogether = options.KeepTablesTogether,
            KeepHeadingsWithNextBlock = options.KeepHeadingsWithNextBlock,
            ImageMaxHeight = Math.Clamp(options.ImageMaxHeight, 1000, 30000),
            ImageContentWidth = Math.Clamp(options.ImageContentWidth, 320, 4000),
            ImageScale = float.IsFinite(options.ImageScale) ? Math.Clamp(options.ImageScale, 1f, 4f) : 2f,
            ImageFormat = string.Equals(options.ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png",
            ImageJpegQuality = Math.Clamp(options.ImageJpegQuality, 1, 100),
        };
        SaveSettings();
    }

    private void ApplyStatusBarSettingsFromPreferences(StatusBarSettings settings)
    {
        _settings.Appearance.StatusBar = settings.Clone();
        RefreshPersistentStatusBar();
        SaveSettings();
    }

    private string? PromptExportPath(string format, string defaultName)
    {
        var isPdf = string.Equals(format, "pdf", StringComparison.Ordinal);
        var isImage = string.Equals(format, "image", StringComparison.Ordinal);
        var imageFormat = string.Equals(_settings.Export.ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";
        var extension = isPdf ? "pdf" : isImage ? imageFormat : "html";
        var filter = isPdf
            ? $"{Loc.Get("export.pdf")}|*.pdf"
            : isImage
                ? (imageFormat == "jpg" ? $"{Loc.Get("export.jpeg")}|*.jpg;*.jpeg" : $"{Loc.Get("export.png")}|*.png")
                : $"{Loc.Get("export.html")}|*.html";
        using var dialog = new SaveFileDialog
        {
            Filter = filter,
            AddExtension = true,
            DefaultExt = extension,
            RestoreDirectory = true,
            OverwritePrompt = true,
            FileName = $"{defaultName}.{extension}",
        };

        return ShowModal(() => dialog.ShowDialog(this)) == DialogResult.OK ? dialog.FileName : null;
    }

    private static string NormalizeExportFormat(string? format) =>
        string.Equals(format, "html", StringComparison.OrdinalIgnoreCase) ? "html"
        : string.Equals(format, "image", StringComparison.OrdinalIgnoreCase) ? "image"
        : "pdf";

    private static string NormalizeHeaderFooterPreset(string? preset) =>
        preset is "title-left" or "page-center" or "page-right" or "page-total-center" or "custom"
            ? preset
            : "none";

    private static string ResolvePdfHeaderFooterText(string? preset, string? customText)
    {
        return NormalizeHeaderFooterPreset(preset) switch
        {
            "title-left" => "{document-title}",
            "page-center" or "page-right" => "{page}",
            "page-total-center" => Loc.Get("export.headerFooterPageTotalTemplate"),
            "custom" => customText ?? "",
            _ => "",
        };
    }

    private static string ResolvePdfHeaderFooterAlignment(string? preset)
    {
        return NormalizeHeaderFooterPreset(preset) switch
        {
            "title-left" => "left",
            "page-center" or "page-total-center" or "custom" => "center",
            "page-right" => "right",
            _ => "",
        };
    }

    private static string ResolvePdfHeaderFooterPlaceholders(string text, string documentTitle)
    {
        return text.Replace("{document-title}", documentTitle, StringComparison.Ordinal);
    }

    private string GetExportTitle()
    {
        return _document?.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : Loc.Get("common.unnamed");
    }

    private static string NormalizePaperSize(string? paperSize)
    {
        string[] valid = ["A4", "A3", "A5", "Letter", "Legal", "B4", "B5"];
        return valid.Contains(paperSize, StringComparer.Ordinal) ? paperSize! : "A4";
    }

    private static float NormalizeMargin(float margin, float fallback) =>
        float.IsFinite(margin) && margin >= 0f && margin <= 1000f ? margin : fallback;

    private string ResolveExportStyle(string? style)
    {
        if (!string.IsNullOrWhiteSpace(style)
            && StyleService.GetAllStyles().Any(s => string.Equals(s.Id, style, StringComparison.Ordinal)))
        {
            return style;
        }

        return StyleService.GetAllStyles().Any(s => string.Equals(s.Id, _markdownStyle, StringComparison.Ordinal))
            ? _markdownStyle
            : "serif";
    }

    private static string ResolveHeaderFooterFontFamily(string styleId)
    {
        var declarations = new List<string>();
        CollectFontFamilyDeclarations(StyleService.BaseCss, declarations);
        foreach (var style in ResolveStyleCascade(styleId))
        {
            CollectFontFamilyDeclarations(style.Css, declarations);
        }

        return declarations.LastOrDefault()
            ?? "serif, \"Source Han Serif CN\", \"Noto Serif CJK CN\"";
    }

    private static IEnumerable<StyleDefinition> ResolveStyleCascade(string styleId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<StyleDefinition>();

        void Visit(string id)
        {
            if (!seen.Add(id)) return;
            var style = StyleService.TryGetStyle(id);
            if (style is null) return;
            if (!string.IsNullOrWhiteSpace(style.DependsOn))
            {
                Visit(style.DependsOn);
            }
            result.Add(style);
        }

        Visit(styleId);
        return result;
    }

    private static void CollectFontFamilyDeclarations(string css, List<string> declarations)
    {
        foreach (Match block in Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.Multiline))
        {
            var selector = block.Groups["selector"].Value;
            if (!IsBodyFontSelector(selector))
            {
                continue;
            }

            var body = block.Groups["body"].Value;
            var match = Regex.Match(body, @"font-family\s*:\s*(?<value>[^;]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                declarations.Add(match.Groups["value"].Value.Trim());
            }
        }
    }

    private static bool IsBodyFontSelector(string selector)
    {
        var selectors = selector.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return selectors.Any(static part =>
            Regex.IsMatch(part, @"(^|\s)\.markleaf-document\s*(>|$)", RegexOptions.IgnoreCase)
            || Regex.IsMatch(part, @"(^|\s)\.markleaf-document\s+p$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(part, @"(^|\s)\.markleaf-document\s*>\s*p$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(part, @"(^|\s)\.markleaf-document\s+li\s*>\s*p$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(part, @"(^|\s)\.markleaf-document\s+(ul|ol|li|td|th|blockquote|pre)$", RegexOptions.IgnoreCase));
    }

    private static string ResolveExportColorScheme(string? colorScheme)
    {
        if (!string.IsNullOrWhiteSpace(colorScheme)
            && ColorThemeService.All.Any(t => string.Equals(t.Id, colorScheme, StringComparison.Ordinal)))
        {
            return colorScheme;
        }

        return ColorThemeService.ActiveThemeId;
    }

    private async Task<string> GeneratePreviewHtmlAsync(ExportOptions options)
    {
        if (_editorHost is null || _document is null)
        {
            return "";
        }

        var colorThemeCss = ColorThemeService.GetThemeCss(options.ColorScheme);
        return await GeneratePrintHtmlAsync(
            options.Format,
            options.Style,
            options.Format == "image" ? "" : options.HtmlHeader,
            options.Format == "image" ? "" : options.HtmlFooter,
            colorThemeCss,
            options.KeepTablesTogether,
            options.KeepHeadingsWithNextBlock,
            options.Format == "image" ? options.ImageContentWidth : null);
    }

    private async Task<string> GeneratePrintHtmlAsync(
        string format,
        string style,
        string header,
        string footer,
        string colorSchemeCss,
        bool keepTablesTogether,
        bool keepHeadingsWithNextBlock,
        int? maxWidth = null)
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
            maxWidth ?? editor.VisualMaxContentWidth,
            editor.VisualCjkAutoSpacing,
            colorSchemeCss,
            title,
            keepTablesTogether,
            keepHeadingsWithNextBlock);
        if (string.IsNullOrEmpty(html))
        {
            SetStatus(Loc.Get("export.noContent"));
        }
        return EmbedExportImages(html, _document.FilePath);
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

    private void InsertLink(string? initialAddress = null)
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new LinkInputDialog(initialAddress);
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

        _editorHost.ExecuteCommand(command);
        SetStatus(isBlock ? Loc.Get("status.mathBlockInserted") : Loc.Get("status.mathInlineInserted"));
    }

    private async Task InsertFootnote()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new FootnoteInputDialog();
        while (ShowModal(() => dialog.ShowDialog(this)) == DialogResult.OK)
        {
            var payload = JsonSerializer.Serialize(new
            {
                label = dialog.FootnoteLabel,
                note = dialog.FootnoteText,
            });
            var result = await _editorHost.ExecuteCommandResultAsync("insertFootnote", payload);
            if (result.Success)
            {
                SetStatus(Loc.Get("status.footnoteInserted"));
                return;
            }
            ShowMessage(this, Loc.Get("dialog.footnoteInvalidLabel"), Loc.Get("dialog.footnoteDuplicateTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        return;
    }

    private async Task ResetFootnoteLabel()
    {
        if (_editorHost?.IsDocumentLoaded != true
            || string.IsNullOrWhiteSpace(_editorCommandStatus.FootnoteDefinitionLabel))
        {
            return;
        }

        var oldLabel = _editorCommandStatus.FootnoteDefinitionLabel.Trim();
        using var dialog = new TextInputDialog(
            Loc.Get("dialog.footnoteResetTitle"),
            Loc.Get("dialog.footnoteResetLabel"),
            oldLabel);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        var newLabel = dialog.InputText;
        if (string.IsNullOrWhiteSpace(newLabel) || string.Equals(oldLabel, newLabel, StringComparison.Ordinal))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            oldLabel,
            newLabel,
        });
        var result = await _editorHost.ExecuteCommandResultAsync("resetFootnoteLabel", payload);
        if (result.Success) SetStatus(Loc.Get("status.footnoteLabelReset"));
        else ShowMessage(this, Loc.Get("dialog.footnoteInvalidLabel"), Loc.Get("dialog.footnoteResetTitle"),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task GoToFootnoteReferenceAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true
            || string.IsNullOrWhiteSpace(_editorCommandStatus.FootnoteDefinitionLabel))
        {
            return;
        }

        var success = await _editorHost.ExecuteCommandAsync(
            "goToFootnoteReference", _editorCommandStatus.FootnoteDefinitionLabel);
        if (!success)
        {
            ShowMessage(this, Loc.Get("dialog.footnoteReferenceMissing"), Loc.Get("dialog.footnoteResetTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void EditMath()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        _editorHost.ExecuteCommand("editMath");
    }

    private static string EmbedExportImages(string html, string? documentPath)
    {
        return Regex.Replace(
            html,
            "(?<prefix>\\bsrc\\s*=\\s*[\\\"'])(?<src>[^\\\"']+)(?<suffix>[\\\"'])",
            match =>
            {
                var source = WebUtility.HtmlDecode(match.Groups["src"].Value);
                if (Uri.TryCreate(source, UriKind.Absolute, out var virtualUri)
                    && string.Equals(virtualUri.Host, "assets.local", StringComparison.OrdinalIgnoreCase)
                    && virtualUri.AbsolutePath.Equals("/image", StringComparison.OrdinalIgnoreCase))
                {
                    var query = virtualUri.Query;
                    source = query.StartsWith("?path=", StringComparison.Ordinal)
                        ? Uri.UnescapeDataString(query[6..])
                        : source;
                }
                var path = ImageAssetService.ResolveLocalImagePath(source, documentPath);
                if (path is null || !File.Exists(path) || !ImageAssetService.IsSupportedImagePath(path))
                    return match.Value;
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var mime = Path.GetExtension(path).ToLowerInvariant() switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        ".bmp" => "image/bmp",
                        _ => "application/octet-stream",
                    };
                    return match.Groups["prefix"].Value
                        + $"data:{mime};base64,{Convert.ToBase64String(bytes)}"
                        + match.Groups["suffix"].Value;
                }
                catch (IOException)
                {
                    return match.Value;
                }
                catch (UnauthorizedAccessException)
                {
                    return match.Value;
                }
            },
            RegexOptions.IgnoreCase);
    }

    private void SetMathNumber()
    {
        if (_editorHost?.IsDocumentLoaded != true || !_editorCommandStatus.MathBlock)
        {
            return;
        }

        using var dialog = new TextInputDialog(
            Loc.Get("dialog.mathNumberTitle"),
            Loc.Get("dialog.mathNumberLabel"),
            _editorCommandStatus.MathNumber ?? "");
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        if (!MathNumberPolicy.IsValid(dialog.InputText))
        {
            ShowModal(() => MessageBox.Show(this, Loc.Get("dialog.mathNumberLabel"), Loc.Get("dialog.mathNumberTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning));
            return;
        }
        _editorHost.ExecuteCommand("setMathNumber", dialog.InputText.Trim());
    }

    private void InsertMermaid()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        _editorHost.ExecuteCommand("insertMermaid");
        SetStatus(Loc.Get("status.mermaidInserted"));
    }

    private void EditMermaid()
    {
        if (_editorHost?.IsDocumentLoaded != true || !_editorCommandStatus.MermaidSelected)
        {
            return;
        }

        _editorHost.ExecuteCommand("editMermaid");
    }

    private void EditImageCaption()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new CaptionInputDialog(_editorCommandStatus.Caption ?? "");
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand("setImageCaption", dialog.Caption);
        SetStatus(Loc.Get("status.captionUpdated"));
    }

    private void EditTableCaption()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new CaptionInputDialog(_editorCommandStatus.Caption ?? "");
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand("setTableCaption", dialog.Caption);
        SetStatus(Loc.Get("status.captionUpdated"));
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
            case RecoveryChoice.Open when dialog.SelectedSnapshot is not null:
                OpenRecoverySnapshot(dialog.SelectedSnapshot);
                break;
            case RecoveryChoice.Discard:
                foreach (var snapshot in pending)
                {
                    RecoveryService.DeletePending(_paths.RecoveryDirectory, snapshot.DocumentId, _logger);
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

            StopWatchingDocument();
            var opened = await _documentFileService.OpenAsync(targetPath);
            await AddAndActivateDocumentAsync(opened);
            RecoveryService.DeletePending(_paths.RecoveryDirectory, recovery.DocumentId, _logger);
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
    /// 直接打开恢复快照继续编辑（macOS 1.7.7 恢复窗口的“打开”动作）：快照内容以
    /// 脏文档载入；原文件仍存在时沿用磁盘指纹与编码，使首次保存与常规保存一致，
    /// 若磁盘内容在崩溃后被其它程序改动，则照常进入外部变更确认后再覆盖。
    /// </summary>
    private async void OpenRecoverySnapshot(RecoverySnapshot recovery)
    {
        var document = await LoadRecoveryDocumentAsync(recovery);
        document.Markdown = recovery.Markdown;
        document.Revision = recovery.Revision;
        document.IsDirty = true;
        // 同路径标签页已存在时强制新开标签页承载快照内容，
        // 避免按去重规则切换到旧标签页而丢失恢复内容。
        var duplicateExists = document.FilePath is not null
            && _openDocuments.Any(item => item.FilePath is not null
                && PathEquals(item.FilePath, document.FilePath));
        await AddAndActivateDocumentAsync(document, forceNewTab: duplicateExists);
        RecoveryService.DeletePending(_paths.RecoveryDirectory, recovery.DocumentId, _logger);
        _logger.Info($"Recovery snapshot opened for editing: {recovery.DocumentId}.");
        SetStatus(Loc.Get("status.recoveredUnsaved"));
    }

    private async Task<MarkdownDocument> LoadRecoveryDocumentAsync(RecoverySnapshot recovery)
    {
        if (recovery.DocumentPath is not null && File.Exists(recovery.DocumentPath))
        {
            try
            {
                return await _documentFileService.OpenAsync(recovery.DocumentPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning(
                    $"Recovery original file unreadable, falling back to a new document: {exception.Message}");
            }
        }

        var document = _documentFileService.CreateNew(
            DefaultNewLine,
            recovery.DocumentPath is null
                ? NewDocumentKind.Markdown
                : NewDocumentKindExtensions.FromExtension(Path.GetExtension(recovery.DocumentPath)),
            DocumentEncodingPolicy.FromId(_settings.File.DefaultEncoding));
        document.FilePath = recovery.DocumentPath;
        return document;
    }

    private void ShowDocumentStatisticsDialog()
    {
        if (_editorCommandStatus.SourceMode)
        {
            return;
        }

        using var dialog = new Form
        {
            Text = Loc.Get("dialog.documentStatisticsTitle"),
            BackColor = DialogColors.Secondary,
            ForeColor = ColorThemeService.GetActiveColors().TryGetValue("text-primary", out var statisticsTextColor)
                ? statisticsTextColor
                : SystemColors.ControlText,
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(this.ScaleForDpi(320), this.ScaleForDpi(230)),
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(this.ScaleForDpi(18), this.ScaleForDpi(14), this.ScaleForDpi(18), this.ScaleForDpi(12)),
            ColumnCount = 2,
            RowCount = 9,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        AddStatisticRow(grid, 0, Loc.Get("dialog.statistics.words"), _editorStatus.TotalCharacterCount);
        AddStatisticRow(grid, 1, Loc.Get("dialog.statistics.nonWhitespace"), _editorStatus.NonWhitespaceCharacterCount);
        AddStatisticRow(grid, 2, Loc.Get("dialog.statistics.cjk"), _editorStatus.CjkCharacterCount);
        AddStatisticRow(grid, 3, Loc.Get("dialog.statistics.westernWords"), _editorStatus.WesternWordCount);
        AddStatisticRow(grid, 4, Loc.Get("dialog.statistics.formulas"), _editorStatus.FormulaCount);
        AddStatisticRow(grid, 5, Loc.Get("dialog.statistics.codeLines"), _editorStatus.CodeLineCount);
        AddStatisticRow(grid, 6, Loc.Get("dialog.statistics.paragraphs"), _editorStatus.ParagraphCount);

        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var closeButton = new Button
        {
            Text = Loc.Get("common.close"),
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Right,
            Width = this.ScaleForDpi(82),
            Height = this.ScaleForDpi(28),
            FlatStyle = FlatStyle.System,
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(36)));
        grid.Controls.Add(closeButton, 1, 8);

        dialog.Controls.Add(grid);
        dialog.AcceptButton = closeButton;
        dialog.CancelButton = closeButton;
        ShowModal(() => dialog.ShowDialog(this));
    }

    private static void AddStatisticRow(TableLayoutPanel grid, int row, string label, int value)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, row);
        grid.Controls.Add(new Label
        {
            Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
        }, 1, row);
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
