using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using MarkLeaf.Native;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class PreferencesDialogThemeTests
{
    [STATestMethod]
    public void DetachedPreferencePagesHaveThemeColorsBeforeTheWindowIsShown()
    {
        InitializeStyles();
        var original = ColorThemeService.ActiveThemeId;
        try
        {
            foreach (var dark in new[] { false, true })
            {
                ColorThemeService.SetActiveTheme(dark ? "dark" : "default-light");
                using var dialog = new PreferencesDialog(new AppSettings());
                Assert.IsFalse(dialog.IsHandleCreated);
                foreach (var name in new[] { "_defaultDirectoryTextBox", "_cjkFontTextBox", "_westernFontTextBox" })
                {
                    var input = Field<TextBox>(dialog, name);
                    Assert.AreEqual(dark, input.BackColor.GetBrightness() < .5f, $"Unthemed initial background: {name}");
                    Assert.IsGreaterThan(.4f, Math.Abs(input.ForeColor.GetBrightness() - input.BackColor.GetBrightness()),
                        $"Unthemed initial text: {name}");
                }
            }
        }
        finally { ColorThemeService.SetActiveTheme(original); }
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void DarkTextBoxFramesStayDarkWhenFocusedAndAfterHandleRecreation()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpi = new DpiScope();
        Application.EnableVisualStyles();
        InitializeStyles();
        var original = ColorThemeService.ActiveThemeId;
        try
        {
            ColorThemeService.SetActiveTheme("dark");
            DarkModeService.Initialize();
            DarkModeService.Apply(true);
            using var preferences = new PreferencesDialog(new AppSettings());
            using var export = new ExportDialog("test.md", "test", "serif", StyleService.GetAllStyles(),
                Path.GetTempPath(), _ => Task.FromResult(Array.Empty<byte>()), _ => Task.FromResult(""), ExportDialogMode.Html);
            foreach (var (dialog, field, tab) in new (Form, string, int)[]
            {
                (preferences, "_defaultDirectoryTextBox", 3),
                (export, "_htmlHeader", 1),
                (export, "_htmlFooter", 1),
            })
            {
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.TopMost = true;
                dialog.Show();
                dialog.GetType().GetMethod("SwitchTabPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [tab]);
                SetForegroundWindow(dialog.Handle);
                var input = Field<TextBox>(dialog, field);
                input.Text = "selection stays native";
                input.Select(2, 4);
                var size = input.Size;
                var clientSize = input.ClientSize;
                var borderStyle = input.BorderStyle;
                for (var step = 0; step < 5; step++)
                {
                    if (step == 0) ((Button)dialog.CancelButton!).Focus();
                    else input.Focus();
                    if (step == 2)
                        typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(input, null);
                    input.Enabled = step != 3;
                    input.ReadOnly = step == 4;
                    dialog.Refresh();
                    var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 100) { Application.DoEvents(); Thread.Sleep(5); }
                    using var image = new Bitmap(input.Width, input.Height);
                    using (var graphics = Graphics.FromImage(image))
                        graphics.CopyFromScreen(input.Parent!.PointToScreen(input.Location), Point.Empty, image.Size);
                    if (Environment.GetEnvironmentVariable("MARKLEAF_EXPORT_QA_OUTPUT") is { } output)
                    {
                        Directory.CreateDirectory(output);
                        image.Save(Path.Combine(output, $"{field}-{step}.png"));
                    }
                    Assert.IsLessThan(.3f, image.GetPixel(image.Width / 2, image.Height / 2).GetBrightness(),
                        "The captured rectangle must contain the actual dark edit control");
                    var bright = 0;
                    for (var x = image.Width / 4; x < image.Width * 3 / 4; x++)
                    {
                        if (image.GetPixel(x, 0).GetBrightness() > .8f) bright++;
                        if (image.GetPixel(x, 1).GetBrightness() > .8f) bright++;
                        if (image.GetPixel(x, image.Height - 1).GetBrightness() > .8f) bright++;
                        if (image.GetPixel(x, image.Height - 2).GetBrightness() > .8f) bright++;
                    }
                    for (var y = image.Height / 4; y < image.Height * 3 / 4; y++)
                    {
                        if (image.GetPixel(0, y).GetBrightness() > .8f) bright++;
                        if (image.GetPixel(image.Width - 1, y).GetBrightness() > .8f) bright++;
                    }
                    Assert.IsLessThan(image.Width / 4, bright, $"White native edit frame: {field}, step {step}");
                    Assert.AreEqual(size, input.Size);
                    Assert.AreEqual(clientSize, input.ClientSize);
                    Assert.AreEqual(borderStyle, input.BorderStyle);
                    Assert.AreEqual("selection stays native", input.Text);
                    Assert.AreEqual(2, input.SelectionStart);
                    Assert.AreEqual(4, input.SelectionLength);
                }
                dialog.Hide();
            }
        }
        finally
        {
            ColorThemeService.SetActiveTheme(original);
            DarkModeService.Apply(ColorThemeService.IsActiveThemeDark());
        }
    }

    private static void InitializeStyles()
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(AppContext.BaseDirectory, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    private sealed class DpiScope : IDisposable
    {
        private readonly nint _previous = SetThreadDpiAwarenessContext(-4);
        public void Dispose() => SetThreadDpiAwarenessContext(_previous);
    }
}
