namespace MarkLeaf.Services.Settings;

public enum SettingsSaveScope
{
    Full,
    Shared,
    Window,
    ClearRecentHistory,
}

public static class SettingsReset
{
    /// <summary>
    /// Resets only groups owned by PreferencesDialog. CodeFormatter is owned by
    /// the external code formatter manager.
    /// </summary>
    public static void Apply(AppSettings target, AppSettings defaults)
    {
        target.SchemaVersion = defaults.SchemaVersion;
        target.File = defaults.File;
        target.Editor = defaults.Editor;
        target.Appearance = defaults.Appearance;
        target.General = defaults.General;
        target.Image = defaults.Image;
        target.Export = defaults.Export;
        target.Shortcut.ApplyFrom(defaults.Shortcut);
        target.MarkdownStyle = defaults.MarkdownStyle;
        target.ColorTheme = defaults.ColorTheme;
    }
}

public static class CodeFormatterSettingsReset
{
    public static void Apply(CodeFormatterSettings target, CodeFormatterSettings defaults)
    {
        target.ApplyFrom(defaults);
    }
}

public enum StartupAction
{
    NewDocument,
    OpenLastWorkspace,
    OpenLastWorkspaceAndFiles,
}

public enum NewLineStyle
{
    Lf,
    Crlf,
}

public enum ClipboardImageHandling
{
    SaveToDefaultDirectory,
    CopyToAssets,
}

public enum FileImageHandling
{
    ReferenceOriginal,
    CopyToAssets,
}

public enum MenuBarStyle
{
    DarkThemeOnly,
    Always,
    System,
    TabBar,
}

public enum TitleBarTextStyle
{
    FileNameAndAppName,
    AppName,
    FileName,
}

public enum CjkLanguageTag
{
    SimplifiedChinese,
    TraditionalChinese,
    Japanese,
    Korean,
}

public enum StatusBarCommandDisplayMode
{
    Always,
    Temporary,
    Hidden,
}

public static class CjkLanguageTagExtensions
{
    public static string ToBcp47(this CjkLanguageTag tag) => tag switch
    {
        CjkLanguageTag.SimplifiedChinese => "zh-Hans",
        CjkLanguageTag.TraditionalChinese => "zh-Hant",
        CjkLanguageTag.Japanese => "ja",
        CjkLanguageTag.Korean => "ko",
        _ => "zh-Hans",
    };
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public WindowSettings MainWindow { get; set; } = new();

    public WorkspaceSettings Workspace { get; set; } = new();

    public FileSettings File { get; set; } = new();

    public EditorSettings Editor { get; set; } = new();

    public AppearanceSettings Appearance { get; set; } = new();

    public GeneralSettings General { get; set; } = new();

    public ImageSettings Image { get; set; } = new();

    public ExportSettings Export { get; set; } = new();

    public ShortcutSettings Shortcut { get; set; } = new();

    public CodeFormatterSettings CodeFormatter { get; set; } = new();

    /// <summary>已自动展示过「更新内容」的应用版本；每个版本只在首次启动时展示一次。</summary>
    public string? LastChangelogShownVersion { get; set; }

    public string MarkdownStyle { get; set; } = "serif";

    public string ColorTheme { get; set; } = "apple-blue";

    // Refresh shared ownership after a successful merged save, retaining live
    // manager references and the current window's view/restore state.
    internal void ApplyPersistedSharedSettings(AppSettings saved)
    {
        var windowZoom = Appearance.ZoomPercent;
        File = saved.File;
        Editor = saved.Editor;
        Appearance = saved.Appearance;
        Appearance.ZoomPercent = windowZoom;
        General = saved.General;
        Image = saved.Image;
        Export = saved.Export;
        Shortcut.ApplyFrom(saved.Shortcut);
        CodeFormatter.ApplyFrom(saved.CodeFormatter);
        LastChangelogShownVersion = saved.LastChangelogShownVersion;
        MarkdownStyle = saved.MarkdownStyle;
        ColorTheme = saved.ColorTheme;
    }

    public static AppSettings CreateDefaults()
    {
        return new AppSettings
        {
            SchemaVersion = CurrentSchemaVersion,
            MainWindow = new WindowSettings(),
            Workspace = new WorkspaceSettings(),
            File = new FileSettings(),
            Editor = new EditorSettings(),
            Appearance = new AppearanceSettings(),
            General = new GeneralSettings(),
            Image = new ImageSettings(),
            Export = new ExportSettings(),
            Shortcut = new ShortcutSettings(),
            MarkdownStyle = "sans-serif",
            ColorTheme = "apple-blue",
        };
    }
}

