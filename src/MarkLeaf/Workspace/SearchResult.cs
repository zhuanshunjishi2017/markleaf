namespace MarkLeaf.Workspace;

internal sealed record SearchResult(
    string FileName,
    string FullPath,
    string FolderName,
    DateTime LastWriteTime,
    string? Snippet);
