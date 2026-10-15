using MarkLeaf.Editor;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorMenuCommandScopeTests
{
    [TestMethod]
    public async Task AwaitedCutCannotSendCommandsAfterDocumentOrSessionChanges()
    {
        var document = Guid.NewGuid();
        var scope = new EditorMenuCommandScope(() => document);
        var current = true;
        using (scope.Begin(42, () => current))
        {
            var captured = scope.Current!;
            await Task.Yield();
            Assert.IsTrue(captured.IsCurrent);
            Assert.AreEqual(42L, captured.MenuSessionId);
            document = Guid.NewGuid();
            Assert.IsFalse(captured.IsCurrent, "A late cut/image continuation must not mutate a different tab.");
        }
        using (scope.Begin(43, () => current))
        {
            var captured = scope.Current!;
            current = false;
            Assert.IsFalse(captured.IsCurrent, "A superseding context menu invalidates the old selection.");
        }
        Assert.IsNull(scope.Current, "Unrelated shortcuts must remain unscoped.");
    }
}
