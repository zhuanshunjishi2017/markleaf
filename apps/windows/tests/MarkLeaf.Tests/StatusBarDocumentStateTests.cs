using System.Reflection;
using MarkLeaf.App;
using MarkLeaf.Documents;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class StatusBarDocumentStateTests
{
    [STATestMethod]
    public void UserReadOnlyDisablesEncodingAndNewLineControls()
    {
        WithForm(form =>
        {
            Field("_document").SetValue(form, new MarkdownDocument { IsUserReadOnly = true });
            InvokeRefresh(form);

            Assert.IsFalse(EncodingEnabled(form));
            Assert.IsFalse(NewLineEnabled(form));
        });
    }

    [STATestMethod]
    public void FileReadOnlyDisablesEncodingAndNewLineControls()
    {
        WithForm(form =>
        {
            Field("_document").SetValue(form, new MarkdownDocument { IsReadOnly = true });
            InvokeRefresh(form);

            Assert.IsFalse(EncodingEnabled(form));
            Assert.IsFalse(NewLineEnabled(form));
        });
    }

    private static FieldInfo Field(string name) =>
        typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static ToolStripItem Item(MainForm form, string name) =>
        (ToolStripItem)Field(name).GetValue(form)!;

    private static bool EncodingEnabled(MainForm form) => Item(form, "_encodingLabel").Enabled;

    private static bool NewLineEnabled(MainForm form) => Item(form, "_newLineLabel").Enabled;

    private static void InvokeRefresh(MainForm form) =>
        typeof(MainForm).GetMethod("RefreshPersistentStatusBar",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);

    private static void WithForm(Action<MainForm> test)
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-status-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = ApplicationPaths.Create(directory);
            using var logger = new FileLogger(paths.LogDirectory);
            using var form = new MainForm(LaunchOptions.Parse([]), paths, new AppSettings(),
                new JsonSettingsService(paths.SettingsFile, logger), logger);
            test(form);
        }
        finally { Directory.Delete(directory, true); }
    }
}
