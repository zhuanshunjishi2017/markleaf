using System.Reflection;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Dialogs;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ExportDialogThemeTests
{
    [TestMethod]
    public void UniformDarkPdfViewerPlaceholderIsNotPaintedContent()
    {
        // Captured from the PDF viewer just before the visible white flash:
        // the viewer shell is #333, with no page content painted yet.
        using var frame = new Bitmap(480, 400);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.FromArgb(51, 51, 51));
        Assert.IsFalse(ExportPreviewFramePolicy.IsPainted(frame, allowBlankPage: false));
    }

    [TestMethod]
    public void WhiteLoadingFramesAreRejectedButReadyBlankLightPagesAreAllowed()
    {
        using var frame = new Bitmap(480, 400);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.White);
        Assert.IsFalse(ExportPreviewFramePolicy.IsPainted(frame, allowBlankPage: false));
        Assert.IsTrue(ExportPreviewFramePolicy.IsPainted(frame, allowBlankPage: true));
        graphics.Clear(Color.FromArgb(30, 30, 30));
        Assert.IsTrue(ExportPreviewFramePolicy.IsPainted(frame, allowBlankPage: false, pageBackground: Color.FromArgb(30, 30, 30)));
    }

    [TestMethod]
    public void PdfReadinessRejectsSpinnersButAllowsPaintedImages()
    {
        using var frame = new Bitmap(480, 400);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(Color.FromArgb(51, 51, 51));
        graphics.FillEllipse(Brushes.White, 230, 190, 20, 20);
        Assert.IsFalse(ExportPreviewFramePolicy.IsPainted(frame, false, Color.FromArgb(30, 30, 30)));
        graphics.FillRectangle(Brushes.DarkGreen, 50, 50, 150, 200);
        Assert.IsTrue(ExportPreviewFramePolicy.IsPainted(frame, false, Color.FromArgb(30, 30, 30)));
    }

    [STATestMethod]
    public void ThemeIsAppliedBeforeShowingIncludingInactiveExportPages()
    {
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(ExportDialog).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var originalTheme = ColorThemeService.ActiveThemeId;
        try
        {
            foreach (var dark in new[] { false, true })
            {
                ColorThemeService.SetActiveTheme(ColorThemeService.All.First(theme => theme.IsDark == dark).Id);
                using var dialog = new ExportDialog("test.md", "test", "serif", StyleService.GetAllStyles(),
                    Path.GetTempPath(), _ => Task.FromResult(Array.Empty<byte>()), _ => Task.FromResult(""));
                var preview = Field<WebView2>(dialog, "_previewView");
                var loading = Field<Label>(dialog, "_loadingLabel");
                Assert.AreEqual(dialog.BackColor, preview.DefaultBackgroundColor);
                Assert.AreEqual(dialog.BackColor, loading.BackColor);
                foreach (var name in new[] { "_portrait", "_landscape", "_imageFormatPng", "_imageFormatJpeg" })
                {
                    var radio = Field<RadioButton>(dialog, name);
                    Assert.AreNotEqual(FlatStyle.System, radio.FlatStyle, "System radios ignore the custom dark text color");
                    Assert.IsGreaterThan(0.4f, Math.Abs(radio.ForeColor.GetBrightness() - radio.BackColor.GetBrightness()));
                }
            }
        }
        finally { ColorThemeService.SetActiveTheme(originalTheme); }
    }

    private static T Field<T>(ExportDialog dialog, string name) =>
        (T)typeof(ExportDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
}
