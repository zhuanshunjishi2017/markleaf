using System.Reflection;
using System.Runtime.InteropServices;
using MarkLeaf.Native;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class UtilityDialogThemeTests
{
    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow("fonts", true)]
    [DataRow("fonts", false)]
    [DataRow("formatter", true)]
    [DataRow("formatter", false)]
    public void VisibleUtilityDialogsUseReadableHeadersAndInputFrames(string kind, bool dark)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpi = new DpiScope();
        Application.EnableVisualStyles();
        InitializeStyles();
        var original = ColorThemeService.ActiveThemeId;
        try
        {
            ColorThemeService.SetActiveTheme(dark ? "dark" : "default-light");
            DarkModeService.Initialize();
            DarkModeService.Apply(dark);
            using Form dialog = kind == "fonts" ? new OptionalFontsDialog()
                : new CodeFormatterSettingsDialog(new CodeFormatterSettings(), () => {});
            dialog.TopMost = true;
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Show();
            SetForegroundWindow(dialog.Handle);
            dialog.Refresh();
            Application.DoEvents();
            DwmFlush();
            if (kind == "fonts")
            {
                var grid = Field<DataGridView>(dialog, "_grid");
                var header = grid.GetCellDisplayRectangle(1, -1, false);
                using var image = Capture(grid);
                Assert.AreEqual(dark, image.GetPixel(header.Right - 10, header.Top + header.Height / 2).GetBrightness() < .5f,
                    "The actual header must use the dialog theme");
                Assert.IsGreaterThan(grid.Columns["installed"]!.Width * 1.5, (double)grid.Columns["font"]!.Width,
                    "Font names need more space than the short installed state");
                Assert.AreEqual(grid.Rows[0].Cells[1].Value?.ToString(), grid.Rows[0].Cells[1].ToolTipText);
            }
            else
            {
                var list = Field<ListView>(dialog, "_toolList");
                var headerWindow = SendMessage(list.Handle, 0x101F, 0, 0); // LVM_GETHEADER
                Assert.AreNotEqual((nint)0, headerWindow);
                Assert.IsTrue(GetWindowRect(headerWindow, out var header));
                using var image = new Bitmap(header.Right - header.Left, header.Bottom - header.Top);
                using (var graphics = Graphics.FromImage(image))
                    graphics.CopyFromScreen(new Point(header.Left, header.Top), Point.Empty, image.Size);
                Assert.AreEqual(dark, image.GetPixel(image.Width - 10, image.Height / 2).GetBrightness() < .5f,
                    "The actual native formatter header must follow the application theme");
                list.Items[0].Selected = true;
                Application.DoEvents();
                if (dark)
                {
                    using var path = Capture(Field<TextBox>(dialog, "_pathTextBox"));
                    var bright = 0;
                    for (var x = 4; x < path.Width - 4; x++)
                    {
                        if (path.GetPixel(x, 0).GetBrightness() > .8f) bright++;
                        if (path.GetPixel(x, path.Height - 1).GetBrightness() > .8f) bright++;
                    }
                    Assert.IsLessThan(path.Width / 4, bright, "The formatter's path field must not have a white frame");
                }
            }
            if (Environment.GetEnvironmentVariable("MARKLEAF_EXPORT_QA_OUTPUT") is { } output)
            {
                Directory.CreateDirectory(output);
                using var image = Capture(dialog);
                image.Save(Path.Combine(output, $"{kind}-{dark}.png"));
            }
        }
        finally
        {
            ColorThemeService.SetActiveTheme(original);
            DarkModeService.Apply(ColorThemeService.IsActiveThemeDark());
        }
    }

    private static Bitmap Capture(Control control)
    {
        GetWindowRect(control.Handle, out var rect);
        var image = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using var graphics = Graphics.FromImage(image);
        graphics.CopyFromScreen(new Point(rect.Left, rect.Top), Point.Empty, image.Size);
        return image;
    }

    [STATestMethod]
    [DataRow("dark", true)]
    [DataRow("default-light", false)]
    public void FontListIsThemedBeforeDisplayIncludingHeadersAndSelection(string theme, bool dark)
    {
        InitializeStyles();
        var original = ColorThemeService.ActiveThemeId;
        try
        {
            ColorThemeService.SetActiveTheme(theme);
            using var dialog = new OptionalFontsDialog();
            Assert.IsFalse(dialog.IsHandleCreated);
            var grid = Field<DataGridView>(dialog, "_grid");
            Assert.AreEqual(dark, dialog.BackColor.GetBrightness() < .5f);
            Assert.AreEqual(dark, grid.BackgroundColor.GetBrightness() < .5f);
            Assert.IsFalse(grid.EnableHeadersVisualStyles, "Native light header styling must not override the explicit dialog palette");
            Assert.AreEqual(dark, grid.ColumnHeadersDefaultCellStyle.BackColor.GetBrightness() < .5f);
            Assert.IsGreaterThan(.4f, Math.Abs(grid.ColumnHeadersDefaultCellStyle.ForeColor.GetBrightness()
                - grid.ColumnHeadersDefaultCellStyle.BackColor.GetBrightness()));
            var rowStyle = grid.Rows[0].InheritedStyle;
            Assert.IsGreaterThan(.4f, Math.Abs(rowStyle.SelectionForeColor.GetBrightness()
                - rowStyle.SelectionBackColor.GetBrightness()), "Selected font names must remain readable");
            Assert.IsTrue(grid.ReadOnly);
            Assert.IsGreaterThan(0, grid.Rows.Count);
        }
        finally { ColorThemeService.SetActiveTheme(original); }
    }

    [STATestMethod]
    [DataRow("dark", true)]
    [DataRow("default-light", false)]
    public void FormatterListAndPathAreThemedBeforeDisplay(string theme, bool dark)
    {
        InitializeStyles();
        var original = ColorThemeService.ActiveThemeId;
        try
        {
            ColorThemeService.SetActiveTheme(theme);
            using var dialog = new CodeFormatterSettingsDialog(new CodeFormatterSettings(), () => {});
            Assert.IsFalse(dialog.IsHandleCreated);
            foreach (var input in new Control[] { Field<ListView>(dialog, "_toolList"), Field<TextBox>(dialog, "_pathTextBox") })
            {
                Assert.AreEqual(dark, input.BackColor.GetBrightness() < .5f, $"Unthemed initial control: {input.GetType().Name}");
                Assert.IsGreaterThan(.4f, Math.Abs(input.ForeColor.GetBrightness() - input.BackColor.GetBrightness()));
            }
            var link = Field<LinkLabel>(dialog, "_websiteLink");
            foreach (var color in new[] { link.LinkColor, link.ActiveLinkColor, link.VisitedLinkColor, link.DisabledLinkColor })
                Assert.IsGreaterThan(4.5, Contrast(color, link.BackColor), "Website link must remain readable in every interaction state");
        }
        finally { ColorThemeService.SetActiveTheme(original); }
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

    private static double Contrast(Color left, Color right)
    {
        static double Channel(byte value)
        {
            var x = value / 255.0;
            return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4);
        }
        static double Luminance(Color color) => .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        var l = Luminance(left);
        var r = Luminance(right);
        return (Math.Max(l, r) + .05) / (Math.Min(l, r) + .05);
    }

    private sealed class DpiScope : IDisposable
    {
        private readonly nint _previous = SetThreadDpiAwarenessContext(-4);
        public void Dispose() => SetThreadDpiAwarenessContext(_previous);
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowRect rect);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
