using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class SharedSettingsNoticeTests
{
    [TestMethod]
    public void CapturesAndAppliesAllSharedGroups()
    {
        var source = new AppSettings();
        source.File.AutoSaveEnabled = true;
        source.Editor.SourceFontSize = 22;
        source.MarkdownStyle = "github";
        source.ColorTheme = "ocean";
        source.Appearance.FollowSystemColorMode = true;
        source.Appearance.ShowCodeHighlight = false;
        source.Appearance.DefaultLightThemeId = "day";
        source.Appearance.DefaultDarkThemeId = "night";
        source.General.UiLanguage = "en-US";
        source.Image.UseRelativePaths = false;
        source.Export.Format = "html";
        source.Shortcut.Overrides[nameof(AppCommand.ToggleBold)] = "Ctrl+Alt+B";
        source.CodeFormatter.SqlDialect = "postgres";

        var notice = SharedSettingsNotice.From(source);
        var target = new AppSettings();
        notice.ApplySharedTo(target);

        Assert.IsTrue(target.File.AutoSaveEnabled);
        Assert.AreEqual(22, target.Editor.SourceFontSize);
        Assert.AreEqual("github", target.MarkdownStyle);
        Assert.AreEqual("ocean", target.ColorTheme);
        Assert.IsTrue(target.Appearance.FollowSystemColorMode);
        Assert.IsFalse(target.Appearance.ShowCodeHighlight);
        Assert.AreEqual("day", target.Appearance.DefaultLightThemeId);
        Assert.AreEqual("night", target.Appearance.DefaultDarkThemeId);
        Assert.AreEqual("en-US", target.General.UiLanguage);
        Assert.IsFalse(target.Image.UseRelativePaths);
        Assert.AreEqual("html", target.Export.Format);
        Assert.AreEqual("Ctrl+Alt+B", target.Shortcut.Overrides[nameof(AppCommand.ToggleBold)]);
        Assert.AreEqual("postgres", target.CodeFormatter.SqlDialect);
    }
}
