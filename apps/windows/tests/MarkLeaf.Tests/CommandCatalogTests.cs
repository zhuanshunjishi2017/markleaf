using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CommandCatalogTests
{
    [TestMethod]
    public void TryGetById_RejectsUnknownCommand()
    {
        Assert.IsFalse(CommandCatalog.TryGetById(0x7fff, out _));
    }

    [TestMethod]
    public void ShortcutCatalog_CoversRequiredDefaults()
    {
        var manager = new ShortcutManager(new ShortcutSettings());

        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.N, out var newDocument));
        Assert.AreEqual(AppCommand.NewDocument, newDocument);
        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.Shift | Keys.S, out var saveAs));
        Assert.AreEqual(AppCommand.SaveDocumentAs, saveAs);
        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.B, out var bold));
        Assert.AreEqual(AppCommand.ToggleBold, bold);
        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.Shift | Keys.C, out var painter));
        Assert.AreEqual(AppCommand.FormatPainter, painter);
        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.D6, out var heading6));
        Assert.AreEqual(AppCommand.SetHeading6, heading6);

        Assert.IsTrue(manager.TryGetCommand(Keys.Control | Keys.Alt | Keys.N, out var newText));
        Assert.AreEqual(AppCommand.NewPlainTextDocument, newText);
    }

    [TestMethod]
    public void NewDocumentKind_UsesExpectedExtensionsAndEditorTypes()
    {
        Assert.AreEqual("md", NewDocumentKind.Markdown.FileExtension);
        Assert.AreEqual("markdown", NewDocumentKind.Markdown.EditorDocumentType);
        Assert.AreEqual("txt", NewDocumentKind.PlainText.FileExtension);
        Assert.AreEqual("plainText", NewDocumentKind.PlainText.EditorDocumentType);
        Assert.AreEqual(NewDocumentKind.PlainText, NewDocumentKind.FromExtension("TXT"));
        Assert.AreEqual(NewDocumentKind.Markdown, NewDocumentKind.FromExtension("markdown"));
    }

    [TestMethod]
    [DataRow(Keys.Control | Keys.N, "Ctrl+N")]
    [DataRow(Keys.Control | Keys.Shift | Keys.S, "Ctrl+Shift+S")]
    [DataRow(Keys.Control | Keys.D6, "Ctrl+6")]
    [DataRow(Keys.F11, "F11")]
    [DataRow(Keys.Control | Keys.OemPeriod, "Ctrl+.")]
    [DataRow(Keys.Control | Keys.Oemcomma, "Ctrl+,")]
    public void ShortcutTextFormatter_RoundTrips(Keys keys, string text)
    {
        Assert.AreEqual(text, ShortcutTextFormatter.Format(keys));
        Assert.IsTrue(ShortcutTextFormatter.TryParse(text, out var parsed));
        Assert.AreEqual(keys, parsed);
    }
}
