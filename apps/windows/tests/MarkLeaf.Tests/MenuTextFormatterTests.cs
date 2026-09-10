using MarkLeaf.Native;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class MenuTextFormatterTests
{
    [TestMethod]
    public void Format_HidesKeyboardShortcutAfterTab()
    {
        var result = MenuTextFormatter.Format("保存(&S)\tCtrl+S", false, true, "zh-CN");

        Assert.AreEqual("保存(&S)", result);
    }

    [TestMethod]
    public void Format_HidesParenthesizedMnemonicForNonEnglishLanguage()
    {
        var result = MenuTextFormatter.Format("保存(&S)\tCtrl+S", true, false, "zh-CN");

        Assert.AreEqual("保存\tCtrl+S", result);
    }

    [TestMethod]
    public void Format_KeepsMnemonicForEnglishLanguage()
    {
        var result = MenuTextFormatter.Format("&Save\tCtrl+S", true, false, "en-US");

        Assert.AreEqual("&Save\tCtrl+S", result);
    }
}
