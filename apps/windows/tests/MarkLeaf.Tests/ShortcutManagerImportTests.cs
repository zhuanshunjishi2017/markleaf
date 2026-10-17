using MarkLeaf.Commands;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ShortcutManagerImportTests
{
    [TestMethod]
    public void ImportSettings_ReplacesEffectiveBindingsWithoutRaisingChanged()
    {
        var model = new ShortcutSettings();
        var manager = new ShortcutManager(model);
        var changed = 0;
        manager.Changed += () => changed++;

        var imported = new ShortcutSettings();
        imported.Overrides[nameof(AppCommand.ToggleBold)] = "Ctrl+Alt+B";
        imported.Cleared.Add(nameof(AppCommand.ToggleItalic));
        manager.ImportSettings(imported);

        Assert.IsTrue(manager.TryGetCommand(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.B, out var boldCommand));
        Assert.AreEqual(AppCommand.ToggleBold, boldCommand);
        Assert.IsNull(manager.GetShortcutText(AppCommand.ToggleItalic));
        Assert.AreEqual(0, changed);
        Assert.HasCount(1, model.Overrides);
        Assert.AreEqual("Ctrl+Alt+B", model.Overrides[nameof(AppCommand.ToggleBold)]);
        Assert.HasCount(1, model.Cleared);
        Assert.Contains(nameof(AppCommand.ToggleItalic), model.Cleared);
    }
}
