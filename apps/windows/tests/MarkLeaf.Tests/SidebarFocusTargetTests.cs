using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class SidebarFocusTargetTests
{
    [TestMethod]
    public void WorkspaceTargetFollowsTheActiveWorkspacePresentation()
    {
        Assert.AreEqual(SidebarFocusTarget.WorkspaceTree, SidebarFocusTargetPolicy.Resolve(
            SidebarTarget.Workspace, listViewActive: false));
        Assert.AreEqual(SidebarFocusTarget.WorkspaceList, SidebarFocusTargetPolicy.Resolve(
            SidebarTarget.Workspace, listViewActive: true));
        Assert.AreEqual(SidebarFocusTarget.Outline, SidebarFocusTargetPolicy.Resolve(
            SidebarTarget.Outline, listViewActive: true));
    }
}
