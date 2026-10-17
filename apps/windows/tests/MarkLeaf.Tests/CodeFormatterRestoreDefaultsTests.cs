using System.Reflection;
using System.Windows.Forms;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CodeFormatterRestoreDefaultsTests
{
    [TestMethod]
    public void ManagerExposesRestoreDefaultsAction()
    {
        var settings = new CodeFormatterSettings
        {
            SqlDialect = "postgres",
        };
        settings.ToolPaths["black"] = "C:\\Tools\\black.exe";
        settings.ToolPaths["rustfmt"] = "C:\\Tools\\rustfmt.exe";

        using var dialog = new CodeFormatterSettingsDialog(settings, () => { });
        var restoreButton = (Button)typeof(CodeFormatterSettingsDialog)
            .GetField("_restoreDefaultsButton", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dialog)!;

        Assert.IsNotNull(restoreButton);
        dialog.RestoreDefaults();

        Assert.IsEmpty(settings.ToolPaths);
        Assert.AreEqual("ansi", settings.SqlDialect);
    }

}
