using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using MarkLeaf.Services.Styles;
using MarkLeaf.Editor;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ExportPreviewDesktopTests
{
    [STATestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    [TestCategory("InteractiveDesktop")]
    public void PdfPreviewChromeFollowsApplicationThemeIndependentlyOfPage(bool darkUi, bool darkPage)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(AppContext.BaseDirectory, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var original = ColorThemeService.ActiveThemeId;
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-preview-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ColorThemeService.SetActiveTheme(darkUi ? "dark" : "default-light");
            using var editor = new WebView2();
            var initializeEditor = editor.EnsureCoreWebView2Async(
                CreateEnvironmentOnUiThread(directory));
            WaitForInitialization(initializeEditor);
            initializeEditor.GetAwaiter().GetResult();
            var editorScheme = editor.CoreWebView2.Profile.PreferredColorScheme;
            using var dialog = new ExportDialog("chrome-test.md", "chrome-test", "serif", StyleService.GetAllStyles(),
                directory, _ => Task.FromResult(DarkPdf(false, darkPage)), _ => Task.FromResult(""));
            Field<ComboBox>(dialog, "_pdfColorScheme").SelectedIndex =
                ColorThemeService.All.ToList().FindIndex(theme => theme.Id == (darkPage ? "dark" : "default-light"));
            dialog.TopMost = true;
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Show();
            SetForegroundWindow(dialog.Handle);
            var watch = Stopwatch.StartNew();
            while (Field<ExportOptions?>(dialog, "_lastPreviewOptions") is null && watch.Elapsed < TimeSpan.FromSeconds(20))
            {
                Application.DoEvents();
                Thread.Sleep(8);
            }
            Assert.IsNotNull(Field<ExportOptions?>(dialog, "_lastPreviewOptions"), "The real PDF must finish loading");
            var preview = Field<WebView2>(dialog, "_previewView");
            Assert.AreEqual(editorScheme, editor.CoreWebView2.Profile.PreferredColorScheme,
                "Preview theme must not mutate the editor's shared default profile");
            using var frame = new Bitmap(preview.Width, preview.Height);
            do
            {
                Application.DoEvents();
                using var graphics = Graphics.FromImage(frame);
                graphics.CopyFromScreen(preview.PointToScreen(Point.Empty), Point.Empty, frame.Size);
                if (darkPage ? frame.GetPixel(frame.Width / 2, frame.Height / 2).GetBrightness() < .2f
                    : frame.GetPixel(frame.Width / 2, frame.Height / 2).GetBrightness() > .9f) break;
                Thread.Sleep(8);
            } while (watch.Elapsed < TimeSpan.FromSeconds(20));
            Assert.AreEqual(darkPage, frame.GetPixel(frame.Width / 2, frame.Height / 2).GetBrightness() < .5f,
                "Viewer theme must not recolor the exported PDF page");
            if (Environment.GetEnvironmentVariable("MARKLEAF_EXPORT_QA_OUTPUT") is { } output)
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, $"pdf-preview-chrome-{darkUi}-{darkPage}.png"), ImageFormat.Png);
            }
            // Check the viewer's left/top rim and right scrollbar track, not PDF page pixels.
            foreach (var point in new[] { new Point(1, frame.Height / 2), new Point(frame.Width / 2, 1),
                new Point(frame.Width - 3, frame.Height / 2) })
                Assert.AreEqual(darkUi, frame.GetPixel(point.X, point.Y).GetBrightness() < .7f,
                    $"Wrong PDF viewer chrome theme at {point}");
            dialog.Close();
        }
        finally
        {
            ColorThemeService.SetActiveTheme(original);
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private static CoreWebView2Environment CreateEnvironmentOnUiThread(string directory)
    {
        var task = CoreWebView2Environment.CreateAsync(userDataFolder: directory);
        WaitForInitialization(task);
        return task.GetAwaiter().GetResult();
    }

    private static void WaitForInitialization(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            Application.DoEvents();
            Thread.Sleep(8);
        }
        Assert.IsTrue(task.IsCompleted, "WebView2 initialization did not finish within 20 seconds");
    }

    [STATestMethod]
    [DataRow(false, "simple")]
    [DataRow(true, "simple")]
    [DataRow(true, "generated")]
    [DataRow(true, "blank")]
    [TestCategory("InteractiveDesktop")]
    public void DarkPdfPreviewNeverExposesWhiteLoadingFramesAcrossRefreshes(bool captureDesktop, string pdfKind)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        LocTestHelper.EnsureInitialized();
        var styles = Path.Combine(Path.GetDirectoryName(typeof(ExportDialog).Assembly.Location)!, "Resources", "Styles");
        StyleService.Initialize(styles);
        ColorThemeService.Initialize(styles);
        var original = ColorThemeService.ActiveThemeId;
        var directory = Path.Combine(Path.GetTempPath(), "markleaf-preview-desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ColorThemeService.SetActiveTheme("dark");
            var pdf = DarkPdf(pdfKind == "blank");
            using var sourceView = new WebView2();
            using var loading = new EditorLoadingView();
            using var printHost = new EditorHostController(sourceView, loading, new EditorSession(), new TestLogger(),
                AppContext.BaseDirectory, directory, () => {});
            Func<ExportOptions, Task<byte[]>> generate = pdfKind == "generated"
                ? options => printHost.PrintExportToPdfAsync(
                    "<html><head><meta charset='utf-8'><style>html,body{background:#1e1e1e;color:#ddd;font-family:'Microsoft YaHei';font-size:16px}table{border-collapse:collapse}td{border:1px solid #aaa;padding:8px}</style></head><body>"
                    + "<h1>PDF 首次预览</h1><p>中文字体与 English 内容，表格和代码块</p>"
                    + "<table><tr><td>项目</td><td>状态</td></tr><tr><td>实际生成的 PDF</td><td>等待绘制完成</td></tr></table>"
                    + "<pre>def preview_ready(frame):\n    return frame.has_document_content</pre>"
                    + "<h2>第二节</h2><p>预览不能在灰色占位画面出现时就认为加载完成。</p></body></html>",
                    options.PaperSize, options.Landscape, options.MarginTop, options.MarginBottom, options.MarginLeft, options.MarginRight)
                : _ => Task.FromResult(pdf);
            using var dialog = new ExportDialog("preview-test.md", "preview-test", "serif", StyleService.GetAllStyles(),
                directory, generate, _ => Task.FromResult("<body style='background:#222'>preview</body>"));
            dialog.TopMost = true;
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.Show();
            SetForegroundWindow(dialog.Handle);
            var watch = Stopwatch.StartNew();
            var whiteFrames = 0;
            var renderedFrames = 0;
            var refreshes = 0;
            var nextRefresh = TimeSpan.FromSeconds(2);
            Task? refresh = null;
            while (watch.Elapsed < TimeSpan.FromSeconds(18))
            {
                Application.DoEvents();
                using var frame = new Bitmap(dialog.Width, dialog.Height);
                using (var graphics = Graphics.FromImage(frame))
                {
                    if (captureDesktop)
                        graphics.CopyFromScreen(dialog.Left, dialog.Top, 0, 0, frame.Size);
                    else
                    {
                        var dc = graphics.GetHdc();
                        try { PrintWindow(dialog.Handle, dc, 2); }
                        finally { graphics.ReleaseHdc(dc); }
                    }
                }
                var white = IsWhitePreview(frame);
                if (white)
                {
                    whiteFrames++;
                    if (whiteFrames <= 3 && Environment.GetEnvironmentVariable("MARKLEAF_EXPORT_QA_OUTPUT") is { } output)
                    {
                        Directory.CreateDirectory(output);
                        frame.Save(Path.Combine(output, $"white-frame-{captureDesktop}-{pdfKind}-{whiteFrames}.png"), ImageFormat.Png);
                    }
                }
                if (Field<ExportOptions?>(dialog, "_lastPreviewOptions") is not null && !white) renderedFrames++;
                if (renderedFrames > 5 && refreshes < 3 && watch.Elapsed >= nextRefresh && refresh?.IsCompleted != false)
                {
                    refresh = (Task)typeof(ExportDialog).GetMethod("RefreshPreviewAsync",
                        BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!.Invoke(dialog, null)!;
                    refreshes++;
                    nextRefresh = watch.Elapsed + TimeSpan.FromSeconds(2);
                }
                if (refreshes == 3 && refresh?.IsCompleted == true && watch.Elapsed > nextRefresh) break;
                Thread.Sleep(8);
            }
            dialog.Close();
            Assert.AreEqual(3, refreshes, "The real PDF viewer must finish initial loading and three refreshes");
            Assert.IsGreaterThan(5, renderedFrames, "The preview must actually become visible");
            Assert.AreEqual(0, whiteFrames, "No visible frame may expose the PDF viewer's white loading surface");
        }
        finally
        {
            ColorThemeService.SetActiveTheme(original);
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private static bool IsWhitePreview(Bitmap frame)
    {
        var white = 0;
        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 10; x++)
        {
            var pixel = frame.GetPixel((int)(frame.Width * (.58 + x * .035)), (int)(frame.Height * (.25 + y * .05)));
            if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240) white++;
        }
        return white > 90;
    }

    private static byte[] DarkPdf(bool blankPage, bool darkPage = true)
    {
        var stream = (darkPage ? "0.12 0.12 0.12" : "1 1 1") + " rg 0 0 612 792 re f "
            + (blankPage ? "\n" : "1 1 1 rg BT /F1 18 Tf 50 740 Td (Preview Test) Tj ET\n");
        var objects = new[] {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {stream.Length} >>\nstream\n{stream}endstream" };
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(text.Length);
            text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = text.Length;
        text.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets) text.Append(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        text.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    private static T Field<T>(ExportDialog dialog, string name) =>
        (T)typeof(ExportDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
