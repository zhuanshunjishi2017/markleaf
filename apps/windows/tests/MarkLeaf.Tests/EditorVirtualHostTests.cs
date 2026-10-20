using MarkLeaf.Editor;
using System.Reflection;
using System.Drawing;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorVirtualHostTests
{
    [TestMethod]
    public void VirtualHostsUseTheReservedExampleTld()
    {
        var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var editorHost = typeof(EditorHostController).GetField(nameof(EditorHostController.EditorHostName), flags)?.GetValue(null);
        var assetHost = typeof(EditorHostController).GetField(nameof(EditorHostController.AssetHostName), flags)?.GetValue(null);

        Assert.AreEqual("editor.example", editorHost);
        Assert.AreEqual("assets.example", assetHost);
    }

    [TestMethod]
    public void StartupScriptInjectsARemovableThemeBackground()
    {
        var script = EditorHostController.CreateDocumentCreatedScript(Color.FromArgb(28, 28, 30));

        Assert.Contains("markleaf-startup-background", script);
        Assert.Contains("#1C1C1E !important", script);
        Assert.Contains("__markleafClearStartupBackground", script);
        Assert.DoesNotContain("markleaf-startup-background", EditorHostController.CreateDocumentCreatedScript(null));
    }
}
