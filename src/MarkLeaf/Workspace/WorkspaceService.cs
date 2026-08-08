using MarkLeaf.Services;

namespace MarkLeaf.Workspace;

internal sealed class WorkspaceService
{
    public string GetAvailableUntitledDocumentPath(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        for (var index = 1; ; index++)
        {
            var name = index == 1
                ? Loc.Get("document.untitledMd")
                : Loc.Format("document.untitledMdWithIndex", index);
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

    private static WorkspaceDocumentEntry[] EnumerateDocuments(
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
                        documents.Add(new WorkspaceDocumentEntry(
                            Path.GetFileName(path),
                            Path.GetFullPath(path),
                            folderName,
                            File.GetLastWriteTime(path)));
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
}
