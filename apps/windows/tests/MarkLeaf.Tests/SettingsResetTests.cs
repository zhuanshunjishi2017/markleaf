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
            MainWindow = new WindowSettings { Width = 1234, Height = 567 },
            Workspace = new WorkspaceSettings(),
            Export = new ExportSettings { Format = "html", Landscape = true },
            Shortcut = new ShortcutSettings(),
            CodeFormatter = new CodeFormatterSettings { SqlDialect = "postgres" },
        };
        target.Workspace.RecentFiles.Add("C:\\Docs\\kept.md");
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
    public void SettingsReset_PreservesShortcutManagerObjectOwnership()
    {
        var target = new AppSettings
        {
            MainWindow = new WindowSettings { Width = 1234, Height = 567 },
            Workspace = new WorkspaceSettings(),
        };
        target.Workspace.RecentFiles.Add("C:\\Docs\\kept.md");
        var targetShortcut = target.Shortcut;
        target.Shortcut.Overrides[nameof(AppCommand.ToggleBold)] = "Ctrl+Alt+B";

        SettingsReset.Apply(target, AppSettings.CreateDefaults());

        Assert.AreSame(targetShortcut, target.Shortcut);
        Assert.IsEmpty(target.Shortcut.Overrides);
        Assert.AreEqual(1234, target.MainWindow.Width);
        Assert.AreEqual(567, target.MainWindow.Height);
        Assert.AreEqual("C:\\Docs\\kept.md", target.Workspace.RecentFiles.Single());
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
            MainWindow = new WindowSettings { Width = 1234, Height = 567 },
            Workspace = new WorkspaceSettings(),
            Editor = new EditorSettings { SourceFontSize = 14 },
            Shortcut = new ShortcutSettings(),
            CodeFormatter = new CodeFormatterSettings { SqlDialect = "ansi" },
        };
        target.Workspace.RecentFiles.Add("C:\\Docs\\target-kept.md");
        target.CodeFormatter.ToolPaths["black"] = "C:\\Tools\\target-black.exe";
        var targetShortcut = target.Shortcut;

        typeof(PreferencesDialog)
            .GetMethod("CopySettings", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [source, target]);

        Assert.AreEqual(22, target.Editor.SourceFontSize);
        Assert.AreSame(targetShortcut, target.Shortcut);
        Assert.IsEmpty(target.Shortcut.Overrides);
        Assert.AreEqual(1234, target.MainWindow.Width);
        Assert.AreEqual(567, target.MainWindow.Height);
        Assert.AreEqual("C:\\Docs\\target-kept.md", target.Workspace.RecentFiles.Single());
        Assert.AreEqual("ansi", target.CodeFormatter.SqlDialect);
        Assert.AreEqual("C:\\Tools\\target-black.exe", target.CodeFormatter.ToolPaths["black"]);
    }
}
