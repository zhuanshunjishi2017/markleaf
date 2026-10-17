using MarkLeaf.App;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class AppearanceSettingsNoticeTests
{
    [TestMethod]
    public void CapturesAndAppliesSharedAppearancePreferences()
    {
        var source = new AppSettings();
        source.MarkdownStyle = "github";
        source.ColorTheme = "ocean";
        source.Appearance.FollowSystemColorMode = true;
        source.Appearance.ShowCodeHighlight = false;
        source.Appearance.DefaultLightThemeId = "day";
        source.Appearance.DefaultDarkThemeId = "night";

        var notice = AppearanceSettingsNotice.From(source);
        var target = new AppSettings();
        notice.ApplyTo(target);

        Assert.AreEqual("github", target.MarkdownStyle);
        Assert.AreEqual("ocean", target.ColorTheme);
        Assert.IsTrue(target.Appearance.FollowSystemColorMode);
        Assert.IsFalse(target.Appearance.ShowCodeHighlight);
        Assert.AreEqual("day", target.Appearance.DefaultLightThemeId);
        Assert.AreEqual("night", target.Appearance.DefaultDarkThemeId);
    }
}
