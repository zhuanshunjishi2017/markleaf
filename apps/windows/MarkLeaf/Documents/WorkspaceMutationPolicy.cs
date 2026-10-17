namespace MarkLeaf.Documents;

internal static class WorkspaceMutationPolicy
{
    public static Task<string> TextForCopyAsync(IEnumerable<MarkdownDocument> documents, string path, Func<string, Task<string>> readDisk)
    {
        var opened = DocumentMenuPolicy.FindOpenDocument(documents, path);
        return opened is null ? readDisk(path) : Task.FromResult(opened.Markdown);
    }

    public static bool IsAffected(string documentPath, string entryPath, bool isDirectory)
    {
        var document = Path.GetFullPath(documentPath);
        var entry = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entryPath));
        return string.Equals(document, entry, StringComparison.OrdinalIgnoreCase)
            || isDirectory && document.StartsWith(Path.EndsInDirectorySeparator(entry)
                ? entry : entry + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void DetachDeleted(IEnumerable<MarkdownDocument> documents, string path, bool isDirectory, bool recycled)
    {
        if (!recycled) return;
        foreach (var document in documents.Where(d => d.FilePath is { } file && IsAffected(file, path, isDirectory)))
        {
            document.FilePath = null;
            document.IsDirty = true;
            document.HasUnsavedMetadataChanges = true;
            document.IsReadOnly = false;
            document.IsUserReadOnly = false;
            document.LastKnownFingerprint = null;
            document.LastKnownWriteTime = null;
        }
    }

    public static void Rename(IEnumerable<MarkdownDocument> documents, string oldPath, string newPath, bool isDirectory)
    {
        foreach (var document in documents.Where(d => d.FilePath is { } file && IsAffected(file, oldPath, isDirectory)))
        {
            var relative = Path.GetRelativePath(oldPath, document.FilePath!);
            document.FilePath = relative == "." ? newPath : Path.Combine(newPath, relative);
        }
    }
}
