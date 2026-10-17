using MarkLeaf.Documents;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class DocumentBatchPolicyTests
{
    [TestMethod]
    public async Task BatchCancelStopsAfterCompletedItems()
    {
        var started = new List<string>();
        var gate = new TaskCompletionSource<bool>();
        var result = DocumentBatchPolicy.RunAsync(new[] { "A", "B", "C" }, item =>
        {
            started.Add(item);
            return item == "A" ? Task.FromResult(true) : gate.Task;
        });
        CollectionAssert.AreEqual(new[] { "A", "B" }, started);
        gate.SetResult(false);
        Assert.IsFalse(await result);
        CollectionAssert.AreEqual(new[] { "A", "B" }, started);
    }

    [TestMethod]
    public void SaveAllTargets_IncludesDirtyUntitledSkipsReadOnly()
    {
        var dirtyUntitled = new MarkdownDocument { IsDirty = true };
        var dirtyReadOnly = new MarkdownDocument { IsDirty = true, IsReadOnly = true };
        var dirtyUserReadOnly = new MarkdownDocument { IsDirty = true, IsUserReadOnly = true };
        var cleanNamed = new MarkdownDocument { FilePath = @"C:\work\clean.md" };
        CollectionAssert.AreEqual(new[] { dirtyUntitled.Id }, DocumentMenuPolicy.SaveAllTargets(
            [dirtyUntitled, dirtyReadOnly, dirtyUserReadOnly, cleanNamed]).Select(d => d.Id).ToArray());
    }
}
