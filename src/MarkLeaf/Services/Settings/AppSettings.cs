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
    Upload,
}

public enum FileImageHandling
{
    ReferenceOriginal,
    CopyToAssets,
    Upload,
}

public enum MenuBarStyle
{
    DarkThemeOnly,
    Always,
    System,
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
            MarkdownStyle = "serif",
        };
    }
}

public sealed class GeneralSettings
{
    public bool AssociateMarkdownFiles { get; set; }

    public bool AssociateTextFiles { get; set; }

    public string UiLanguage { get; set; } = "";
}

public sealed class AppearanceSettings
{
    public static readonly int[] ZoomPercentOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200];

    public int ZoomPercent { get; set; } = 100;

    public bool RestoreZoomOnOpen { get; set; } = true;

    public bool CtrlWheelZoom { get; set; } = true;

    public bool TopMostWindow { get; set; }

    public bool AutoHideScrollbars { get; set; }

    public bool FollowSystemColorMode { get; set; }

    public MenuBarStyle MenuBarStyle { get; set; } = MenuBarStyle.DarkThemeOnly;
}

public sealed class EditorSettings
{
    public float VisualLineHeight { get; set; } = 1.6f;

    public int VisualFontSize { get; set; } = 16;

    public int VisualMaxContentWidth { get; set; } = 820;

    public int SourceFontSize { get; set; } = 14;

    public string SourceFontFamily { get; set; } = "Cascadia Mono";

    public string SourceCjkFontFamily { get; set; } = "Microsoft YaHei";

    public int SourceIndentWidth { get; set; } = 2;
}

public sealed class FileSettings
{
    public StartupAction StartupAction { get; set; } = StartupAction.NewDocument;

    public bool AutoSaveEnabled { get; set; }

    public bool SaveOnDocumentSwitch { get; set; } = true;

    public int SnapshotIntervalSeconds { get; set; } = 30;

    public bool RecordRecentFiles { get; set; } = true;

    public bool RecordRecentFolders { get; set; } = true;

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

    public List<string> RecentFolders { get; set; } = [];

    public List<string> RecentFiles { get; set; } = [];
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

    public bool SidebarCollapsed { get; set; }
}
