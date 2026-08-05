using MarkLeaf.App;
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
            StyleService.Initialize(Path.Combine(AppContext.BaseDirectory, "Resources", "Styles"));
            logger.Info($"Styles loaded: {StyleService.Styles.Count} from Resources/Styles.");
            var settings = settingsService.LoadAsync().GetAwaiter().GetResult();

            Application.Run(new MainForm(options, paths, settings, settingsService, logger));
            logger.Info("MarkLeaf stopped normally.");
        }
        catch (Exception exception)
        {
            logger.Error("MarkLeaf failed during startup.", exception);
            MessageBox.Show(
                "MarkLeaf 无法启动。详细信息已写入日志。\r\n\r\n" + exception.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
