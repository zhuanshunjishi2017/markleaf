using System.Text.Json;
using System.Text.RegularExpressions;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Services;
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
            ResetAllSettingsToDefaults,
            ApplyStatusBarSettingsFromPreferences);
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
        _editorHost?.ApplyCssVariables(editor.VisualLineHeight, editor.VisualFontSize, editor.VisualMaxContentWidth, editor.SourceFontSize, editor.SourceFontFamily, editor.SourceCjkFontFamily, editor.CjkLanguageTag.ToBcp47(), editor.VisualCjkAutoSpacing);
        _editorHost?.ApplySourceSettings(editor.SourceIndentWidth);
        ApplyCodeHighlightVisibility();
        ApplyBlockHandleVisibility();

        SetMarkdownStyle(_settings.MarkdownStyle);
        ApplyEffectiveColorTheme();
        SetZoomPercent(_settings.Appearance.ZoomPercent);
        TopMost = _settings.Appearance.TopMostWindow;
        _editorHost?.ApplyAutoHideScrollbar(_settings.Appearance.AutoHideScrollbars);
        ApplySidebarAutoHideScrollbar();
        RefreshPersistentStatusBar();

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
        using var dialog = new ShortcutDialog(_shortcutManager);
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

    private async void ShowWelcome()
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

        var cachePath = Path.Combine(_paths.DefaultImageDirectory, "welcome.md");
        try
        {
            File.Copy(welcomePath, cachePath, overwrite: true);
        }
        catch
        {
            SetStatus(Loc.Get("welcome.openFailed"));
            return;
        }

        await OpenDocumentPathAsync(cachePath);
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
            _paths.WebView2UserDataDirectory, GeneratePreviewPdfAsync, GeneratePreviewHtmlAsync,
            ExportDialogMode.Pdf, _settings.Export);
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
            // 预览 PDF 已复制到目标路径，无需重新生成。
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
        using var dialog = new ExportDialog(
            docName, defaultName, _markdownStyle, StyleService.GetAllStyles(),
            _paths.WebView2UserDataDirectory, GeneratePreviewPdfAsync, GeneratePreviewHtmlAsync,
            ExportDialogMode.Html, _settings.Export);
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
            var colorThemeCss = ColorThemeService.GetThemeCss(options.ColorScheme);
            var html = await _editorHost.RequestExportAsync(
                options.Format,
                options.Style,
                options.HtmlHeader,
                options.HtmlFooter,
                editor.VisualFontSize,
                editor.VisualLineHeight,
                editor.VisualMaxContentWidth,
                editor.VisualCjkAutoSpacing,
                colorThemeCss,
                defaultName,
                options.KeepTablesTogether);

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
                    options.MarginRight,
                    ResolvePdfHeaderFooterPlaceholders(options.PdfHeaderText, defaultName),
                    options.PdfHeaderAlignment,
                    ResolvePdfHeaderFooterPlaceholders(options.PdfFooterText, defaultName),
                    options.PdfFooterAlignment,
                    ResolveHeaderFooterFontFamily(options.Style));
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
        var html = await GeneratePrintHtmlAsync(options.Format, options.Style, options.HtmlHeader, options.HtmlFooter, colorThemeCss, options.KeepTablesTogether);
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
        var extension = isPdf ? "pdf" : "html";
        var filter = isPdf ? $"{Loc.Get("export.pdf")}|*.pdf" : $"{Loc.Get("export.html")}|*.html";
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
        string.Equals(format, "html", StringComparison.OrdinalIgnoreCase) ? "html" : "pdf";

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
        return await GeneratePrintHtmlAsync(options.Format, options.Style, options.HtmlHeader, options.HtmlFooter, colorThemeCss, options.KeepTablesTogether);
    }

    private async Task<string> GeneratePrintHtmlAsync(
        string format,
        string style,
        string header,
        string footer,
        string colorSchemeCss,
        bool keepTablesTogether)
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
            editor.VisualCjkAutoSpacing,
            colorSchemeCss,
            title,
            keepTablesTogether);
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

    private async void InsertFootnote()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new FootnoteInputDialog();
        while (ShowModal(() => dialog.ShowDialog(this)) == DialogResult.OK)
        {
            if (await FootnoteLabelExistsAsync(dialog.FootnoteLabel))
            {
                var duplicateChoice = ShowMessage(
                    this,
                    Loc.Format("dialog.footnoteDuplicateMessage", dialog.FootnoteLabel),
                    Loc.Get("dialog.footnoteDuplicateTitle"),
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (duplicateChoice != DialogResult.OK)
                {
                    continue;
                }
            }

            var payload = JsonSerializer.Serialize(new
            {
                label = dialog.FootnoteLabel,
                note = dialog.FootnoteText,
            });
            _editorHost.ExecuteCommand("insertFootnote", payload);
            SetStatus(Loc.Get("status.footnoteInserted"));
            return;
        }

        return;
    }

    private async Task<bool> FootnoteLabelExistsAsync(string label)
    {
        if (_editorHost is null || string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        try
        {
            var snapshot = await _editorHost.RequestSnapshotAsync(TimeSpan.FromSeconds(3));
            return FootnoteLabelExists(snapshot.Markdown, label);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            _logger.Warning($"Footnote duplicate check skipped: {exception.Message}");
            return false;
        }
    }

    private static bool FootnoteLabelExists(string markdown, string label)
    {
        foreach (Match match in Regex.Matches(markdown, @"(?m)^ {0,3}\[\^([^\]\r\n]+)\]:"))
        {
            if (string.Equals(match.Groups[1].Value.Trim(), label.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ResetFootnoteLabel()
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
        _editorHost.ExecuteCommand("resetFootnoteLabel", payload);
        SetStatus(Loc.Get("status.footnoteLabelReset"));
    }

    private void EditMath()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        var isBlock = _editorCommandStatus.MathBlock;
        using var dialog = new MathInputDialog(isBlock, _editorCommandStatus.MathLatex ?? "", _editorCommandStatus.MathNumber ?? "", showNumber: isBlock);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dialog.Latex))
        {
            return;
        }

        _editorHost.ExecuteCommand("updateMath", dialog.Latex);
        if (isBlock)
        {
            _editorHost.ExecuteCommand("setMathNumber", dialog.Number);
        }
        SetStatus(Loc.Get("status.mathUpdated"));
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

    private void ShowDocumentStatisticsDialog()
    {
        if (_editorCommandStatus.SourceMode)
        {
            return;
        }

        using var dialog = new Form
        {
            Text = Loc.Get("dialog.documentStatisticsTitle"),
            AutoScaleMode = AutoScaleMode.Dpi,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(this.ScaleForDpi(320), this.ScaleForDpi(280)),
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
