using MarkLeaf.Commands;
using MarkLeaf.Documents;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class DocumentMenuPolicyTests
{
    [TestMethod]
    public void RepeatedOpenPreservesBuffer()
    {
        var document = new MarkdownDocument
        {
            FilePath = @"C:\work\Note.md", Markdown = "unsaved buffer", IsDirty = true,
            IsUserReadOnly = true, Revision = 19, ScrollTop = 81,
        };
        var existing = DocumentMenuPolicy.FindOpenDocument([document], @"C:\work\sub\..\NOTE.md");
        Assert.AreSame(document, existing);
        Assert.IsNotNull(existing);
        Assert.AreEqual("unsaved buffer", existing.Markdown);
        Assert.IsTrue(existing.IsDirty);
        Assert.IsTrue(existing.IsUserReadOnly);
        Assert.AreEqual(19L, existing.Revision);
        Assert.AreEqual(81d, existing.ScrollTop);
        Assert.IsNull(DocumentMenuPolicy.FindOpenDocument([document], @"C:\work\Other.md"));
    }

    [TestMethod]
    public void SaveEligibility()
    {
        Assert.IsTrue(DocumentMenuPolicy.CanSave(true, true, false, false, false));
        Assert.IsFalse(DocumentMenuPolicy.CanSave(true, true, false, true, false));
        Assert.IsTrue(DocumentMenuPolicy.CanSave(true, true, true, true, false));
        Assert.IsFalse(DocumentMenuPolicy.CanSave(true, true, true, false, true));
        Assert.IsFalse(DocumentMenuPolicy.CanSave(false, true, true, false, false));
        Assert.IsFalse(DocumentMenuPolicy.CanSave(true, false, true, false, false));
        var cleanNamed = new CommandContext(true, true, DocumentHasPath: true);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SaveDocument, cleanNamed).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SaveDocument,
            cleanNamed with { DocumentDirty = true }).IsEnabled);
    }

    [TestMethod]
    public void SaveAs_RemainsAvailableForReadOnlyDocuments()
    {
        var readOnly = new CommandContext(DocumentAvailable: true, EditorReady: true, ReadOnlyMode: true);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SaveDocumentAs, readOnly).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SaveDocument, readOnly).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SaveDocumentAs,
            readOnly with { EditorReady = false }).IsEnabled);
    }
}
