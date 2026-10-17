using System.Reflection;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CodeFormatterDialogLayoutTests
{
    private static string Locales => Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources", "Locales");

    [STATestMethod]
    [DataRow("zh-CN", "代码格式化器")]
    [DataRow("zh-TW", "程式碼格式化器")]
    [DataRow("en-US", "Code Formatters")]
    [DataRow("ja-JP", "コードフォーマッター")]
    public void WindowUsesTheUnifiedFormatterName(string language, string expected)
    {
        Loc.Initialize(Locales, language);
        try
        {
            using var dialog = new CodeFormatterSettingsDialog(new CodeFormatterSettings(), () => {});
            Assert.AreEqual(expected, dialog.Text);
        }
        finally { Loc.Initialize(Locales, "zh-CN"); }
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    [DataRow("zh-CN", 1f)]
    [DataRow("en-US", 1f)]
    [DataRow("zh-CN", 1.5f)]
    [DataRow("en-US", 2f)]
    public void WebsiteAndPathButtonsShareTheSameVerticalCenter(string language, float scale)
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        Loc.Initialize(Locales, language);
        try
        {
            using var dialog = new CodeFormatterSettingsDialog(new CodeFormatterSettings(), () => {});
            dialog.Show();
            var list = Field<ListView>(dialog, "_toolList");
            list.Items[0].Selected = true;
            Application.DoEvents();
            var link = Field<LinkLabel>(dialog, "_websiteLink");
            // This test owns the dialog; show the optional control independently
            // of whatever developer tools happen to be installed on the machine.
            link.Visible = true;
            dialog.Scale(new SizeF(scale, scale));
            dialog.PerformLayout();
            link.Parent!.PerformLayout();
            Application.DoEvents();
            var browse = Field<Button>(dialog, "_browseButton");
            var clear = Field<Button>(dialog, "_clearButton");
            static double Center(Control control) => control.Top + control.Height / 2.0;
            var pathRow = (TableLayoutPanel)link.Parent!;
            Assert.IsGreaterThan(2.0 * dialog.ClientSize.Height / 3.0, list.Height,
                "The tool list must own the flexible height instead of collapsing.");
            Assert.IsLessThanOrEqualTo(Math.Max(browse.Height, clear.Height) + browse.Margin.Vertical + 2,
                pathRow.Height,
                "The path row must stay compact.");
            Assert.AreSame(browse.Parent, link.Parent);
            Assert.IsLessThanOrEqualTo(1d, Math.Abs(Center(link) - Center(browse)),
                $"Website {link.Bounds} must be vertically centered with browse {browse.Bounds}.");
            Assert.IsLessThanOrEqualTo(1d, Math.Abs(Center(link) - Center(clear)),
                $"Website {link.Bounds} must be vertically centered with clear {clear.Bounds}.");
        }
        finally { Loc.Initialize(Locales, "zh-CN"); }
    }

    [STATestMethod]
    [TestCategory("InteractiveDesktop")]
    public void SqlDialectAppearsOnlyForSqlToolsAndAfterProbe()
    {
        if (!Environment.UserInteractive) Assert.Inconclusive("Requires the interactive Windows desktop.");
        Loc.Initialize(Locales, "zh-CN");
        try
        {
            using var dialog = new CodeFormatterSettingsDialog(new CodeFormatterSettings(), () => {});
            dialog.Show();
            var list = Field<ListView>(dialog, "_toolList");
            var sqlDialect = Field<ComboBox>(dialog, "_sqlDialectCombo");
            var probe = Field<Button>(dialog, "_probeButton");
            list.Items[0].Selected = true;
            Application.DoEvents();
            Assert.IsFalse(sqlDialect.Visible);

            list.Items.Cast<ListViewItem>().Single(item => (string)item.Tag! == "sqlfluff").Selected = true;
            Application.DoEvents();
            Assert.IsTrue(sqlDialect.Visible);
            Assert.AreSame(probe.Parent, sqlDialect.Parent);
            var pathRow = Assert.IsInstanceOfType<TableLayoutPanel>(probe.Parent);
            Assert.AreEqual(pathRow.GetColumn(probe) + 1, pathRow.GetColumn(sqlDialect));
            Assert.IsGreaterThan(2.0 * dialog.ClientSize.Height / 3.0,
                Field<ListView>(dialog, "_toolList").Height);
        }
        finally { Loc.Initialize(Locales, "zh-CN"); }
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
}
