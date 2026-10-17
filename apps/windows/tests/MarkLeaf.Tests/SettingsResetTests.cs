using MarkLeaf.Services.Settings;
using MarkLeaf.Commands;
using MarkLeaf.UI.Dialogs;
using System.Reflection;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class SettingsResetTests
{
    [TestMethod]
    public void PreferencesApply_DoesNotTakeOwnershipOfCodeFormatterSettings()
    {
        var target = new AppSettings
        {
            Export = new ExportSettings { Format = "html", Landscape = true },
            Shortcut = new ShortcutSettings(),
            CodeFormatter = new CodeFormatterSettings { SqlDialect = "postgres" },
        };
        target.Shortcut.Overrides[nameof(AppCommand.ToggleBold)] = "Ctrl+Alt+B";
        target.CodeFormatter.ToolPaths["black"] = "C:\\Tools\\black.exe";
        var codeFormatterBefore = target.CodeFormatter;

        var defaults = AppSettings.CreateDefaults();
        defaults.Export.Format = "pdf";
        defaults.CodeFormatter.SqlDialect = "ansi";
        SettingsReset.Apply(target, defaults);

        Assert.AreEqual("pdf", target.Export.Format);
        Assert.IsFalse(target.Export.Landscape);
        Assert.IsEmpty(target.Shortcut.Overrides);
        Assert.AreSame(codeFormatterBefore, target.CodeFormatter);
        Assert.AreEqual("postgres", target.CodeFormatter.SqlDialect);
        Assert.AreEqual("C:\\Tools\\black.exe", target.CodeFormatter.ToolPaths["black"]);
    }

    [TestMethod]
    public void CodeFormatterApply_ResetsItsOwnedSettings()
    {
        var target = new CodeFormatterSettings
        {
            SqlDialect = "postgres",
        };
        target.ToolPaths["black"] = "C:\\Tools\\black.exe";
        target.ToolPaths["rustfmt"] = "C:\\Tools\\rustfmt.exe";

        CodeFormatterSettingsReset.Apply(target, AppSettings.CreateDefaults().CodeFormatter);

        Assert.IsEmpty(target.ToolPaths);
        Assert.AreEqual("ansi", target.SqlDialect);
    }

    [TestMethod]
    public void PreferencesCopySettings_PreservesCodeFormatterOwnership()
    {
        var source = new AppSettings
        {
            Editor = new EditorSettings { SourceFontSize = 22 },
            CodeFormatter = new CodeFormatterSettings { SqlDialect = "postgres" },
        };
        source.CodeFormatter.ToolPaths["black"] = "C:\\Tools\\dialog-black.exe";
        var target = new AppSettings
        {
            Editor = new EditorSettings { SourceFontSize = 14 },
            CodeFormatter = new CodeFormatterSettings { SqlDialect = "ansi" },
        };
        target.CodeFormatter.ToolPaths["black"] = "C:\\Tools\\target-black.exe";

        typeof(PreferencesDialog)
            .GetMethod("CopySettings", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [source, target]);

        Assert.AreEqual(22, target.Editor.SourceFontSize);
        Assert.AreEqual("ansi", target.CodeFormatter.SqlDialect);
        Assert.AreEqual("C:\\Tools\\target-black.exe", target.CodeFormatter.ToolPaths["black"]);
    }
}
