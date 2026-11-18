using System.Globalization;

namespace MarkLeaf.Services;

internal static class ChangelogResource
{
    public static string? PrepareCachedPath(string resourceDirectory, string cacheDirectory, string? language)
    {
        var culture = (string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture.Name : language)
            .Replace('_', '-').ToLowerInvariant();
        var name = culture switch
        {
            "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" => "changelog.zh-TW.md",
            "en" => "changelog.en-US.md",
            "ja" => "changelog.ja-JP.md",
            _ when culture.StartsWith("en-", StringComparison.Ordinal) => "changelog.en-US.md",
            _ when culture.StartsWith("ja-", StringComparison.Ordinal) => "changelog.ja-JP.md",
            _ => "changelog.md",
        };
        var fallback = Path.Combine(resourceDirectory, "changelog.md");
        var source = Path.Combine(resourceDirectory, name);
        if (!File.Exists(source)) source = fallback;
        if (!File.Exists(source)) return null;

        Directory.CreateDirectory(cacheDirectory);
        // Localized releases link to the complete Chinese history beside the cached document.
        if (source != fallback && File.Exists(fallback))
            File.Copy(fallback, Path.Combine(cacheDirectory, "changelog.md"), overwrite: true);
        var cached = Path.Combine(cacheDirectory, Path.GetFileName(source));
        File.Copy(source, cached, overwrite: true);
        return cached;
    }
}
