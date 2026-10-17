namespace MarkLeaf.UI;

internal enum SidebarTarget { Workspace, Outline }
internal enum ViewNavigationStep { ExitMinimalMode, ExpandSidebar, ShowTarget, FocusTarget }

internal sealed record ViewNavigationSnapshot(
    bool SidebarVisible,
    bool OutlineDetached,
    bool StatusBarVisible);

internal sealed record ViewNavigationPlan(SidebarTarget Target, IReadOnlyList<ViewNavigationStep> Steps);

internal static class ViewNavigationPolicy
{
    public static bool RestoreStatusBarVisible(ViewNavigationSnapshot snapshot) => snapshot.StatusBarVisible;

    public static ViewNavigationPlan NavigateToSidebar(
        SidebarTarget target, bool FocusMode, bool SidebarCollapsed)
    {
        var steps = new List<ViewNavigationStep>();
        if (FocusMode) steps.Add(ViewNavigationStep.ExitMinimalMode);
        if (SidebarCollapsed) steps.Add(ViewNavigationStep.ExpandSidebar);
        steps.Add(ViewNavigationStep.ShowTarget);
        steps.Add(ViewNavigationStep.FocusTarget);
        return new(target, steps.AsReadOnly());
    }
}
