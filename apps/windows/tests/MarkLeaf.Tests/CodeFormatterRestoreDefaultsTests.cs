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
        var changeCount = 0;

        using var dialog = new CodeFormatterSettingsDialog(settings, () => changeCount++);
        var restoreButton = (Button)typeof(CodeFormatterSettingsDialog)
            .GetField("_restoreDefaultsButton", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dialog)!;

        Assert.IsNotNull(restoreButton);
        Assert.AreEqual(7, ((TableLayoutPanel)restoreButton.Parent!).ColumnCount);
        dialog.RestoreDefaults();

        Assert.IsEmpty(settings.ToolPaths);
        Assert.AreEqual("ansi", settings.SqlDialect);
        Assert.AreEqual(1, changeCount);
    }

}