/// <summary>
/// 自定义快捷键（对应 macOS ShortcutSettings）：
/// Overrides 键为 AppCommand 名，值为 ShortcutTextFormatter 的规范字符串；
/// Cleared 表示“已清除（无快捷键）”的命令。
/// </summary>
public sealed class ShortcutSettings
{
    public Dictionary<string, string> Overrides { get; set; } = [];

    public List<string> Cleared { get; set; } = [];

    public void ApplyFrom(ShortcutSettings source)
    {
        Overrides.Clear();
        Cleared.Clear();

        foreach (var (name, shortcut) in source.Overrides)
        {
            Overrides[name] = shortcut;
        }

        Cleared.AddRange(source.Cleared);
    }
}

public sealed class ExportSettings
{
    public int ImageMaxHeight { get; set; } = 6000;

    public int ImageContentWidth { get; set; } = 1200;

    public float ImageScale { get; set; } = 2f;

    public string ImageFormat { get; set; } = "png";

    public int ImageJpegQuality { get; set; } = 90;

    public bool KeepTablesTogether { get; set; }

    public bool KeepHeadingsWithNextBlock { get; set; }

    public string Format { get; set; } = "pdf";

    public string PaperSize { get; set; } = "A4";

    public bool Landscape { get; set; }

    public float MarginTop { get; set; } = 25.4f;

    public float MarginBottom { get; set; } = 25.4f;

    public float MarginLeft { get; set; } = 31.7f;

    public float MarginRight { get; set; } = 31.7f;

    public string HtmlHeader { get; set; } = "";

    public string HtmlFooter { get; set; } = "";

    public string PdfHeaderPreset { get; set; } = "none";

    public string PdfFooterPreset { get; set; } = "none";

    public string PdfHeaderCustom { get; set; } = "";

    public string PdfFooterCustom { get; set; } = "";

    public string PdfHeaderText { get; set; } = "";

    public string PdfHeaderAlignment { get; set; } = "";

    public string PdfFooterText { get; set; } = "";

    public string PdfFooterAlignment { get; set; } = "";

    public string Style { get; set; } = "serif";

    public string ColorScheme { get; set; } = "";
}

public sealed class GeneralSettings
{
    public bool AssociateMarkdownFiles { get; set; } =
        FileAssociationService.IsMachineAssociated(".md")
        || FileAssociationService.IsMachineAssociated(".markdown");

    public bool AssociateTextFiles { get; set; } =
        FileAssociationService.IsMachineAssociated(".txt");

    public string UiLanguage { get; set; } = "";

    public bool AutoCheckForUpdates { get; set; } = true;

    /// <summary>用户选择跳过的版本（VersionText）。仅作用于启动时的自动检查；
    /// 出现更高版本或手动「检查更新」时仍会提示。</summary>
    public string? SkippedUpdateVersion { get; set; }
}

public sealed class AppearanceSettings
{
    public static readonly int[] ZoomPercentOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200];

    public int ZoomPercent { get; set; } = 100;

    public bool RestoreZoomOnOpen { get; set; } = true;

    public bool CtrlWheelZoom { get; set; } = true;

    public bool TopMostWindow { get; set; }

    public bool UseThemeTitleBarColor { get; set; }

    public TitleBarTextStyle TitleBarTextStyle { get; set; } = TitleBarTextStyle.FileNameAndAppName;

    public bool AutoHideScrollbars { get; set; } = true;

    public bool ShowCodeHighlight { get; set; } = true;

    public bool FollowSystemColorMode { get; set; }

    public string DefaultLightThemeId { get; set; } = "apple-blue";

    public string DefaultDarkThemeId { get; set; } = "apple-dark";

    public MenuBarStyle MenuBarStyle { get; set; } = MenuBarStyle.TabBar;

    public bool ShowMenuKeyboardShortcuts { get; set; } = true;

    public bool ShowMenuMnemonics { get; set; } = true;

    public StatusBarSettings StatusBar { get; set; } = new();
}

public sealed class StatusBarSettings
{
    public bool SidebarToggleVisible { get; set; } = true;

    public bool CommandStatusVisible { get; set; } = true;

    public StatusBarCommandDisplayMode CommandDisplayMode { get; set; } = StatusBarCommandDisplayMode.Temporary;

    public bool WordCountVisible { get; set; } = true;

    public bool BlockTypeVisible { get; set; } = false;

    public bool PositionVisible { get; set; } = true;

    public bool EncodingVisible { get; set; } = true;

    public bool NewLineVisible { get; set; } = true;

    public bool ModeToggleVisible { get; set; } = true;

    public bool ZoomVisible { get; set; } = true;

    public StatusBarSettings Clone() => new()
    {
        SidebarToggleVisible = SidebarToggleVisible,
        CommandStatusVisible = CommandStatusVisible,
        CommandDisplayMode = CommandDisplayMode,
        WordCountVisible = WordCountVisible,
        BlockTypeVisible = BlockTypeVisible,
        PositionVisible = PositionVisible,
        EncodingVisible = EncodingVisible,
        NewLineVisible = NewLineVisible,
        ModeToggleVisible = ModeToggleVisible,
        ZoomVisible = ZoomVisible,
    };
}

