using System.Collections.Concurrent;
using System.Text;
using MarkLeaf.Documents;
using MarkLeaf.Services;

namespace MarkLeaf.Workspace;

internal sealed class WorkspaceService
{
    private const int PreviewReadLimit = 2 * 1024;
    private readonly ConcurrentDictionary<string, PreviewCacheEntry> _previewCache =
        new(StringComparer.OrdinalIgnoreCase);

    public string GetAvailableUntitledDocumentPath(
        string directory,
        NewDocumentKind kind = NewDocumentKind.Markdown)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var initialName = Loc.Get(kind == NewDocumentKind.PlainText
            ? "document.untitledTxt"
            : "document.untitledMd");
        var stem = Path.GetFileNameWithoutExtension(initialName);
        var extension = Path.GetExtension(initialName);
        for (var index = 1; ; index++)
        {
            var name = index == 1
                ? initialName
                : $"{stem} ({index}){extension}";
            var path = Path.Combine(fullDirectory, name);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }
        }
    }

    public string GetAvailableUntitledDirectoryPath(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var initialName = Loc.Get("workspace.untitledFolder");
        for (var index = 1; ; index++)
        {
            var name = index == 1 ? initialName : $"{initialName} ({index})";
            var path = Path.Combine(fullDirectory, name);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return path;
            }
        }
    }

    public Task<IReadOnlyList<WorkspaceEntry>> GetChildrenAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(directory);
        return Task.Run<IReadOnlyList<WorkspaceEntry>>(
            () => EnumerateChildren(fullPath, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<WorkspaceDocumentEntry>> GetDocumentsAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(directory);
        return Task.Run<IReadOnlyList<WorkspaceDocumentEntry>>(
            () => EnumerateDocuments(fullPath, cancellationToken),
            cancellationToken);
    }

    public void ResetPreviewCache() => _previewCache.Clear();

    public void InvalidatePreview(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            _previewCache.TryRemove(Path.GetFullPath(path), out _);
        }
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        string directory,
        string query,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(directory);
        return Task.Run<IReadOnlyList<SearchResult>>(
            () => Search(fullPath, query, cancellationToken),
            cancellationToken);
    }

    private static SearchResult[] Search(
        string rootDirectory,
        string query,
        CancellationToken cancellationToken)
    {
        var rootName = Path.GetFileName(
            rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = rootDirectory;
        }

        var results = new List<SearchResult>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootDirectory);
        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if ((attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                pendingDirectories.Push(path);
                            }
                            continue;
                        }

                        var extension = Path.GetExtension(path);
                        if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var fileName = Path.GetFileName(path);
                        var parent = Path.GetDirectoryName(path)!;
                        var relativeFolder = Path.GetRelativePath(rootDirectory, parent);
                        var folderName = relativeFolder == "."
                            ? rootName
                            : relativeFolder;

                        var lastWriteTime = File.GetLastWriteTime(path);
                        var plainText = ReadPlainText(path, extension);
                        if (fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new SearchResult(
                                fileName,
                                Path.GetFullPath(path),
                                folderName,
                                lastWriteTime,
                                plainText,
                                IsContentMatch: false,
                                query));
                            continue;
                        }

                        var snippet = FindSnippet(plainText, query);
                        if (snippet is not null)
                        {
                            results.Add(new SearchResult(
                                fileName,
                                Path.GetFullPath(path),
                                folderName,
                                lastWriteTime,
                                snippet,
                                IsContentMatch: true,
                                query));
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Files can disappear or become inaccessible during enumeration.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Continue with other readable directories.
            }
        }

        return results
            .OrderBy(result => result.FolderName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(result => result.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string? ReadPlainText(string path, string extension)
    {
        try
        {
            var source = File.ReadAllText(path);
            var plainText = MarkdownPlainText.FromDocument(
                source,
                string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase));
            return plainText.Length == 0 ? null : plainText;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Skip unreadable files.
        }

        return null;
    }

    private string? ReadPreviewPlainText(
        string path,
        string extension,
        DateTime lastWriteTime,
        long fileLength)
    {
        var fullPath = Path.GetFullPath(path);
        if (_previewCache.TryGetValue(fullPath, out var cached))
        {
            return cached.Preview;
        }

        string? preview = null;
        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                PreviewReadLimit,
                FileOptions.SequentialScan);
            var buffer = new byte[Math.Min(PreviewReadLimit, (int)Math.Min(fileLength, int.MaxValue))];
            var read = stream.Read(buffer, 0, buffer.Length);
            var bytes = buffer.AsSpan(0, read).ToArray();
            var source = DecodePreview(bytes);
            var plainText = MarkdownPlainText.FromDocument(
                source,
                string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase));
            preview = plainText.Length == 0 ? null : plainText;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Skip unreadable files.
        }

        _previewCache[fullPath] = new PreviewCacheEntry(lastWriteTime, fileLength, preview);
        return preview;
    }

    private static string DecodePreview(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var detected = DocumentEncodingPolicy.Detect(bytes);
        for (var length = bytes.Length; length >= Math.Max(0, bytes.Length - 4); length--)
        {
            try
            {
                return DocumentEncodingPolicy.Decode(bytes.AsSpan(0, length).ToArray(), detected.Policy);
            }
            catch (DecoderFallbackException)
            {
                // The byte limit may split the final multi-byte character.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string? FindSnippet(string? plainText, string query)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }

        var index = plainText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = Math.Max(0, index - 2);
        var length = Math.Min(plainText.Length - start, query.Length + 42);
        return plainText.Substring(start, length).TrimEnd();
    }

    private static WorkspaceEntry[] EnumerateChildren(string directory, CancellationToken cancellationToken)
    {
        var entries = new List<WorkspaceEntry>();
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var extension = Path.GetExtension(path);
                if (!isDirectory
                    && !string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(new WorkspaceEntry(
                    Path.GetFileName(path),
                    Path.GetFullPath(path),
                    isDirectory));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Entries can disappear while a large folder is being enumerated.
            }
        }

        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private WorkspaceDocumentEntry[] EnumerateDocuments(
        string rootDirectory,
        CancellationToken cancellationToken)
    {
        var rootName = Path.GetFileName(
            rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = rootDirectory;
        }

        var documents = new List<WorkspaceDocumentEntry>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootDirectory);
        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Pop();
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if ((attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                pendingDirectories.Push(path);
                            }
                            continue;
                        }

                        var extension = Path.GetExtension(path);
                        if (!string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var parent = Path.GetDirectoryName(path)!;
                        var relativeFolder = Path.GetRelativePath(rootDirectory, parent);
                        var folderName = relativeFolder == "."
                            ? rootName
                            : relativeFolder;
                        var lastWriteTime = File.GetLastWriteTime(path);
                        var fileLength = new FileInfo(path).Length;
                        documents.Add(new WorkspaceDocumentEntry(
                            Path.GetFileName(path),
                            Path.GetFullPath(path),
                            folderName,
                            lastWriteTime,
                            ReadPreviewPlainText(path, extension, lastWriteTime, fileLength)));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Files can disappear or become inaccessible during enumeration.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Continue with other readable directories.
            }
        }

        return documents
            .OrderByDescending(document => document.LastWriteTime)
            .ThenBy(document => document.FolderName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(document => document.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private sealed record PreviewCacheEntry(
        DateTime LastWriteTime,
        long FileLength,
        string? Preview);
}
