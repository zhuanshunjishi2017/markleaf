namespace MarkLeaf.Documents;

internal static class DocumentPathIdentity
{
    // Resolve each existing component: a link on a parent directory is just as
    // much an alias as a link on the file itself. Missing destinations remain
    // comparable so a Save As dialog can select a new file.
    public static bool Equals(string? first, string second) => first is not null
        && string.Equals(CanonicalPath(first), CanonicalPath(second), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var component in fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.Exists && entry.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                current = target.FullName;
        }
        return current;
    }
}
