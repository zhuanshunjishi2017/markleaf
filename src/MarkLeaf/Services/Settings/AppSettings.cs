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

    public string MarkdownStyle { get; set; } = "serif";
}

public sealed class GeneralSettings
{
    public bool AssociateMarkdownFiles { get; set; }

    public bool AssociateTextFiles { get; set; }
}

public sealed class AppearanceSettings
{
    public static readonly int[] ZoomPercentOptions = [50, 75, 90, 100, 110, 125, 150, 175, 200];

    public int ZoomPercent { get; set; } = 100;

    public bool RestoreZoomOnOpen { get; set; } = true;

    public bool CtrlWheelZoom { get; set; } = true;

    public bool TopMostWindow { get; set; }

    public bool AutoHideScrollbars { get; set; }
}

public sealed class EditorSettings
{
    public float VisualLineHeight { get; set; } = 1.6f;

    public int VisualFontSize { get; set; } = 16;

    public int VisualMaxContentWidth { get; set; } = 820;

    public int SourceFontSize { get; set; } = 14;

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

public sealed class WorkspaceSettings
{
    public string? LastFolder { get; set; }

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
}
