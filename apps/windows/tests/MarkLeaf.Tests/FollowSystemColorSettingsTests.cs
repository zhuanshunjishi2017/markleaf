using System.Reflection;
using System.Text.Json;
using MarkLeaf.App;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class FollowSystemColorSettingsTests
{
    [STATestMethod]
    public void TogglingFollowSystemPersistsImmediately()
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-follow-system-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = ApplicationPaths.Create(directory);
            using var logger = new FileLogger(paths.LogDirectory);
            using var form = new MainForm(LaunchOptions.Parse([]), paths, new AppSettings(),
                new JsonSettingsService(paths.SettingsFile, logger), logger);

            typeof(MainForm).GetMethod("ToggleFollowSystemColorMode",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);

            Assert.IsTrue(((AppSettings)typeof(MainForm).GetField("_settings",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!).Appearance.FollowSystemColorMode);
            using var json = JsonDocument.Parse(File.ReadAllText(paths.SettingsFile));
            Assert.IsTrue(json.RootElement.GetProperty("appearance").GetProperty("followSystemColorMode").GetBoolean());
        }
        finally { Directory.Delete(directory, true); }
    }
}
