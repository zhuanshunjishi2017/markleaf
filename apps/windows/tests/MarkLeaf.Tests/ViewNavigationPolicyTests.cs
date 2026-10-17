using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ViewNavigationPolicyTests
{
    [TestMethod]
    public void MinimalModeRestoresTheExactPreviousStatusBarVisibility()
    {
        var snapshot = new ViewNavigationSnapshot(
            SidebarVisible: false,
            OutlineDetached: true,
            StatusBarVisible: false);

        Assert.IsFalse(ViewNavigationPolicy.RestoreStatusBarVisible(snapshot));
    }

    [TestMethod]
    public void SidebarNavigationRevealsBeforeFocus()
    {
        var plan = ViewNavigationPolicy.NavigateToSidebar(
            SidebarTarget.Workspace, FocusMode: false, SidebarCollapsed: true);

        CollectionAssert.AreEqual(new[]
        {
            ViewNavigationStep.ExpandSidebar,
            ViewNavigationStep.ShowTarget,
            ViewNavigationStep.FocusTarget,
        }, plan.Steps.ToArray());
    }

    [TestMethod]
    public void SidebarNavigationExitsMinimalBeforeExplicitReveal()
    {
        var plan = ViewNavigationPolicy.NavigateToSidebar(
            SidebarTarget.Outline, FocusMode: true, SidebarCollapsed: true);

        CollectionAssert.AreEqual(new[]
        {
            ViewNavigationStep.ExitMinimalMode,
            ViewNavigationStep.ExpandSidebar,
            ViewNavigationStep.ShowTarget,
            ViewNavigationStep.FocusTarget,
        }, plan.Steps.ToArray());
    }

    [TestMethod]
    public void AlreadyVisibleSidebarDoesNotExpandAgain()
    {
        var plan = ViewNavigationPolicy.NavigateToSidebar(
            SidebarTarget.Outline, FocusMode: false, SidebarCollapsed: false);

        CollectionAssert.AreEqual(new[]
        {
            ViewNavigationStep.ShowTarget,
            ViewNavigationStep.FocusTarget,
        }, plan.Steps.ToArray());
    }

    [TestMethod]
    public void WorkspaceViewSwitchRevealsBeforeToggling()
    {
        var hiddenTree = WorkspaceViewNavigationPolicy.Switch(
            TreeViewDesired: true, ListViewActive: true, FocusMode: true, SidebarCollapsed: true);
        CollectionAssert.AreEqual(new[]
        {
            ViewNavigationStep.ExitMinimalMode,
            ViewNavigationStep.ExpandSidebar,
            ViewNavigationStep.ShowTarget,
            ViewNavigationStep.FocusTarget,
        }, hiddenTree.Navigation.Steps.ToArray());
        Assert.IsTrue(hiddenTree.SwitchPresentation);

        var visibleListNoop = WorkspaceViewNavigationPolicy.Switch(
            TreeViewDesired: false, ListViewActive: true, FocusMode: false, SidebarCollapsed: false);
        CollectionAssert.AreEqual(new[]
        {
            ViewNavigationStep.ShowTarget,
            ViewNavigationStep.FocusTarget,
        }, visibleListNoop.Navigation.Steps.ToArray());
        Assert.IsFalse(visibleListNoop.SwitchPresentation);
    }
}
