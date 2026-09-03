namespace MarkLeaf.Services.Settings;

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

    public string MarkdownStyle { get; set; } = "serif";

    public string ColorTheme { get; set; } = "white";

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
            MarkdownStyle = "serif",
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
}

public sealed class ExportSettings
{
    public int ImageMaxHeight { get; set; } = 30000;

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
    public bool AssociateMarkdownFiles { get; set; }

    public bool AssociateTextFiles { get; set; }

    public string UiLanguage { get; set; } = "";

    public bool AutoCheckForUpdates { get; set; } = true;
}

public sealed class AppearanceSettings
{
    public static readonly int[] ZoomPercentOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200];

    public int ZoomPercent { get; set; } = 100;

    public bool RestoreZoomOnOpen { get; set; } = true;

    public bool CtrlWheelZoom { get; set; } = true;

    public bool TopMostWindow { get; set; }

    public bool AutoHideScrollbars { get; set; }

    public bool ShowCodeHighlight { get; set; }

    public bool FollowSystemColorMode { get; set; }

    public string DefaultLightThemeId { get; set; } = "white-only";

    public string DefaultDarkThemeId { get; set; } = "dark";

    public MenuBarStyle MenuBarStyle { get; set; } = MenuBarStyle.DarkThemeOnly;

    public bool ShowMenuKeyboardShortcuts { get; set; } = true;

    public bool ShowMenuMnemonics { get; set; } = true;

    public StatusBarSettings StatusBar { get; set; } = new();
}

public sealed class StatusBarSettings
{
    public bool SidebarToggleVisible { get; set; } = true;

    public bool CommandStatusVisible { get; set; } = true;

    public StatusBarCommandDisplayMode CommandDisplayMode { get; set; } = StatusBarCommandDisplayMode.Always;

    public bool WordCountVisible { get; set; } = true;

    public bool BlockTypeVisible { get; set; } = true;

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
    public float VisualLineHeight { get; set; } = 1.6f;

    public int VisualFontSize { get; set; } = 16;

    public int VisualMaxContentWidth { get; set; } = 820;

    public int SourceFontSize { get; set; } = 14;

    public string SourceFontFamily { get; set; } = "Cascadia Mono";

    public string SourceCjkFontFamily { get; set; } = "Microsoft YaHei";

    public CjkLanguageTag CjkLanguageTag { get; set; } = CjkLanguageTag.SimplifiedChinese;

    public bool VisualCjkAutoSpacing { get; set; } = true;

    public bool ExitBlockOnEmptyEnter { get; set; }

    public bool UseShiftEnterHardBreak { get; set; } = true;

    public bool AutoConvertUnsafeEmphasis { get; set; } = true;

    public bool EscapeLiteralSymbols { get; set; }

    public bool EscapeMarkdownLiteralSymbols { get; set; } = true;

    public string MarkdownCodeFence { get; set; } = "backtick";

    public string MarkdownEmphasisMarker { get; set; } = "asterisk";

    public string MarkdownBulletMarker { get; set; } = "dash";

    public int SourceIndentWidth { get; set; } = 2;

    public bool ShowParagraphBlockHandle { get; set; } = true;

    public string? UnsafeEmphasisPreference { get; set; }
}

public sealed class FileSettings
{
    public StartupAction StartupAction { get; set; } = StartupAction.NewDocument;

    public bool AutoSaveEnabled { get; set; }

    public bool SaveOnDocumentSwitch { get; set; } = true;

    public int SnapshotIntervalSeconds { get; set; } = 30;

    public bool RecordRecentFiles { get; set; } = true;

    public bool RecordRecentFolders { get; set; } = true;

    public string DefaultEncoding { get; set; } = "utf-8";

    public NewLineStyle NewLineStyle { get; set; } = NewLineStyle.Crlf;
}

public sealed class ImageSettings
{
    public ClipboardImageHandling ClipboardHandling { get; set; } = ClipboardImageHandling.SaveToDefaultDirectory;

    public FileImageHandling FileHandling { get; set; } = FileImageHandling.ReferenceOriginal;

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
