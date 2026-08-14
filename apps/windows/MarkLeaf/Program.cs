using MarkLeaf.App;
using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI;

namespace MarkLeaf;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var options = LaunchOptions.Parse(args);
        var paths = ApplicationPaths.Create(options.SettingsRoot);
        Directory.CreateDirectory(paths.DataDirectory);

        using var logger = new FileLogger(paths.LogDirectory);
        var settingsService = new JsonSettingsService(paths.SettingsFile, logger);

        try
        {
            logger.Info("MarkLeaf starting.");
            logger.Info(
                $"Runtime: MarkLeaf {typeof(Program).Assembly.GetName().Version}; " +
                $".NET {Environment.Version}; {Environment.OSVersion.VersionString}.");
            var stylesDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Styles");
            StyleService.Initialize(stylesDir);
            logger.Info($"Styles loaded: {StyleService.Styles.Count} from Resources/Styles.");
            ColorThemeService.Initialize(stylesDir);
            logger.Info($"Color themes loaded: {ColorThemeService.All.Count} from Resources/Styles.");
            var settings = settingsService.LoadAsync().GetAwaiter().GetResult();
            var uiLanguage = settings.General.UiLanguage ?? "";
            var localesDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Locales");
            Loc.Initialize(localesDir, uiLanguage);
            logger.Info($"Locales initialized: {uiLanguage} from Resources/Locales.");

            // 在任何窗口创建前设置进程级颜色模式，确保 HMENU 深色渲染就绪。
            DarkModeService.Initialize();

            Application.Run(new MainForm(options, paths, settings, settingsService, logger));
            logger.Info("MarkLeaf stopped normally.");
        }
        catch (Exception exception)
        {
            logger.Error("MarkLeaf failed during startup.", exception);
            var startupMessage = MarkLeaf.Services.Loc.Get("startup.failed");
            MessageBox.Show(
                $"{startupMessage}\r\n\r\n{exception.Message}",
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
