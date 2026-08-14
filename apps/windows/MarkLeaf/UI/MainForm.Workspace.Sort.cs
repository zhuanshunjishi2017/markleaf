using MarkLeaf.Services;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private void SetWorkspaceSortField(bool modifiedTime)
    {
        var descending = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.FileNameDescending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        SetWorkspaceDocumentSortOrder(modifiedTime
            ? (descending ? WorkspaceDocumentSortOrder.ModifiedTimeDescending : WorkspaceDocumentSortOrder.ModifiedTimeAscending)
            : (descending ? WorkspaceDocumentSortOrder.FileNameDescending : WorkspaceDocumentSortOrder.FileNameAscending));
    }

    private void SetWorkspaceSortDirection(bool descending)
    {
        var modifiedTime = _workspaceDocumentSortOrder is WorkspaceDocumentSortOrder.ModifiedTimeAscending
            or WorkspaceDocumentSortOrder.ModifiedTimeDescending;
        SetWorkspaceDocumentSortOrder(modifiedTime
            ? (descending ? WorkspaceDocumentSortOrder.ModifiedTimeDescending : WorkspaceDocumentSortOrder.ModifiedTimeAscending)
            : (descending ? WorkspaceDocumentSortOrder.FileNameDescending : WorkspaceDocumentSortOrder.FileNameAscending));
    }

    private void SetWorkspaceDocumentSortOrder(WorkspaceDocumentSortOrder sortOrder)
    {
        _workspaceDocumentSortOrder = sortOrder;
        _workspaceTree.SetSortOrder(sortOrder);
        ApplyWorkspaceDocumentSort();
        SetStatus(Loc.Format("status.workspaceSortChanged", GetWorkspaceSortDescription(sortOrder)));
    }

    private static string GetWorkspaceSortDescription(WorkspaceDocumentSortOrder sortOrder)
    {
        return sortOrder switch
        {
            WorkspaceDocumentSortOrder.FileNameAscending => Loc.Get("workspace.sortFileNameAscending"),
            WorkspaceDocumentSortOrder.FileNameDescending => Loc.Get("workspace.sortFileNameDescending"),
            WorkspaceDocumentSortOrder.ModifiedTimeAscending => Loc.Get("workspace.sortModifiedTimeAscending"),
            _ => Loc.Get("workspace.sortModifiedTimeDescending"),
        };
    }

    private void ApplyWorkspaceDocumentSort()
    {
        IEnumerable<WorkspaceDocumentEntry> documents = _workspaceDocumentSortOrder switch
        {
            WorkspaceDocumentSortOrder.FileNameAscending => _workspaceDocuments
                .OrderBy(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
            WorkspaceDocumentSortOrder.FileNameDescending => _workspaceDocuments
                .OrderByDescending(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
            WorkspaceDocumentSortOrder.ModifiedTimeAscending => _workspaceDocuments
                .OrderBy(document => document.LastWriteTime)
                .ThenBy(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => _workspaceDocuments
                .OrderByDescending(document => document.LastWriteTime)
                .ThenBy(document => document.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        _workspaceDocumentList.SetDocuments(documents.ToArray());
        _workspaceDocumentList.SelectedPath = _document?.FilePath;
    }
}
