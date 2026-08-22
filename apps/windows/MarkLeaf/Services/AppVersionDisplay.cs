namespace MarkLeaf.Services;

internal static class AppVersionDisplay
{
    public static string Format(string version, string build) =>
        $"Version {version} (Build {build})";
}
