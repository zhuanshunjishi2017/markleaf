namespace MarkLeaf.UI;

internal enum SidebarFocusTarget
{
    WorkspaceTree,
    WorkspaceList,
    Outline,
}

internal static class SidebarFocusTargetPolicy
{
    public static SidebarFocusTarget Resolve(SidebarTarget target, bool listViewActive) => target switch
    {
        SidebarTarget.Workspace => listViewActive
            ? SidebarFocusTarget.WorkspaceList
            : SidebarFocusTarget.WorkspaceTree,
        _ => SidebarFocusTarget.Outline,
    };
}
