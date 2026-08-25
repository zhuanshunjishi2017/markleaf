using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EncodingChangeDialogTests
{
    [TestMethod]
    public void EncodingChangeChoice_ContainsExpectedOptions()
    {
        Assert.HasCount(3, Enum.GetValues<EncodingChangeChoice>());
        Assert.IsTrue(Enum.IsDefined(typeof(EncodingChangeChoice), EncodingChangeChoice.DirectRead));
        Assert.IsTrue(Enum.IsDefined(typeof(EncodingChangeChoice), EncodingChangeChoice.ConvertEncoding));
    }
}
