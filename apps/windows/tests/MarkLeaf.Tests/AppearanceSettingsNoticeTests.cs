using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Services.Settings;
using System.Text.Json;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class SharedSettingsNoticeTests
{
    [TestMethod]
    public void SharedAppearancePreservesTargetWindowZoom()
    {
        var source = new AppSettings();
        source.Appearance.ZoomPercent = 150;
        source.Appearance.ShowCodeHighlight = false;
        var target = new AppSettings();
        target.Appearance.ZoomPercent = 75;
        var notice = SharedSettingsNotice.From(source);
        notice.ApplyTo(target);
        Assert.AreEqual(75, target.Appearance.ZoomPercent);
        Assert.IsFalse(target.Appearance.ShowCodeHighlight);
        Assert.DoesNotContain("ZoomPercent", JsonSerializer.Serialize(notice), StringComparison.OrdinalIgnoreCase);
        var decoded = JsonSerializer.Deserialize<SharedSettingsNotice>(JsonSerializer.Serialize(notice))!;
        target.Appearance.ZoomPercent = 90;
        decoded.ApplyTo(target);
        Assert.AreEqual(90, target.Appearance.ZoomPercent);
        Assert.IsFalse(target.Appearance.ShowCodeHighlight);
    }

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
        target.CodeFormatter.SqlDialect = "mysql";
        target.CodeFormatter.ToolPaths["kept"] = "C:\\Tools\\kept.exe";
        var targetCodeFormatter = target.CodeFormatter;
        var targetShortcut = target.Shortcut;
        notice.ApplyTo(target);

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
        Assert.AreSame(targetShortcut, target.Shortcut);
        Assert.AreSame(targetCodeFormatter, target.CodeFormatter);
        Assert.IsFalse(target.CodeFormatter.ToolPaths.ContainsKey("kept"));

        var serialized = JsonSerializer.Serialize(notice.Shared);
        Assert.DoesNotContain("\"mainWindow\"", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"workspace\"", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
