using System.Reflection;
using System.Windows.Forms;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class WorkspaceRootMenuDispatchTests
{
    [STATestMethod]
    public void CopyPathCopiesTheRootThatOpenedTheMenu()
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-root-menu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousClipboard = Clipboard.GetDataObject();
        try
        {
            var paths = ApplicationPaths.Create(directory);
            using var logger = new FileLogger(paths.LogDirectory);
            using var form = new MainForm(LaunchOptions.Parse([]), paths, new AppSettings(),
                new JsonSettingsService(paths.SettingsFile, logger), logger);
            // A menu belongs to its opening target, not a subsequently changed workspace.
            typeof(MainForm).GetField("_workspaceRoot", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(form, Path.Combine(directory, "different-root"));
            Clipboard.SetText("unchanged");
            var task = (Task)typeof(MainForm).GetMethod("ExecuteWorkspaceFolderMenuCommandAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [AppCommand.WorkspaceCopyPath, directory])!;
            task.GetAwaiter().GetResult();
            Assert.AreEqual(directory, Clipboard.GetText());
        }
        finally
        {
            if (previousClipboard is null) Clipboard.Clear();
            else Clipboard.SetDataObject(previousClipboard, true);
            Directory.Delete(directory, true);
        }
    }
}
