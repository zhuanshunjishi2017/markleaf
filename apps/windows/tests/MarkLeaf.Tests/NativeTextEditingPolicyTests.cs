using System.Reflection;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class NativeTextEditingPolicyTests
{
    [STATestMethod]
    public void CopyShortcutCopiesSelectedTextInANativeTextBox()
    {
        var previousClipboard = Clipboard.GetDataObject();
        WithForm(form =>
        {
            var textBox = new TextBox { Parent = form, Text = "native selected text" };
            _ = textBox.Handle;
            textBox.SelectAll();

            var handled = (bool)InvokeHandleShortcut(form, Keys.Control | Keys.C, textBox)!;

            Assert.IsTrue(handled);
            Assert.AreEqual("native selected text", Clipboard.GetText());
        });
        RestoreClipboard(previousClipboard);
    }

    [STATestMethod]
    public void PastePlainTextShortcutIsMappedInsideANativeTextBox()
    {
        WithForm(form =>
        {
            var textBox = new TextBox { Parent = form };

            Assert.AreEqual(NativeTextCommand.PastePlainText,
                NativeTextEditingPolicy.MapShortcut(Keys.Control | Keys.Shift | Keys.V));
            Assert.IsNull(NativeTextEditingPolicy.MapShortcut(Keys.Control | Keys.N));
            Assert.IsNull(NativeTextEditingPolicy.FindFocusedText(form, focused: null));
        });
    }

    [STATestMethod]
    public void PastePlainTextShortcutReplacesSelectionWithClipboardText()
    {
        var previousClipboard = Clipboard.GetDataObject();
        WithForm(form =>
        {
            var textBox = new TextBox { Parent = form, Text = "before selected after" };
            _ = textBox.Handle;
            var start = textBox.Text.IndexOf("selected", StringComparison.Ordinal);
            textBox.Select(start, "selected".Length);
            Clipboard.SetText("replacement");

            var handled = (bool)InvokeHandleShortcut(
                form, Keys.Control | Keys.Shift | Keys.V, textBox)!;

            Assert.IsTrue(handled);
            Assert.AreEqual("before replacement after", textBox.Text);
        });
        RestoreClipboard(previousClipboard);
    }

    private static object? InvokeHandleShortcut(MainForm form, Keys keys, Control focused) =>
        typeof(MainForm).GetMethod("TryHandleNativeTextShortcut",
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, [keys, focused]);

    private static void WithForm(Action<MainForm> test)
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-native-text-" + Guid.NewGuid().ToString("N"));
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

    private static void RestoreClipboard(IDataObject? dataObject)
    {
        if (dataObject is null) Clipboard.Clear();
        else Clipboard.SetDataObject(dataObject, true);
    }
}
