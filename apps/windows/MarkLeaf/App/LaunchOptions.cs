namespace MarkLeaf.App;

internal sealed record LaunchOptions(
    string? SettingsRoot,
    int? AutoCloseMilliseconds,
    string? WindowReportPath,
    int? LayoutDpiOverride,
    string? SmokeCommand,
    string? CommandReportPath,
    string? EditorSmokeReportPath,
    string? EditorWebRoot,
    string? EditorStateReportPath,
    string? EditorCommandSmoke,
    string? EditorCommandReportPath,
    string? DocumentSmokeInputPath,
    string? DocumentSmokeOutputPath,
    string? DocumentSmokeReportPath,
    string? InitialDocumentPath,
    string? DocumentStatePath,
    int? InitialWindowLeft,
    int? InitialWindowTop,
    int? InitialWindowWidth,
    int? InitialWindowHeight,
    bool SmokeCrashExit,
    bool IsolatedFileWindow)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? settingsRoot = null;
        int? autoCloseMilliseconds = null;
        string? windowReportPath = null;
        int? layoutDpiOverride = null;
        string? smokeCommand = null;
        string? commandReportPath = null;
        string? editorSmokeReportPath = null;
        string? editorWebRoot = null;
        string? editorStateReportPath = null;
        string? editorCommandSmoke = null;
        string? editorCommandReportPath = null;
        string? documentSmokeInputPath = null;
        string? documentSmokeOutputPath = null;
        string? documentSmokeReportPath = null;
        string? initialDocumentPath = null;
        string? documentStatePath = null;
        int? initialWindowLeft = null;
        int? initialWindowTop = null;
        int? initialWindowWidth = null;
        int? initialWindowHeight = null;
        bool smokeCrashExit = false;
        bool isolatedFileWindow = false;

        for (var index = 0; index < args.Length - 1; index++)
        {
            switch (args[index])
            {
                case "--settings-root":
                    settingsRoot = args[++index];
                    break;
                case "--auto-close-ms" when int.TryParse(args[index + 1], out var milliseconds):
                    autoCloseMilliseconds = milliseconds;
                    index++;
                    break;
                case "--window-report":
                    windowReportPath = Path.GetFullPath(args[++index]);
                    break;
                case "--layout-dpi" when int.TryParse(args[index + 1], out var dpi):
                    layoutDpiOverride = Math.Clamp(dpi, 96, 192);
                    index++;
                    break;
                case "--smoke-command":
                    smokeCommand = args[++index];
                    break;
                case "--command-report":
                    commandReportPath = Path.GetFullPath(args[++index]);
                    break;
                case "--editor-smoke-report":
                    editorSmokeReportPath = Path.GetFullPath(args[++index]);
                    break;
                case "--editor-web-root":
                    editorWebRoot = Path.GetFullPath(args[++index]);
                    break;
                case "--editor-state-report":
                    editorStateReportPath = Path.GetFullPath(args[++index]);
                    break;
                case "--editor-command-smoke":
                    editorCommandSmoke = args[++index];
                    break;
                case "--editor-command-report":
                    editorCommandReportPath = Path.GetFullPath(args[++index]);
                    break;
                case "--document-smoke-input":
                    documentSmokeInputPath = Path.GetFullPath(args[++index]);
                    break;
                case "--document-smoke-output":
                    documentSmokeOutputPath = Path.GetFullPath(args[++index]);
                    break;
                case "--document-smoke-report":
                    documentSmokeReportPath = Path.GetFullPath(args[++index]);
                    break;
                case "--open-document":
                    initialDocumentPath = Path.GetFullPath(args[++index]);
                    break;
                case "--open-document-state":
                    documentStatePath = Path.GetFullPath(args[++index]);
                    break;
                case "--window-left" when int.TryParse(args[index + 1], out var windowLeft):
                    initialWindowLeft = windowLeft;
                    index++;
                    break;
                case "--window-top" when int.TryParse(args[index + 1], out var windowTop):
                    initialWindowTop = windowTop;
                    index++;
                    break;
                case "--window-width" when int.TryParse(args[index + 1], out var windowWidth):
                    initialWindowWidth = windowWidth;
                    index++;
                    break;
                case "--window-height" when int.TryParse(args[index + 1], out var windowHeight):
                    initialWindowHeight = windowHeight;
                    index++;
                    break;
                case "--smoke-crash-exit":
                    smokeCrashExit = true;
                    break;
                case "--isolated-file-window":
                    isolatedFileWindow = true;
                    break;
            }
        }

        return new LaunchOptions(
            settingsRoot,
            autoCloseMilliseconds,
            windowReportPath,
            layoutDpiOverride,
            smokeCommand,
            commandReportPath,
            editorSmokeReportPath,
            editorWebRoot,
            editorStateReportPath,
            editorCommandSmoke,
            editorCommandReportPath,
            documentSmokeInputPath,
            documentSmokeOutputPath,
            documentSmokeReportPath,
            initialDocumentPath,
            documentStatePath,
            initialWindowLeft,
            initialWindowTop,
            initialWindowWidth,
            initialWindowHeight,
            smokeCrashExit,
            isolatedFileWindow);
    }
}
