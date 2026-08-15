namespace MarkLeaf.Workspace;

internal sealed record WorkspaceDocumentEntry(
    string Name,
    string FullPath,
    string FolderName,
    DateTime LastWriteTime);

internal enum WorkspaceDocumentSortOrder
{
    FileNameAscending,
    FileNameDescending,
    ModifiedTimeAscending,
    ModifiedTimeDescending,
}
