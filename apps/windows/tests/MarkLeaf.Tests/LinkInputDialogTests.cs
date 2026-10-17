using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class LinkInputDialogTests
{
    [STATestMethod]
    [DataRow("#heading", true)]
    [DataRow("../notes/other.md", true)]
    [DataRow("https://example.com", true)]
    [DataRow("javascript:alert(1)", false)]
    [DataRow("data:text/html,unsafe", false)]
    public void ExistingAddressIsPrefilledAndValidated(string address, bool allowed)
    {
        LocTestHelper.EnsureInitialized();
        using var dialog = new LinkInputDialog(address);
        Assert.AreEqual(address, dialog.LinkAddress);
        Assert.AreEqual(allowed, ((Button)dialog.AcceptButton!).Enabled);
    }
}
