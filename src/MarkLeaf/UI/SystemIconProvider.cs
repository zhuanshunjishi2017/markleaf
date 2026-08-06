namespace MarkLeaf.UI;

internal static class SystemIconProvider
{
    public static bool IsWindows11 { get; } = DetectWindows11();

    public static string IconFontName => IsWindows11 ? "Segoe Fluent Icons" : "Segoe UI Symbol";

    // Arrow icons
    public static string RightArrow => IsWindows11 ? "" : "";
    public static string DownArrow => IsWindows11 ? "" : "";

    // Scrollbar arrows
    public static string ScrollUpArrow => IsWindows11 ? "" : "";
    public static string ScrollDownArrow => IsWindows11 ? "" : "";

    // File type icons
    public static string TextFileIcon => IsWindows11 ? "" : "";
    public static string MarkdownFileIcon => IsWindows11 ? "" : "";

    // View toggle icons
    public static string TreeViewIcon => IsWindows11 ? "" : "";
    public static string ListViewIcon => IsWindows11 ? "" : "";

    // Folder icons (Win10 uses same icon for both states)
    public static string FolderExpandedIcon => IsWindows11 ? "" : "";
    public static string FolderCollapsedIcon => IsWindows11 ? "" : "";
    public static string FolderIcon => IsWindows11 ? "" : "";

    private static bool DetectWindows11()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT
            && Environment.OSVersion.Version.Major >= 10
            && Environment.OSVersion.Version.Build >= 22000;
    }
}
