using MarkLeaf.Commands;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CommandRouterTests
{
    private static CommandRouter CreateRouter(
        ShortcutManager manager,
        Func<AppCommand, CommandState> getState,
        Action<AppCommand> execute)
        => new(manager, getState, execute);

    [TestMethod]
    public void TryExecuteById_ExecutesEnabledCommand()
    {
        AppCommand? executed = null;
        var router = CreateRouter(
            new ShortcutManager(new ShortcutSettings()),
            _ => new CommandState(true),
            command => executed = command);

        var handled = router.TryExecuteById((int)AppCommand.ShowAbout);

        Assert.IsTrue(handled);
        Assert.AreEqual(AppCommand.ShowAbout, executed);
    }

    [TestMethod]
    public void TryExecuteById_DoesNotExecuteDisabledCommand()
    {
        AppCommand? executed = null;
        var router = CreateRouter(
            new ShortcutManager(new ShortcutSettings()),
            _ => new CommandState(false),
            command => executed = command);

        var handled = router.TryExecuteById((int)AppCommand.SaveDocument);

        Assert.IsFalse(handled);
        Assert.IsNull(executed);
    }

    [TestMethod]
    public void TryExecuteShortcut_ConsumesKnownDisabledShortcutOnce()
    {
        AppCommand? executed = null;
        var router = CreateRouter(
            new ShortcutManager(new ShortcutSettings()),
            _ => new CommandState(false),
            command => executed = command);

        var handled = router.TryExecuteShortcut(Keys.Control | Keys.Z);

        Assert.IsTrue(handled);
        Assert.IsNull(executed);
    }

    [TestMethod]
    public void TryExecuteShortcut_RespectsCustomBinding()
    {
        AppCommand? executed = null;
        var manager = new ShortcutManager(new ShortcutSettings());
        manager.Set(AppCommand.Find, Keys.Control | Keys.Alt | Keys.G);
        var router = CreateRouter(manager, _ => new CommandState(true), command => executed = command);

        Assert.IsTrue(router.TryExecuteShortcut(Keys.Control | Keys.Alt | Keys.G));
        Assert.AreEqual(AppCommand.Find, executed);
        Assert.IsFalse(router.TryExecuteShortcut(Keys.Control | Keys.F));
    }

    [TestMethod]
    public void TryExecuteShortcut_IgnoresUnboundKeys()
    {
        AppCommand? executed = null;
        var router = CreateRouter(
            new ShortcutManager(new ShortcutSettings()),
            _ => new CommandState(true),
            command => executed = command);

        Assert.IsFalse(router.TryExecuteShortcut(Keys.Control | Keys.Alt | Keys.G));
        Assert.IsNull(executed);
    }
}
