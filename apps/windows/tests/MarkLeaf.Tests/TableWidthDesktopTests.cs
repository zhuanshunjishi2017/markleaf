using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class TableWidthDesktopTests
{
    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void RealWebViewPreservesColumnAndLocalWidthsAcrossGesturesAndReload()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        var resources = Path.Combine(AppContext.BaseDirectory, "EditorWeb");
        Assert.IsTrue(File.Exists(Path.Combine(resources, "index.html")), "Build editor-web before desktop verification.");
        var profile = Path.Combine(Path.GetTempPath(), "markleaf-table-" + Guid.NewGuid().ToString("N"));
        using var form = new Form { Text = "MarkLeaf table width verification", Size = new Size(1000, 750) };
        using var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        form.Show();
        try
        {
            var environment = Wait(CoreWebView2Environment.CreateAsync(userDataFolder: profile));
            Wait(web.EnsureCoreWebView2Async(environment));
            var messages = new List<JsonElement>();
            web.CoreWebView2.WebMessageReceived += (_, e) => messages.Add(JsonDocument.Parse(e.WebMessageAsJson).RootElement.Clone());
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("table-test.invalid", resources, CoreWebView2HostResourceAccessKind.Allow);
            web.CoreWebView2.Navigate("https://table-test.invalid/index.html");
            Pump(() => messages.Any(m => m.GetProperty("type").GetString() == "ready"));
            void Send(string type, object? payload = null) => web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new {
                protocolVersion = 1, type, documentId = "table-test", revision = 0, requestId = Guid.NewGuid().ToString(), payload = payload ?? new { }
            }));
            JsonElement Eval(string script) => JsonDocument.Parse(Wait(web.CoreWebView2.ExecuteScriptAsync(script))).RootElement.Clone();
            var baseCss = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "Styles", "base.css"));
            Eval("{const style=document.createElement('style');style.textContent=" + JsonSerializer.Serialize(baseCss) + ";document.head.append(style);void 0}");
            const string markdown = "<!-- markleaf:table-layout {\"version\":1,\"shape\":[2,2,2],\"width\":400,\"columns\":[180,220],\"rows\":{\"2\":[210,190]}} -->\n| A | B |\n| --- | --- |\n| first | second |\n| third | fourth |";
            Send("loadDocument", new { markdown, readOnly = false });
            Pump(() => Eval("document.querySelectorAll('table.markleaf-table-manual').length").GetInt32() == 1);
            Eval("""
                window.layout=()=>JSON.parse(document.querySelector('table.markleaf-table-manual').dataset.markleafTableLayout);
                window.widths=()=>[...document.querySelector('table.markleaf-table-manual').rows].map(r=>[...r.cells].map(c=>c.getBoundingClientRect().width));
                window.drag=(row,dx,cancel=false)=>{const c=document.querySelector('table.markleaf-table-manual').rows[row].cells[0],r=c.getBoundingClientRect();c.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,cancelable:true,button:0,pointerId:7,clientX:r.right,clientY:r.top+5}));if(cancel)document.dispatchEvent(new KeyboardEvent('keydown',{bubbles:true,key:'Escape'}));document.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,cancelable:true,button:0,pointerId:7,clientX:r.right+dx,clientY:r.top+5}));};void 0
                """);
            Assert.IsTrue(Eval("Math.abs(widths()[0][0]-180)<1 && Math.abs(widths()[2][0]-210)<1").GetBoolean());
            Eval("drag(1,20)");
            Assert.IsTrue(Eval("Math.abs(layout().columns[0]-200)<1 && Math.abs(layout().rows['2'][0]-230)<1").GetBoolean());
            Send("command", new { command = "undo" });
            Pump(() => Eval("layout().columns[0]").GetDouble() == 180);
            Eval("{const cell=document.querySelector('table').rows[0].cells[0],range=document.createRange();range.selectNodeContents(cell);window.getSelection().removeAllRanges();window.getSelection().addRange(range);document.dispatchEvent(new Event('selectionchange'));void 0}");
            Assert.AreEqual("A", Eval("window.getSelection().toString()").GetString());
            Eval("drag(0,25)");
            Assert.IsTrue(Eval("Math.abs(layout().rows['0'][0]-205)<1 && layout().columns[0]===180").GetBoolean());
            Eval("drag(0,20,true)");
            Assert.IsTrue(Eval("Math.abs(layout().rows['0'][0]-205)<1").GetBoolean());
            Send("command", new { command = "fitTableContents" });
            Pump(() => Eval("Object.keys(layout().rows).length").GetInt32() == 0);
            Send("command", new { command = "distributeTableColumns" });
            Pump(() => Eval("Math.abs(widths()[0][0]-widths()[0][1])<1").GetBoolean());
            Send("loadDocument", new { markdown, readOnly = false });
            Pump(() => Eval("layout().columns[0]").GetDouble() == 180);
            foreach (var zoom in new[] { .8, 1.25, 2.0 })
            {
                Eval($"document.querySelector('.ProseMirror').style.zoom='{zoom.ToString(System.Globalization.CultureInfo.InvariantCulture)}';void 0");
                var before = Eval("layout().columns[0]").GetDouble();
                Eval($"drag(1,{(10 * zoom).ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                Assert.AreEqual(before + 10, Eval("layout().columns[0]").GetDouble(), 1);
            }
            Eval("document.querySelector('.ProseMirror').style.zoom='1';void 0");
            foreach (var zoom in new[] { .8, 1.25, 2.0 })
            {
                web.ZoomFactor = zoom;
                var before = Eval("layout().columns[0]").GetDouble();
                Eval("drag(1,10)");
                Assert.AreEqual(before + 10, Eval("layout().columns[0]").GetDouble(), 1);
            }
            web.ZoomFactor = 1;
            messages.RemoveAll(m => m.GetProperty("type").GetString() == "snapshot");
            Send("requestSnapshot");
            Pump(() => messages.Any(m => m.GetProperty("type").GetString() == "snapshot"));
            var saved = messages.Last(m => m.GetProperty("type").GetString() == "snapshot").GetProperty("payload").GetProperty("markdown").GetString()!;
            StringAssert.Contains(saved, "markleaf:table-layout");
            Send("loadDocument", new { markdown = saved, readOnly = false });
            Pump(() => Eval("document.querySelectorAll('table.markleaf-table-manual').length").GetInt32() == 1);
            Send("loadDocument", new { markdown = markdown.Replace("[180,220]", "[40,360]"), readOnly = false });
            Pump(() => Eval("layout().columns[0]").GetDouble() == 40);
            Eval("document.querySelector('.ProseMirror').style.width='200px';document.querySelector('.ProseMirror').style.zoom='1';void 0");
            Assert.IsTrue(Eval("Math.min(...widths().flat())>=39.5 && document.querySelector('.markleaf-table-wrapper').scrollWidth>document.querySelector('.markleaf-table-wrapper').clientWidth").GetBoolean());
            Send("command", new { command = "resetTableWidths" });
            Pump(() => Eval("document.querySelectorAll('table.markleaf-table-manual').length").GetInt32() == 0);
        }
        finally
        {
            web.Dispose();
            try { Directory.Delete(profile, true); } catch (IOException) { }
        }
    }
    private static T Wait<T>(Task<T> task) { Pump(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task) { Pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Pump(Func<bool> complete)
    {
        var watch = Stopwatch.StartNew();
        while (!complete() && watch.Elapsed < TimeSpan.FromSeconds(20)) { Application.DoEvents(); Thread.Sleep(8); }
        Assert.IsTrue(complete(), "WebView2 table verification timed out.");
    }
}
