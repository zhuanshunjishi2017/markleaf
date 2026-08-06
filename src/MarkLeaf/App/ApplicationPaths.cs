namespace MarkLeaf.App;

internal sealed record ApplicationPaths(
    string DataDirectory,
    string SettingsFile,
    string LogDirectory,
    string DefaultImageDirectory,
    string RecoveryDirectory,
    string WebView2UserDataDirectory)
{
    public static ApplicationPaths Create(string? overrideRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarkLeaf")
            : Path.GetFullPath(overrideRoot);

        return new ApplicationPaths(
            root,
            Path.Combine(root, "settings.json"),
            Path.Combine(root, "Logs"),
            Path.Combine(root, "Cache"),
            Path.Combine(root, "Recovery"),
            Path.Combine(root, "Cache", "WebView2"));
    }
}
