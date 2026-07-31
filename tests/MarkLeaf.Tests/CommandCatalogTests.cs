using MarkLeaf.Commands;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CommandCatalogTests
{
    [TestMethod]
    [DataRow(Keys.Control | Keys.N, AppCommand.NewDocument)]
    [DataRow(Keys.Control | Keys.Shift | Keys.S, AppCommand.SaveDocumentAs)]
    [DataRow(Keys.Control | Keys.Z, AppCommand.Undo)]
    [DataRow(Keys.Control | Keys.B, AppCommand.ToggleBold)]
    [DataRow(Keys.Control | Keys.D6, AppCommand.SetHeading6)]
    [DataRow(Keys.F11, AppCommand.ToggleFocusMode)]
    public void TryGetByShortcut_MapsRequiredKeys(Keys keys, AppCommand expected)
    {
        var found = CommandCatalog.TryGetByShortcut(keys, out var actual);

        Assert.IsTrue(found);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void TryGetById_RejectsUnknownCommand()
    {
        Assert.IsFalse(CommandCatalog.TryGetById(0x7fff, out _));
    }
}
