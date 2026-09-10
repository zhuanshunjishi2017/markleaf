using System.Diagnostics;

namespace MarkLeaf.Services.ExternalLinks;

internal static class ExternalLinkService
{
    public static bool IsAllowed(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto";
    }

    public static void Open(string value, string? documentPath = null)
    {
        if (IsAllowed(value))
        {
            Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
            return;
        }

        var localPath = TryResolveLocalPath(value, documentPath);
        if (localPath is null || !File.Exists(localPath))
        {
            throw new FileNotFoundException("The linked local file does not exist.", localPath ?? value);
        }

        OpenLocal(localPath);
    }

    public static string? TryResolveLocalPath(string value, string? documentPath)
    {
        return ResolveLocalPath(value, documentPath);
    }

    public static void OpenLocal(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string? ResolveLocalPath(string value, string? documentPath)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeFile)
            {
                return null;
            }

            return Path.GetFullPath(uri.LocalPath);
        }

        var path = Uri.UnescapeDataString(trimmed.Replace('/', Path.DirectorySeparatorChar));
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        var directory = documentPath is null
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(documentPath));
        return directory is null ? null : Path.GetFullPath(Path.Combine(directory, path));
    }
}
