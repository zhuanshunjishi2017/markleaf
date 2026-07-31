namespace MarkLeaf.Prototype;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = PrototypeOptions.Parse(args);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => HandleFatalException(options, eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            HandleFatalException(options, eventArgs.ExceptionObject as Exception ?? new Exception("Unknown fatal error."));

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(options));
        }
        catch (Exception exception)
        {
            HandleFatalException(options, exception);
        }
    }

    private static void HandleFatalException(PrototypeOptions options, Exception exception)
    {
        Environment.ExitCode = 1;

        if (options.IsSmokeTest && options.SmokeTestOutputPath is not null)
        {
            var logPath = options.SmokeTestOutputPath + ".error.log";
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, exception.ToString());
            Application.Exit();
            return;
        }

        MessageBox.Show(
            exception.Message,
            "MarkLeaf 原型启动失败",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
