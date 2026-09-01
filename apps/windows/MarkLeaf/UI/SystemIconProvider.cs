namespace MarkLeaf.UI;

internal static class SystemIconProvider
{
    public static bool IsWindows11 { get; } = DetectWindows11();

    public static string IconFontName => IsWindows11 ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    // Arrow icons
    public static string RightArrow => IsWindows11 ? "" : "\uE76C";
    public static string DownArrow => IsWindows11 ? "" : "\uE70D";

    // Scrollbar arrows
    public static string ScrollUpArrow => IsWindows11 ? "" : "\uE70E";
    public static string ScrollDownArrow => IsWindows11 ? "" : "\uE70D";

    // File type icons
    public static string TextFileIcon => IsWindows11 ? "" : "\uE8A5";
    public static string MarkdownFileIcon => IsWindows11 ? "" : "";

    // View toggle icons
    public static string TreeViewIcon => IsWindows11 ? "" : "\uF0E2";
    public static string ListViewIcon => IsWindows11 ? "" : "\uEA37";
    public static string ExpandSidebarIcon => IsWindows11 ? "" : "\uE8A0";
    public static string CollapseSidebarIcon => IsWindows11 ? "" : "\uE89F";

    // Folder icons (Win10 uses same icon for both states)
    public static string FolderExpandedIcon => IsWindows11 ? "" : "\uE8B7";
    public static string FolderCollapsedIcon => IsWindows11 ? "" : "\uE8B7";
    public static string FolderIcon => IsWindows11 ? "" : "\uE8B7";

    // Common Fluent glyphs and their MDL2 Assets equivalents.
    public static string SearchIcon => IsWindows11 ? "\uE721" : "\uE721";
    public static string ClearIcon => IsWindows11 ? "\uEB90" : "\uE894";
    public static string OpenFolderIcon => IsWindows11 ? "" : "\uE838";
    public static string NewFileIcon => IsWindows11 ? "\uECC8" : "\uE710";
    public static string MergeIcon => IsWindows11 ? "\uE8A0" : "\uE8AB";
    public static string DetachIcon => IsWindows11 ? "\uE89F" : "\uE89F";

    public static string PreferencesFileIcon => IsWindows11 ? "" : "\uE8A5";
    public static string PreferencesAppearanceIcon => IsWindows11 ? "" : "\uE771";
    public static string PreferencesEditorIcon => IsWindows11 ? "" : "\uE70F";
    public static string PreferencesImagesIcon => IsWindows11 ? "" : "\uE91B";
    public static string PreferencesGeneralIcon => IsWindows11 ? "" : "\uE713";
    public static string PdfIcon => IsWindows11 ? "\uEA90" : "\uEA90";
    public static string HtmlIcon => IsWindows11 ? "\uE943" : "\uE943";
    public static string ImageIcon => IsWindows11 ? "\uE91B" : "\uE91B";

    private static bool DetectWindows11()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT
            && Environment.OSVersion.Version.Major >= 10
            && Environment.OSVersion.Version.Build >= 22000;
    }
}
