using System.Diagnostics;
using MarkLeaf.App;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Native.ModernMenus;
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
        if (options.IsExportCommand || !string.IsNullOrWhiteSpace(options.ExportConfigPath))
        {
            try
            {
                var exportInput = options.ExportInputPath;
                if (string.IsNullOrWhiteSpace(exportInput) && !string.IsNullOrWhiteSpace(options.ExportConfigPath))
                {
                    var exportConfig = CommandLineExportOptions.LoadAsync(options.ExportConfigPath).GetAwaiter().GetResult();
                    var configDirectory = Path.GetDirectoryName(options.ExportConfigPath);
                    exportInput = CommandLineExportOptions.ResolvePath(exportConfig.Input, configDirectory);
                }
                if (string.IsNullOrWhiteSpace(exportInput)) throw new InvalidDataException("Export input is required.");
                exportInput = CommandLineExportOptions.ResolvePath(exportInput);
                if (!File.Exists(exportInput)) throw new FileNotFoundException("Export input was not found.", exportInput);
                options = options with
                {
                    InitialDocumentPath = exportInput,
                    IsolatedFileWindow = true,
                };
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"MarkLeaf export configuration failed: {exception.Message}");
                Environment.ExitCode = 1;
                return;
            }
        }
        var paths = ApplicationPaths.Create(options.SettingsRoot);
        Directory.CreateDirectory(paths.DataDirectory);

        // 主实例互斥：两个主实例会争抢同一 WebView2 用户数据目录，导致环境
        // 创建失败/初始化竞态。孤立文件窗口（IsolatedFileWindow）按设计可
        // 多开，不参与锁。已存在主实例时把文件转发过去后退出。
        Mutex? primaryInstanceMutex = null;
        if (!options.IsolatedFileWindow)
        {
            primaryInstanceMutex = new Mutex(initiallyOwned: true, @"Global\MarkLeaf.PrimaryInstance", out var createdNew);
            if (!createdNew)
            {
                if (!string.IsNullOrWhiteSpace(options.InitialDocumentPath)
                    && FileOpenRouter.TryForwardAsync(options.InitialDocumentPath).GetAwaiter().GetResult())
                {
                    return;
                }

                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(options.InitialDocumentPath))
        {
            var currentProcessId = Environment.ProcessId;
            var existingInstances = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                .Count(process =>
                {
                    try { return process.Id != currentProcessId && !process.HasExited; }
                    catch { return false; }
                });
            if (!options.IsolatedFileWindow
                && existingInstances == 1
                && FileOpenRouter.TryForwardAsync(options.InitialDocumentPath).GetAwaiter().GetResult())
            {
                return;
            }
            if (existingInstances >= 2)
                options = options with { IsolatedFileWindow = true };
        }

        using var logger = new FileLogger(paths.LogDirectory);
        using var _mutexScope = primaryInstanceMutex;
        var settingsService = new JsonSettingsService(paths.SettingsFile, logger);

        try
        {
            var startupTimer = Stopwatch.StartNew();
            logger.Info("MarkLeaf starting.");
            logger.Info(
                $"Runtime: MarkLeaf {typeof(Program).Assembly.GetName().Version}; " +
                $".NET {Environment.Version}; {Environment.OSVersion.VersionString}.");
            var stylesDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Styles");

            // These operations only perform independent file reads.  Running them
            // concurrently avoids making first-paint wait for each CSS file set in
            // sequence (which is especially noticeable on a cold disk/cache).
            var stylesTask = Task.Run(() => StyleService.Initialize(stylesDir));
            var colorThemesTask = Task.Run(() => ColorThemeService.Initialize(stylesDir));
            var settingsFileExisted = File.Exists(paths.SettingsFile);
            var settingsTask = settingsService.LoadAsync();
            Task.WhenAll(stylesTask, colorThemesTask, settingsTask).GetAwaiter().GetResult();

            var settings = settingsTask.GetAwaiter().GetResult();
            logger.Info($"Startup file initialization completed in {startupTimer.ElapsedMilliseconds} ms.");
            logger.Info($"Styles loaded: {StyleService.Styles.Count} from Resources/Styles.");
            logger.Info($"Color themes loaded: {ColorThemeService.All.Count} from Resources/Styles.");
            var uiLanguage = settings.General.UiLanguage ?? "";
            var localesDir = Path.Combine(AppContext.BaseDirectory, "Resources", "Locales");
            Loc.Initialize(localesDir, uiLanguage);
            logger.Info($"Locales initialized: {uiLanguage} from Resources/Locales ({startupTimer.ElapsedMilliseconds} ms).");

            // 在任何窗口创建前设置进程级颜色模式，确保 HMENU 深色渲染就绪。
            DarkModeService.Initialize();

            // Overlap WebView2 process/profile startup with construction of the
            // native control tree, after the small files needed for first paint
            // are already in memory. InitializeAsync consumes this same task.
            _ = EditorHostController.PrewarmEnvironmentAsync(paths.WebView2UserDataDirectory);

            using var form = new MainForm(options, paths, settings, settingsService, logger, settingsFileExisted);
            using var fileOpenRouter = FileOpenRouter.TryStartPrimary(
                path =>
                {
                    if (form.IsDisposed) return Task.CompletedTask;
                    form.BeginInvoke(async () => await form.OpenDocumentFromExternalRequestAsync(path));
                    return Task.CompletedTask;
                },
                out var primaryRouter)
                ? primaryRouter
                : null;
            Application.Run(form);
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
