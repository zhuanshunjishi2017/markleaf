using MarkLeaf.Services;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class AppVersionDisplayTests
{
    [TestMethod]
    public void Format_UsesEnglishVersionAndBuildLabelsForEveryLocale()
    {
        Assert.AreEqual(
            "Version 1.3.1 (Build 42)",
            AppVersionDisplay.Format("1.3.1", "42"));
    }
}
