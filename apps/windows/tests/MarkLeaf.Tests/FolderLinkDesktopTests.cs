using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using MarkLeaf.Services.ExternalLinks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class FolderLinkDesktopTests
{
    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void CtrlClickFolderLinkOpensExplorer()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        var root = Path.Combine(Path.GetTempPath(), "markleaf-folder-link-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "会议记录");
        Directory.CreateDirectory(folder);
        var document = Path.Combine(root, "readme.md");
        File.WriteAllText(document, "[会议记录](会议记录/)");
        using var form = new Form { Text = "MarkLeaf folder link verification", Size = new Size(900, 600) };
        using var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        form.Show();
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        try
        {
            var environment = Wait(CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "profile")));
            Wait(web.EnsureCoreWebView2Async(environment));
            var messages = new List<JsonElement>();
            web.CoreWebView2.WebMessageReceived += (_, e) => messages.Add(JsonDocument.Parse(e.WebMessageAsJson).RootElement.Clone());
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("folder-test.invalid", Path.Combine(AppContext.BaseDirectory, "EditorWeb"), CoreWebView2HostResourceAccessKind.Allow);
            web.CoreWebView2.Navigate("https://folder-test.invalid/index.html");
            Pump(() => messages.Any(m => m.GetProperty("type").GetString() == "ready"));
            web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                protocolVersion = 1, type = "loadDocument", documentId = "folder-link-test", revision = 0,
                payload = new { markdown = File.ReadAllText(document), readOnly = false }
            }));
            JsonElement Eval(string script) => JsonDocument.Parse(Wait(web.CoreWebView2.ExecuteScriptAsync(script))).RootElement.Clone();
            Pump(() => Eval("!!document.querySelector('.ProseMirror a')").GetBoolean());
            Eval("document.querySelector('.ProseMirror a').dispatchEvent(new MouseEvent('mousemove',{bubbles:true}));void 0");
            StringAssert.Contains(Eval("document.querySelector('.editor-tooltip-hint').textContent").GetString()!, "Ctrl");
            StringAssert.Contains(Eval("document.querySelector('.editor-tooltip-hint').textContent").GetString()!, "文件夹");
            Eval("document.querySelector('.ProseMirror a').dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,ctrlKey:true}));void 0");
            Pump(() => messages.Any(m => m.GetProperty("type").GetString() == "openLink"));
            var href = messages.Last(m => m.GetProperty("type").GetString() == "openLink").GetProperty("payload").GetProperty("url").GetString()!;
            ExternalLinkService.Open(href, document);
            Pump(() => FindExplorerWindow() is not null);
            object? FindExplorerWindow()
            {
                foreach (var window in shell.Windows())
                {
                    try
                    {
                        if (Uri.TryCreate((string)window.LocationURL, UriKind.Absolute, out var uri)
                            && uri.IsFile && string.Equals(Path.TrimEndingDirectorySeparator(uri.LocalPath), folder, StringComparison.OrdinalIgnoreCase)) return window;
                    }
                    catch (System.Runtime.InteropServices.COMException) { }
                }
                return null;
            }
            dynamic? opened = FindExplorerWindow();
            opened?.Quit();
        }
        finally
        {
            web.Dispose();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
    private static T Wait<T>(Task<T> task) { Pump(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { Pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Pump(Func<bool> complete)
    {
        var watch = Stopwatch.StartNew();
        while (!complete() && watch.Elapsed < TimeSpan.FromSeconds(30)) { Application.DoEvents(); Thread.Sleep(10); }
        Assert.IsTrue(complete(), "Folder link desktop verification timed out.");
    }
}
