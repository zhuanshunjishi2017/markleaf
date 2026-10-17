using MarkLeaf.Services.Settings;
using MarkLeaf.Commands;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class SettingsResetTests
{
    [TestMethod]
    public void Apply_ResetsEveryPersistedSettingsGroup()
    {
        var target = new AppSettings
        {
            Export = new ExportSettings { Format = "html", Landscape = true },
            Shortcut = new ShortcutSettings(),
            CodeFormatter = new CodeFormatterSettings { SqlDialect = "postgres" },
        };
        target.Shortcut.Overrides[nameof(AppCommand.ToggleBold)] = "Ctrl+Alt+B";
        target.CodeFormatter.ToolPaths["black"] = "C:\\Tools\\black.exe";

        var defaults = AppSettings.CreateDefaults();
        defaults.Export.Format = "pdf";
        defaults.CodeFormatter.SqlDialect = "ansi";
        SettingsReset.Apply(target, defaults);

        Assert.AreEqual("pdf", target.Export.Format);
        Assert.IsFalse(target.Export.Landscape);
        Assert.IsEmpty(target.Shortcut.Overrides);
        Assert.IsEmpty(target.CodeFormatter.ToolPaths);
        Assert.AreEqual("ansi", target.CodeFormatter.SqlDialect);
    }
}
