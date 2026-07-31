using MarkLeaf.Commands;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CommandRouterTests
{
    [TestMethod]
    public void TryExecuteById_ExecutesEnabledCommand()
    {
        AppCommand? executed = null;
        var router = new CommandRouter(_ => new CommandState(true), command => executed = command);

        var handled = router.TryExecuteById((int)AppCommand.ShowAbout);

        Assert.IsTrue(handled);
        Assert.AreEqual(AppCommand.ShowAbout, executed);
    }

    [TestMethod]
    public void TryExecuteById_DoesNotExecuteDisabledCommand()
    {
        AppCommand? executed = null;
        var router = new CommandRouter(_ => new CommandState(false), command => executed = command);

        var handled = router.TryExecuteById((int)AppCommand.SaveDocument);

        Assert.IsFalse(handled);
        Assert.IsNull(executed);
    }

    [TestMethod]
    public void TryExecuteShortcut_ConsumesKnownDisabledShortcutOnce()
    {
        AppCommand? executed = null;
        var router = new CommandRouter(_ => new CommandState(false), command => executed = command);

        var handled = router.TryExecuteShortcut(Keys.Control | Keys.Z);

        Assert.IsTrue(handled);
        Assert.IsNull(executed);
    }
}
