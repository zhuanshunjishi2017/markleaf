using System.Reflection;
using MarkLeaf.Editor;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class OutlineTreeViewTests
{
    [STATestMethod]
    public void RefreshPreservesCollapsedHeadingByTextIdentity()
    {
        using var tree = new OutlineTreeView();
        tree.SetItems(new[]
        {
            new EditorOutlineItem(1, "Chapter", 0),
            new EditorOutlineItem(2, "Section", 6),
            new EditorOutlineItem(3, "Detail", 12),
        });
        tree.CollapseAll();

        tree.SetItems(new[]
        {
            new EditorOutlineItem(1, "Chapter", 0),
            new EditorOutlineItem(1, "Inserted", 6),
            new EditorOutlineItem(2, "Section", 12),
            new EditorOutlineItem(3, "Detail", 18),
        });

        Assert.IsTrue(tree.IsVisible(0), "The original root remains visible.");
        Assert.IsTrue(tree.IsVisible(6), "The newly inserted root is independent and expanded.");
        Assert.IsFalse(tree.IsExpanded(12), "The Section node itself must remain collapsed.");
        Assert.IsFalse(tree.IsVisible(18), "Children of a collapsed Section must stay hidden after positions shift.");
    }
}
