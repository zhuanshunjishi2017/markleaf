namespace MarkLeaf.Workspace;

internal sealed record WorkspaceDocumentEntry(
    string Name,
    string FullPath,
    string FolderName,
    DateTime LastWriteTime,
    string? Preview);

internal enum WorkspaceDocumentSortOrder
{
    FileNameAscending,
    FileNameDescending,
    ModifiedTimeAscending,
    ModifiedTimeDescending,
}
