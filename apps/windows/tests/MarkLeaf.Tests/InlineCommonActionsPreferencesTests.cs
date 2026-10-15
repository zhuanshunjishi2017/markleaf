using System.Windows.Forms;
using System.Runtime.InteropServices;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class InlineCommonActionsPreferencesTests
{
    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow(false)]
    [DataRow(true)]
    public void CheckboxSavesDirectMeaningAndCancelLeavesPreferenceUnchanged(bool initial)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var settings = new AppSettings { Editor = new EditorSettings { InlineCommonActions = initial } };
        using (var dialog = new PreferencesDialog(settings))
        {
            dialog.Show();
            var check = InlineCheck(dialog);
            Assert.AreEqual(initial, check.Checked);
            check.Checked = !initial;
            var screenshotDirectory = Environment.GetEnvironmentVariable("MARKLEAF_MENU_TEST_SCREENSHOTS");
            if (!string.IsNullOrWhiteSpace(screenshotDirectory))
            {
                Directory.CreateDirectory(screenshotDirectory);
                using var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, dialog.Width, dialog.Height));
                image.Save(Path.Combine(screenshotDirectory, $"preferences-inline-{!initial}.png"));
            }
            Assert.AreEqual(initial, settings.Editor.InlineCommonActions, "Changes must remain pending until OK.");
            ((Button)dialog.AcceptButton!).PerformClick();
            Assert.AreEqual(DialogResult.OK, dialog.DialogResult);
        }
        Assert.AreEqual(!initial, settings.Editor.InlineCommonActions);
        using (var reopened = new PreferencesDialog(settings))
        {
            reopened.Show();
            var check = InlineCheck(reopened);
            Assert.AreEqual(!initial, check.Checked, "The saved value must be reflected on reopening.");
            check.Checked = initial;
            ((Button)reopened.CancelButton!).PerformClick();
        }
        Assert.AreEqual(!initial, settings.Editor.InlineCommonActions, "Cancel must not alter the saved choice.");
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void ShortPreferencesViewportCanScrollToLastVisualOptionWithoutMovingFooter()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        using var dialog = new PreferencesDialog(new AppSettings());
        dialog.Show();
        Assert.IsTrue(Screen.FromControl(dialog).WorkingArea.Contains(dialog.Bounds), "Initial window must fit the monitor.");
        InlineCheck(dialog);
        var last = Descendants(dialog).OfType<CheckBox>()
            .Single(control => control.Text == Loc.Get("prefs.editor.codeBlockSpellcheck"));
        var container = Descendants(dialog).OfType<PreferenceOptionsContainer>().Single();
        var viewport = container.Controls.OfType<Panel>().Single();
        if (Screen.FromControl(dialog).WorkingArea.Height >= Math.Round(660 * dialog.DeviceDpi / 96.0))
        {
            Assert.IsFalse(viewport.VerticalScroll.Visible, "Empty filler rows must not create overflow when all settings fit.");
            Assert.IsTrue(viewport.RectangleToScreen(viewport.ClientRectangle).Contains(last.RectangleToScreen(last.ClientRectangle)));
        }
        SaveScreenshot(dialog, "preferences-default-size.png");
        dialog.Height = Math.Min(dialog.Height, (int)Math.Round(450 * dialog.DeviceDpi / 96.0));
        Application.DoEvents();
        var ok = (Button)dialog.AcceptButton!;
        var footerBounds = ok.RectangleToScreen(ok.ClientRectangle);
        Assert.IsTrue(viewport.AutoScroll && viewport.VerticalScroll.Visible,
            "The framed settings viewport must scroll instead of clipping its last option.");
        viewport.ScrollControlIntoView(last);
        Application.DoEvents();
        Assert.IsTrue(viewport.RectangleToScreen(viewport.ClientRectangle).Contains(last.RectangleToScreen(last.ClientRectangle)),
            "The entire final checkbox must be reachable, not only its top edge.");
        Assert.AreEqual(footerBounds, ok.RectangleToScreen(ok.ClientRectangle));
        Assert.IsTrue(dialog.RectangleToScreen(dialog.ClientRectangle).Contains(footerBounds));
        SaveScreenshot(dialog, "preferences-short-scrolled.png");
    }

    private static void SaveScreenshot(Form dialog, string name)
    {
        var directory = Environment.GetEnvironmentVariable("MARKLEAF_MENU_TEST_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        dialog.Refresh();
        using var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(image))
            graphics.CopyFromScreen(dialog.Location, System.Drawing.Point.Empty, image.Size);
        image.Save(Path.Combine(directory, name));
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow("zh-CN")]
    [DataRow("en-US")]
    public void CompactPreferencesCentersVisualFormAndWrapsLongOptions(string language)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        LocTestHelper.EnsureInitialized();
        var resources = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources");
        StyleService.Initialize(Path.Combine(resources, "Styles"));
        ColorThemeService.Initialize(Path.Combine(resources, "Styles"));
        Loc.Initialize(Path.Combine(resources, "Locales"), language);
        try
        {
            using var dialog = new PreferencesDialog(new AppSettings());
            dialog.Show();
            InlineCheck(dialog);
            Application.DoEvents();
            var scale = dialog.DeviceDpi / 96.0;
            Assert.IsTrue(dialog.Width <= Math.Ceiling(500 * scale)
                && dialog.Height <= Math.Ceiling(630 * scale), $"Oversized preferences: {dialog.Size}");
            var container = Descendants(dialog).OfType<PreferenceOptionsContainer>().Single();
            var viewport = container.Controls.OfType<Panel>().Single();
            var bounds = viewport.RectangleToScreen(viewport.ClientRectangle);
            var controls = Descendants(viewport).Where(control => control.Visible
                && control is Label or CheckBox or NumericUpDown or ComboBox).ToArray();
            var formBounds = controls.Select(control => control.RectangleToScreen(control.ClientRectangle))
                .Aggregate(System.Drawing.Rectangle.Union);
            var leftGap = formBounds.Left - bounds.Left;
            var rightGap = bounds.Right - formBounds.Right;
            Assert.IsLessThanOrEqualTo(Math.Ceiling(12 * scale), (double)Math.Abs(leftGap - rightGap),
                $"Form must be centered: left={leftGap}, right={rightGap}, form={formBounds}, viewport={bounds}");
            Assert.IsFalse(viewport.HorizontalScroll.Visible, "Long labels must wrap, not widen the viewport.");
            foreach (var check in controls.OfType<CheckBox>())
            {
                var checkBounds = check.RectangleToScreen(check.ClientRectangle);
                Assert.IsTrue(checkBounds.Left >= bounds.Left && checkBounds.Right <= bounds.Right,
                    $"Checkbox is clipped horizontally: {check.Text}: {checkBounds}");
                Assert.IsGreaterThanOrEqualTo(check.GetPreferredSize(new System.Drawing.Size(check.Width, 0)).Height, check.Height,
                    $"Wrapped text must retain its full preferred height: {check.Text}");
                if (language == "en-US" && check.Text == Loc.Get("prefs.editor.exitBlockOnEmptyEnter"))
                    Assert.IsGreaterThanOrEqualTo(check.Font.Height * 2, check.ClientSize.Height,
                        "The long English option needs enough height for both lines in this compact viewport.");
            }
            SaveScreenshot(dialog, $"preferences-compact-{language}.png");
        }
        finally { Loc.Initialize(Path.Combine(resources, "Locales"), "zh-CN"); }
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void DefaultEnglishPreferencesKeepsVisualCheckboxesInsideViewport()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        using var dpiScope = new DpiScope();
        LocTestHelper.EnsureInitialized();
        var resources = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources");
        StyleService.Initialize(Path.Combine(resources, "Styles"));
        ColorThemeService.Initialize(Path.Combine(resources, "Styles"));
        Loc.Initialize(Path.Combine(resources, "Locales"), "en-US");
        try
        {
            using var dialog = new PreferencesDialog(new AppSettings());
            dialog.Show();
            InlineCheck(dialog);
            var container = Descendants(dialog).OfType<PreferenceOptionsContainer>().Single();
            var viewport = container.Controls.OfType<Panel>().Single();
            var bounds = viewport.RectangleToScreen(viewport.ClientRectangle);
            var clipped = Descendants(viewport).OfType<CheckBox>()
                .Select(check => (check.Text, Bounds: check.RectangleToScreen(check.ClientRectangle)))
                .Where(check => check.Bounds.Left < bounds.Left || check.Bounds.Right > bounds.Right).ToArray();
            Assert.IsEmpty(clipped, $"Viewport={bounds}; clipped labels: "
                + string.Join("; ", clipped.Select(check => $"{check.Text}: {check.Bounds}")));
        }
        finally { Loc.Initialize(Path.Combine(resources, "Locales"), "zh-CN"); }
    }

    private static CheckBox InlineCheck(PreferencesDialog dialog)
    {
        Descendants(dialog).OfType<PreferencesTabBar>().First().SelectedIndex = 2;
        return Descendants(dialog).OfType<CheckBox>()
            .Single(control => control.Text == Loc.Get("prefs.editor.inlineCommonActions"));
    }

    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));

    private sealed class DpiScope : IDisposable
    {
        private readonly nint _previous = SetThreadDpiAwarenessContext(-4);
        public void Dispose() => SetThreadDpiAwarenessContext(_previous);
    }

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
}
