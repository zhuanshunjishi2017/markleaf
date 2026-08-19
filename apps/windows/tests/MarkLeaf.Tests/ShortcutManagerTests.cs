using MarkLeaf.Commands;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ShortcutManagerTests
{
    private static ShortcutManager Create() => new(new ShortcutSettings());

    [TestMethod]
    public void Default_ResolvesCatalogDefaults()
    {
        var manager = Create();

        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.F, out var command));
        Assert.AreEqual(AppCommand.Find, command);
        Assert.AreEqual("Ctrl+F", manager.GetShortcutText(AppCommand.Find));
    }

    [TestMethod]
    public void Clear_ReturnsNullTextAndIsNotResolved()
    {
        var manager = Create();
        manager.Clear(AppCommand.Find);

        Assert.IsNull(manager.GetShortcutText(AppCommand.Find));
        Assert.IsFalse(manager.TryGetCommand(Keys.Control | Keys.F, out _));
    }

    [TestMethod]
    public void Set_OverridesDefaultAndReleasesOld()
    {
        var manager = Create();
        Assert.IsTrue(manager.Set(AppCommand.Find, Keys.Control | Keys.Alt | Keys.G));

        Assert.AreEqual("Ctrl+Alt+G", manager.GetShortcutText(AppCommand.Find));
        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.Alt | Keys.G, out var command));
        Assert.AreEqual(AppCommand.Find, command);
        Assert.IsFalse(manager.TryGetCommand(Keys.Control | Keys.F, out _));
    }

    [TestMethod]
    public void Validate_RejectsDuplicate()
    {
        var manager = Create();
        var conflict = manager.Validate(Keys.Control | Keys.Z, AppCommand.Find);

        Assert.AreEqual(ShortcutConflictKind.Duplicate, conflict.Kind);
        Assert.AreEqual(AppCommand.Undo, conflict.OtherCommand);
        Assert.IsFalse(manager.Set(AppCommand.Find, Keys.Control | Keys.Z));
    }

    [TestMethod]
    [DataRow(Keys.F)]
    [DataRow(Keys.A)]
    [DataRow(Keys.Shift | Keys.A)]
    [DataRow(Keys.Escape)]
    public void Validate_RejectsInvalid(Keys keys)
    {
        var manager = Create();
        Assert.AreEqual(ShortcutConflictKind.Invalid, manager.Validate(keys, AppCommand.Find).Kind);
    }

    [TestMethod]
    public void Validate_AllowsBareFunctionKey()
    {
        var manager = Create();
        Assert.AreEqual(ShortcutConflictKind.None, manager.Validate(Keys.F12, AppCommand.Find).Kind);
    }

    [TestMethod]
    public void RestoreDefault_ResetsToDefault()
    {
        var manager = Create();
        manager.Set(AppCommand.Find, Keys.Control | Keys.Alt | Keys.G);
        manager.RestoreDefault(AppCommand.Find);

        Assert.AreEqual("Ctrl+F", manager.GetShortcutText(AppCommand.Find));
    }

    [TestMethod]
    public void ResetAll_RemovesOverridesAndCleared()
    {
        var manager = Create();
        manager.Set(AppCommand.Find, Keys.Control | Keys.Alt | Keys.G);
        manager.Clear(AppCommand.Undo);
        manager.ResetAll();

        Assert.AreEqual("Ctrl+F", manager.GetShortcutText(AppCommand.Find));
        Assert.AreEqual("Ctrl+Z", manager.GetShortcutText(AppCommand.Undo));
    }

    [TestMethod]
    public void Changed_FiresOnMutation()
    {
        var manager = Create();
        var count = 0;
        manager.Changed += () => count++;
        manager.Set(AppCommand.Find, Keys.Control | Keys.Alt | Keys.G);
        manager.Clear(AppCommand.Undo);
        manager.RestoreDefault(AppCommand.Find);
        manager.ResetAll();

        Assert.AreEqual(4, count);
    }
}
