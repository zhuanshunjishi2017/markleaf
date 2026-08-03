namespace MarkLeaf.Services.Settings;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public WindowSettings MainWindow { get; set; } = new();

    public WorkspaceSettings Workspace { get; set; } = new();

    public string MarkdownStyle { get; set; } = "serif";
}

public sealed class WorkspaceSettings
{
    public string? LastFolder { get; set; }

    public List<string> RecentFolders { get; set; } = [];
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
