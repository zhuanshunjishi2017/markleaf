using MarkLeaf.Documents;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class WorkspaceMutationPolicyTests
{
    [TestMethod]
    public async Task CopyContentsPrefersOpenBuffer()
    {
        var document = new MarkdownDocument { FilePath = @"C:\work\note.md", Markdown = "unsaved", IsDirty = true };
        var reads = 0;
        Task<string> ReadDisk(string _) { reads++; return Task.FromResult("disk"); }
        Assert.AreEqual("unsaved", await WorkspaceMutationPolicy.TextForCopyAsync([document], @"c:\work\NOTE.md", ReadDisk));
        Assert.AreEqual(0, reads);
        Assert.AreEqual("disk", await WorkspaceMutationPolicy.TextForCopyAsync([document], @"C:\work\other.md", ReadDisk));
        Assert.AreEqual(1, reads);
    }

    [TestMethod]
    public void WorkspaceDeletePreservesAllOpenBuffersAndRequiresRecycleSuccess()
    {
        var first = new MarkdownDocument { FilePath = @"C:\work\foo\一.md", Markdown = "unsaved A", IsDirty = true, VisualSelectionFrom = 2 };
        var second = new MarkdownDocument { FilePath = @"C:\work\foo\二.md", Markdown = "buffer B", IsReadOnly = true, ScrollTop = 30 };
        var sibling = new MarkdownDocument { FilePath = @"C:\work\foobar\x.md", Markdown = "C" };
        var documents = new[] { first, second, sibling };
        WorkspaceMutationPolicy.DetachDeleted(documents, @"c:\WORK\foo", true, recycled: false);
        Assert.AreEqual(@"C:\work\foo\一.md", first.FilePath);
        WorkspaceMutationPolicy.DetachDeleted(documents, @"c:\WORK\foo", true, recycled: true);
        Assert.IsNull(first.FilePath);
        Assert.IsNull(second.FilePath);
        Assert.AreEqual("unsaved A", first.Markdown);
        Assert.AreEqual("buffer B", second.Markdown);
        Assert.AreEqual(2, first.VisualSelectionFrom);
        Assert.AreEqual(30d, second.ScrollTop);
        Assert.IsTrue(first.IsDirty && second.IsDirty);
        Assert.IsFalse(second.IsReadOnly);
        Assert.AreEqual(@"C:\work\foobar\x.md", sibling.FilePath);
    }

    [TestMethod]
    public void PrefixSiblingNotAffected()
    {
        Assert.IsFalse(WorkspaceMutationPolicy.IsAffected(@"C:\work\foobar\a.md", @"C:\work\foo", true));
        Assert.IsTrue(WorkspaceMutationPolicy.IsAffected(@"C:\work\foo\a.md", @"c:\WORK\foo", true));
    }

    [TestMethod]
    public void RenameDirectoryUpdatesDescendantsWithoutLosingDirtyState()
    {
        var document = new MarkdownDocument { FilePath = @"C:\work\foo\sub\一.md", Markdown = "buffer", IsDirty = true, SourceSelectionFrom = 6 };
        WorkspaceMutationPolicy.Rename([document], @"C:\work\foo", @"C:\work\中文 目录", true);
        Assert.AreEqual(@"C:\work\中文 目录\sub\一.md", document.FilePath);
        Assert.AreEqual("buffer", document.Markdown);
        Assert.IsTrue(document.IsDirty);
        Assert.AreEqual(6, document.SourceSelectionFrom);
    }
}
