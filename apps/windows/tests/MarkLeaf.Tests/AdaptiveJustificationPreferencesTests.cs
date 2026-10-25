using System.Text.Json;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class AdaptiveJustificationPreferencesTests
{
    [TestMethod]
    public async Task LegacySettingsDefaultOnAndExplicitFalseSurvivesSharedSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-adaptive-test", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        Directory.CreateDirectory(root);
        try
        {
            Assert.IsTrue(new EditorSettings().VisualAdaptiveJustification);
            await File.WriteAllTextAsync(
                file,
                "{\"schemaVersion\":" + AppSettings.CurrentSchemaVersion + ",\"editor\":{\"visualFontSize\":19}}");
            var service = new JsonSettingsService(file, new TestLogger());
            var settings = await service.LoadAsync();
            Assert.IsTrue(settings.Editor.VisualAdaptiveJustification);
            settings.Editor.VisualAdaptiveJustification = false;
            await service.SaveAsync(settings, SettingsSaveScope.Shared);
            var saved = await new JsonSettingsService(file, new TestLogger()).LoadAsync();
            Assert.IsFalse(saved.Editor.VisualAdaptiveJustification);
            Assert.AreEqual(19, saved.Editor.VisualFontSize);
        }
        finally { Directory.Delete(root, true); }
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void VisualCheckboxAppliesOnOkAndCancelPreservesSavedChoice(bool initial)
    {
        LocTestHelper.EnsureInitialized();
        var resources = Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "Resources");
        StyleService.Initialize(Path.Combine(resources, "Styles"));
        ColorThemeService.Initialize(Path.Combine(resources, "Styles"));
        var settings = new AppSettings { Editor = new EditorSettings { VisualAdaptiveJustification = initial } };
        using (var dialog = new PreferencesDialog(settings))
        {
            dialog.Show();
            var check = Checkbox(dialog);
            Assert.AreEqual(initial, check.Checked);
            check.Checked = !initial;
            Assert.AreEqual(initial, settings.Editor.VisualAdaptiveJustification);
            ((Button)dialog.CancelButton!).PerformClick();
            Assert.AreEqual(initial, settings.Editor.VisualAdaptiveJustification);
        }
        using (var dialog = new PreferencesDialog(settings))
        {
            dialog.Show();
            var check = Checkbox(dialog);
            Assert.AreEqual(initial, check.Checked);
            check.Checked = !initial;
            ((Button)dialog.AcceptButton!).PerformClick();
            Assert.AreEqual(DialogResult.OK, dialog.DialogResult);
            Assert.AreEqual(!initial, settings.Editor.VisualAdaptiveJustification);
        }
    }

    private static CheckBox Checkbox(PreferencesDialog dialog)
    {
        Descendants(dialog).OfType<PreferencesTabBar>().First().SelectedIndex = 2;
        return Descendants(dialog).OfType<CheckBox>().Single(c => c.Text == Loc.Get("prefs.editor.visualAdaptiveJustification"));
    }
    private static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(control => new[] { control }.Concat(Descendants(control)));
}
