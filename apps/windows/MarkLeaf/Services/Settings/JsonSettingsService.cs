using System.Text.Json;
using MarkLeaf.Services.Logging;

namespace MarkLeaf.Services.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _settingsFile;
    private readonly IAppLogger _logger;
    private AppSettings? _loadBaseline;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public JsonSettingsService(string settingsFile, IAppLogger logger)
    {
        _settingsFile = settingsFile;
        _logger = logger;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFile))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = new FileStream(
                _settingsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            var loaded = settings?.SchemaVersion switch
            {
                AppSettings.CurrentSchemaVersion => NormalizeCurrent(settings),
                2 => MigrateVersion2(settings),
                1 => MigrateVersion1(settings),
                _ => new AppSettings(),
            };
            _loadBaseline = CloneSettings(loaded);
            return loaded;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.Warning($"Settings could not be loaded: {exception.GetType().Name}.");
            return new AppSettings();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        SettingsSaveScope scope = SettingsSaveScope.Full,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_settingsFile)
            ?? throw new InvalidOperationException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        using var saveLock = await AcquireSaveLockAsync(cancellationToken).ConfigureAwait(false);

        var baselineAtSaveStart = _loadBaseline;
        var latest = await ReadSettingsForSaveAsync(cancellationToken).ConfigureAwait(false);
        var baseline = baselineAtSaveStart ?? latest;
        var merged = SettingsSaveScope.Window == scope
            ? MergeWindowScopedSave(latest, settings, baseline)
            : SettingsSaveScope.Shared == scope
            ? MergeSharedScopedSave(latest, settings, baseline)
            : SettingsSaveScope.ClearRecentHistory == scope
            ? MergeClearRecentHistory(latest)
            : MergeRecentItems(latest, settings, baseline);
        if (SettingsSaveScope.Full == scope)
        {
            MergeSharedSections(latest, merged, baseline);
        }
        _loadBaseline = CloneSettings(merged);
        settings = merged;

        var temporaryFile = _settingsFile + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var stream = new FileStream(
                temporaryFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await JsonSerializer
                .SerializeAsync(stream, settings, _jsonOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            stream.Close();
            File.Move(temporaryFile, _settingsFile, true);
        }
        finally
        {
            if (File.Exists(temporaryFile))
            {
                File.Delete(temporaryFile);
            }
        }
    }

    private AppSettings MergeWindowScopedSave(AppSettings latest, AppSettings incoming, AppSettings baseline)
    {
        var merged = CloneSettings(incoming);
        merged.File = latest.File;
        merged.Editor = latest.Editor;
        merged.Appearance = latest.Appearance;
        merged.General = latest.General;
        merged.Image = latest.Image;
        merged.Export = latest.Export;
        merged.Shortcut = latest.Shortcut;
        merged.CodeFormatter = latest.CodeFormatter;
        merged.MarkdownStyle = latest.MarkdownStyle;
        merged.ColorTheme = latest.ColorTheme;
        merged.MainWindow = latest.MainWindow;
        merged.Workspace.LastFolder = latest.Workspace.LastFolder;
        merged.Workspace.LastFile = latest.Workspace.LastFile;
        merged.Workspace.LastFileReadOnly = latest.Workspace.LastFileReadOnly;
        merged.Workspace.OpenDocuments = latest.Workspace.OpenDocuments;
        merged.Workspace.ActiveDocumentIndex = latest.Workspace.ActiveDocumentIndex;
        return MergeRecentItems(latest, merged, baseline);
    }

    private AppSettings MergeSharedScopedSave(AppSettings latest, AppSettings incoming, AppSettings baseline)
    {
        var merged = CloneSettings(incoming);
        MergeSharedSections(latest, merged, baseline);
        merged.MainWindow = latest.MainWindow;
        merged.Workspace.LastFolder = latest.Workspace.LastFolder;
        merged.Workspace.LastFile = latest.Workspace.LastFile;
        merged.Workspace.LastFileReadOnly = latest.Workspace.LastFileReadOnly;
        merged.Workspace.OpenDocuments = latest.Workspace.OpenDocuments;
        merged.Workspace.ActiveDocumentIndex = latest.Workspace.ActiveDocumentIndex;
        return MergeRecentItems(latest, merged, baseline);
    }

    private AppSettings MergeClearRecentHistory(AppSettings latest)
    {
        var merged = CloneSettings(latest);
        merged.Workspace.RecentFiles.Clear();
        merged.Workspace.RecentFolders.Clear();
        return merged;
    }

    private void MergeSharedSections(AppSettings latest, AppSettings incoming, AppSettings baseline)
    {
        incoming.File = ChooseSharedSection(baseline.File, incoming.File, latest.File);
        incoming.Editor = ChooseSharedSection(baseline.Editor, incoming.Editor, latest.Editor);
        incoming.Appearance = ChooseSharedSection(baseline.Appearance, incoming.Appearance, latest.Appearance);
        incoming.General = ChooseSharedSection(baseline.General, incoming.General, latest.General);
        incoming.Image = ChooseSharedSection(baseline.Image, incoming.Image, latest.Image);
        incoming.Export = ChooseSharedSection(baseline.Export, incoming.Export, latest.Export);
        incoming.Shortcut = ChooseSharedSection(baseline.Shortcut, incoming.Shortcut, latest.Shortcut);
        incoming.CodeFormatter = ChooseSharedSection(baseline.CodeFormatter, incoming.CodeFormatter, latest.CodeFormatter);
        incoming.MarkdownStyle = ChooseSharedSection(baseline.MarkdownStyle, incoming.MarkdownStyle, latest.MarkdownStyle);
        incoming.ColorTheme = ChooseSharedSection(baseline.ColorTheme, incoming.ColorTheme, latest.ColorTheme);
    }

    private static T ChooseSharedSection<T>(T baseline, T incoming, T latest) where T : class
    {
        var localChanged = !JsonEquals(baseline, incoming);
        return localChanged ? incoming : latest;
    }

    private static bool JsonEquals<T>(T? left, T? right) where T : class
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return JsonSerializer.Serialize(left, SectionJsonOptions)
            == JsonSerializer.Serialize(right, SectionJsonOptions);
    }

    private async Task<AppSettings> ReadSettingsForSaveAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsFile))
        {
            return new AppSettings();
        }

        await using var stream = new FileStream(
            _settingsFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var settings = await JsonSerializer
            .DeserializeAsync<AppSettings>(stream, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return settings?.SchemaVersion switch
        {
            AppSettings.CurrentSchemaVersion => NormalizeCurrent(settings),
            2 => MigrateVersion2(settings),
            1 => MigrateVersion1(settings),
            _ => new AppSettings(),
        };
    }

    private async Task<FileStream> AcquireSaveLockAsync(CancellationToken cancellationToken)
    {
        var lockPath = _settingsFile + ".lock";
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("Timed out waiting for the settings save lock.");
    }

    private AppSettings MergeRecentItems(AppSettings latest, AppSettings incoming, AppSettings baseline)
    {
        var deletedFiles = DeletedPaths(baseline.Workspace.RecentFiles, incoming.Workspace.RecentFiles);
        var deletedFolders = DeletedPaths(baseline.Workspace.RecentFolders, incoming.Workspace.RecentFolders);
        var files = latest.Workspace.RecentFiles
            .Where(path => !deletedFiles.Contains(NormalizePath(path)))
            .Concat(incoming.Workspace.RecentFiles)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        files = incoming.Workspace.RecentFiles
            .Concat(files)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var folders = latest.Workspace.RecentFolders
            .Where(path => !deletedFolders.Contains(NormalizePath(path)))
            .Concat(incoming.Workspace.RecentFolders)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        folders = incoming.Workspace.RecentFolders
            .Concat(folders)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = CloneSettings(incoming);
        merged.Workspace.RecentFiles = files;
        merged.Workspace.RecentFolders = folders;
        return merged;
    }

    private static HashSet<string> DeletedPaths(IReadOnlyList<string> baseline, IReadOnlyList<string> incoming) =>
        baseline
            .Where(path => !incoming.Contains(path, StringComparer.OrdinalIgnoreCase))
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static AppSettings CloneSettings(AppSettings settings)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, settings, options);
        stream.Position = 0;
        return JsonSerializer.Deserialize<AppSettings>(stream, options) ?? new AppSettings();
    }

    private static readonly JsonSerializerOptions SectionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static AppSettings MigrateVersion1(AppSettings settings)
    {
        var window = settings.MainWindow ?? new WindowSettings();
        window.Width = WindowPlacementCalculator.ToLogicalPixels(window.Width, window.Dpi);
        window.Height = WindowPlacementCalculator.ToLogicalPixels(window.Height, window.Dpi);
        window.WorkspaceWidth = WindowPlacementCalculator.ToLogicalPixels(window.WorkspaceWidth, window.Dpi);
        window.OutlineWidth = WindowPlacementCalculator.ToLogicalPixels(window.OutlineWidth, window.Dpi);
        settings.MainWindow = window;
        settings.Workspace ??= new WorkspaceSettings();
        settings.Image ??= new ImageSettings();
        NormalizeImage(settings.Image);
        settings.Export ??= new ExportSettings();
        NormalizeExport(settings.Export);
        settings.Shortcut ??= new ShortcutSettings();
        NormalizeShortcuts(settings.Shortcut);
        settings.CodeFormatter ??= new CodeFormatterSettings();
        NormalizeCodeFormatter(settings.CodeFormatter);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }

    private static AppSettings MigrateVersion2(AppSettings settings)
    {
        settings.Workspace ??= new WorkspaceSettings();
        settings.Image ??= new ImageSettings();
        NormalizeImage(settings.Image);
        settings.Export ??= new ExportSettings();
        NormalizeExport(settings.Export);
        settings.Shortcut ??= new ShortcutSettings();
        NormalizeShortcuts(settings.Shortcut);
        settings.CodeFormatter ??= new CodeFormatterSettings();
        NormalizeCodeFormatter(settings.CodeFormatter);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }

    private static AppSettings NormalizeCurrent(AppSettings settings)
    {
        settings.MainWindow ??= new WindowSettings();
        settings.Workspace ??= new WorkspaceSettings();
        settings.Workspace.RecentFolders ??= [];
        settings.Workspace.RecentFiles ??= [];
        settings.Workspace.OpenDocuments ??= [];
        settings.File ??= new FileSettings();
        settings.Editor ??= new EditorSettings();
        NormalizeEditor(settings.Editor);
        settings.Appearance ??= new AppearanceSettings();
        if (!Enum.IsDefined(settings.Appearance.TitleBarTextStyle))
            settings.Appearance.TitleBarTextStyle = TitleBarTextStyle.FileNameAndAppName;
        settings.Appearance.StatusBar ??= new StatusBarSettings();
        NormalizeStatusBar(settings.Appearance.StatusBar);
        settings.General ??= new GeneralSettings();
        settings.Image ??= new ImageSettings();
        NormalizeImage(settings.Image);
        settings.Export ??= new ExportSettings();
        NormalizeExport(settings.Export);
        settings.Shortcut ??= new ShortcutSettings();
        settings.Shortcut.Overrides ??= [];
        settings.Shortcut.Cleared ??= [];
        NormalizeShortcuts(settings.Shortcut);
        settings.CodeFormatter ??= new CodeFormatterSettings();
        NormalizeCodeFormatter(settings.CodeFormatter);
        return settings;
    }

    private static void NormalizeCodeFormatter(CodeFormatterSettings codeFormatter)
    {
        codeFormatter.ToolPaths ??= [];
        if (!SqlFluffDialects.Contains(codeFormatter.SqlDialect, StringComparer.Ordinal))
        {
            codeFormatter.SqlDialect = "ansi";
        }
    }

    private static readonly string[] SqlFluffDialects =
    [
        "ansi", "bigquery", "clickhouse", "duckdb", "hive", "materialize", "mysql",
        "oracle", "postgres", "redshift", "snowflake", "sparksql", "sqlite", "trino", "tsql",
    ];

    private static void NormalizeShortcuts(ShortcutSettings shortcuts)
    {
        shortcuts.Overrides ??= [];
        shortcuts.Cleared ??= [];
        if (shortcuts.Overrides.TryGetValue(nameof(Commands.AppCommand.ToggleFocusMode), out var shortcut)
            && string.Equals(shortcut, "F11", StringComparison.OrdinalIgnoreCase))
        {
            shortcuts.Overrides[nameof(Commands.AppCommand.ToggleFocusMode)] = "Shift+F11";
        }
    }

    private static void NormalizeImage(ImageSettings image)
    {
        if (!Enum.IsDefined(image.ClipboardHandling))
            image.ClipboardHandling = ClipboardImageHandling.SaveToDefaultDirectory;
        if (!Enum.IsDefined(image.FileHandling))
            image.FileHandling = FileImageHandling.ReferenceOriginal;
    }

    private static void NormalizeEditor(EditorSettings editor)
    {
        editor.MarkdownCodeFence = editor.MarkdownCodeFence is "backtick" or "tilde"
            ? editor.MarkdownCodeFence
            : "backtick";
        editor.MarkdownEmphasisMarker = editor.MarkdownEmphasisMarker is "asterisk" or "underscore"
            ? editor.MarkdownEmphasisMarker
            : "asterisk";
        editor.MarkdownBulletMarker = editor.MarkdownBulletMarker is "dash" or "asterisk" or "plus"
            ? editor.MarkdownBulletMarker
            : "dash";
        if (editor.UnsafeEmphasisPreference is not ("literal" or "html"))
            editor.UnsafeEmphasisPreference = null;
    }

    private static void NormalizeExport(ExportSettings export)
    {
        export.Format = NormalizeExportFormat(export.Format);
        export.ImageMaxHeight = Math.Clamp(export.ImageMaxHeight, 1000, 30000);
        export.ImageContentWidth = Math.Clamp(export.ImageContentWidth, 320, 4000);
        export.ImageScale = float.IsFinite(export.ImageScale)
            ? Math.Clamp(export.ImageScale, 1f, 4f)
            : 2f;
        export.ImageFormat = string.Equals(export.ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(export.ImageFormat, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : "png";
        export.ImageJpegQuality = Math.Clamp(export.ImageJpegQuality, 1, 100);
        export.PaperSize = string.IsNullOrWhiteSpace(export.PaperSize) ? "A4" : export.PaperSize;
        export.HtmlHeader ??= "";
        export.HtmlFooter ??= "";
        export.PdfHeaderPreset = NormalizeHeaderFooterPreset(export.PdfHeaderPreset);
        export.PdfFooterPreset = NormalizeHeaderFooterPreset(export.PdfFooterPreset);
        export.PdfHeaderCustom ??= "";
        export.PdfFooterCustom ??= "";
        export.PdfHeaderText ??= "";
        export.PdfHeaderAlignment ??= "";
        export.PdfFooterText ??= "";
        export.PdfFooterAlignment ??= "";
        export.Style = string.IsNullOrWhiteSpace(export.Style) ? "serif" : export.Style;
        export.ColorScheme ??= "";
    }

    private static string NormalizeExportFormat(string? format) =>
        string.Equals(format, "html", StringComparison.OrdinalIgnoreCase) ? "html"
        : string.Equals(format, "image", StringComparison.OrdinalIgnoreCase) ? "image"
        : "pdf";

    private static string NormalizeHeaderFooterPreset(string? preset) =>
        preset is "title-left" or "page-center" or "page-right" or "page-total-center" or "custom"
            ? preset
            : "none";

    private static void NormalizeStatusBar(StatusBarSettings statusBar)
    {
        if (!statusBar.CommandStatusVisible)
        {
            statusBar.CommandDisplayMode = StatusBarCommandDisplayMode.Hidden;
            statusBar.CommandStatusVisible = true;
        }
    }
}