public sealed class EditorSettings
{
    public float VisualLineHeight { get; set; } = 1.75f;

    public int VisualFontSize { get; set; } = 16;

    public int VisualMaxContentWidth { get; set; } = 820;

    public bool VisualIgnoreMaxContentWidth { get; set; }

    public int SourceFontSize { get; set; } = 14;

    public string SourceFontFamily { get; set; } = "Cascadia Mono";

    public string SourceCjkFontFamily { get; set; } = "Microsoft YaHei";

    public CjkLanguageTag CjkLanguageTag { get; set; } = CjkLanguageTag.SimplifiedChinese;

    public bool VisualCjkAutoSpacing { get; set; } = true;
    public bool AllowTableColumnResize { get; set; } = true;
    public bool VisualAdaptiveJustification { get; set; } = true;

    public bool ExitBlockOnEmptyEnter { get; set; }

    public bool UseShiftEnterHardBreak { get; set; } = true;

    public bool AutoConvertUnsafeEmphasis { get; set; }

    public bool EscapeLiteralSymbols { get; set; }

    public bool EscapeMarkdownLiteralSymbols { get; set; }

    public string MarkdownCodeFence { get; set; } = "backtick";

    public string MarkdownEmphasisMarker { get; set; } = "asterisk";

    public string MarkdownBulletMarker { get; set; } = "dash";

    /// <summary>代码块拼写检查，默认关闭，与 macOS 1.7.7 行为一致。</summary>
    public bool CodeBlockSpellcheck { get; set; }

    public int SourceIndentWidth { get; set; } = 2;

    public bool ShowParagraphBlockHandle { get; set; } = true;

    /// <summary>Show common formatting as text menu items instead of the quick-action row.</summary>
    public bool InlineCommonActions { get; set; }

    public string? UnsafeEmphasisPreference { get; set; }
}

/// <summary>
/// 外部代码格式化器设置。ToolPaths 为工具 id → 可执行文件/jar 自定义路径；
/// 与 macOS 的 codeFormatterPaths 键空间一致。SqlDialect 仅作用于 sqlfluff。
/// </summary>
public sealed class CodeFormatterSettings
{
    public Dictionary<string, string> ToolPaths { get; set; } = [];

    public string SqlDialect { get; set; } = "ansi";

    public void ApplyFrom(CodeFormatterSettings source)
    {
        ToolPaths.Clear();
        foreach (var (toolId, path) in source.ToolPaths)
        {
            ToolPaths[toolId] = path;
        }

        SqlDialect = source.SqlDialect;
    }
}

public sealed class FileSettings
{
    public StartupAction StartupAction { get; set; } = StartupAction.OpenLastWorkspaceAndFiles;

    public bool AutoSaveEnabled { get; set; }

    public int SnapshotIntervalSeconds { get; set; } = 30;

    public bool RecordRecentFiles { get; set; } = true;

    public bool RecordRecentFolders { get; set; } = true;

    public string DefaultEncoding { get; set; } = "utf-8";

    public NewLineStyle NewLineStyle { get; set; } = NewLineStyle.Crlf;
}

public sealed class ImageSettings
{
    public ClipboardImageHandling ClipboardHandling { get; set; } = ClipboardImageHandling.CopyToAssets;

    public FileImageHandling FileHandling { get; set; } = FileImageHandling.CopyToAssets;

    public string DefaultDirectory { get; set; } = string.Empty;

    public bool UseRelativePaths { get; set; } = true;

    public bool PrefixRelativeWithDotSlash { get; set; } = true;
}

public sealed class WorkspaceSettings
{
    public string? LastFolder { get; set; }

    public string? LastFile { get; set; }

    public bool LastFileReadOnly { get; set; }

    public List<OpenDocumentSetting> OpenDocuments { get; set; } = [];

    public int ActiveDocumentIndex { get; set; } = -1;

    public List<string> RecentFolders { get; set; } = [];

    public List<string> RecentFiles { get; set; } = [];
}

public sealed class OpenDocumentSetting
{
    public string Path { get; set; } = string.Empty;

    public bool ReadOnly { get; set; }
}

public sealed class WindowSettings
{
    public int Left { get; set; } = 120;

    public int Top { get; set; } = 80;

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 800;

    public int Dpi { get; set; } = 96;

    public bool IsMaximized { get; set; }

    public int WorkspaceWidth { get; set; } = 220;

    public int OutlineWidth { get; set; } = 220;

    public bool OutlineDetached { get; set; }

    public bool SidebarCollapsed { get; set; }

    public bool SidebarActiveOutline { get; set; }
}
