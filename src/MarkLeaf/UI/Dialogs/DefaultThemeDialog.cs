using MarkLeaf.Services;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class DefaultThemeDialog : Form
{
    private readonly ComboBox _lightCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _darkCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly (string Id, string DisplayName)[] _lightThemes;
    private readonly (string Id, string DisplayName)[] _darkThemes;

    public DefaultThemeDialog(string currentLightId, string currentDarkId)
    {
        _lightThemes = ColorThemeService.All
            .Where(t => !t.IsDark)
            .Select(t => (t.Id, t.DisplayName))
            .ToArray();
        _darkThemes = ColorThemeService.All
            .Where(t => t.IsDark)
            .Select(t => (t.Id, t.DisplayName))
            .ToArray();

        foreach (var (_, displayName) in _lightThemes)
            _lightCombo.Items.Add(displayName);
        foreach (var (_, displayName) in _darkThemes)
            _darkCombo.Items.Add(displayName);

        _lightCombo.SelectedIndex = Math.Max(0, FindIndex(_lightThemes, currentLightId));
        _darkCombo.SelectedIndex = Math.Max(0, FindIndex(_darkThemes, currentDarkId));

        Text = Loc.Get("prefs.appearance.defaultTheme");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var okButton = new Button { AutoSize = true, Text = Loc.Get("common.ok"), DialogResult = DialogResult.OK };
        var cancelButton = new Button { AutoSize = true, Text = Loc.Get("common.cancel"), DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, this.ScaleForDpi(7), 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(this.ScaleForDpi(8)),
            MinimumSize = new Size(this.ScaleForDpi(320), 0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lightCombo.Width = this.ScaleForDpi(183);
        _darkCombo.Width = this.ScaleForDpi(183);

        content.Controls.Add(new Label
        {
            AutoSize = true,
            Text = Loc.Get("prefs.appearance.defaultLightTheme"),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, this.ScaleForDpi(6), 0),
        }, 0, 0);
        content.Controls.Add(_lightCombo, 1, 0);
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Text = Loc.Get("prefs.appearance.defaultDarkTheme"),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, this.ScaleForDpi(6), this.ScaleForDpi(6), 0),
        }, 0, 1);
        content.Controls.Add(_darkCombo, 1, 1);
        content.Controls.Add(buttons, 1, 2);

        Controls.Add(content);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string LightThemeId => _lightCombo.SelectedIndex >= 0
        ? _lightThemes[_lightCombo.SelectedIndex].Id
        : "white-only";

    public string DarkThemeId => _darkCombo.SelectedIndex >= 0
        ? _darkThemes[_darkCombo.SelectedIndex].Id
        : "dark";

    private static int FindIndex((string Id, string)[] themes, string id)
    {
        for (var i = 0; i < themes.Length; i++)
        {
            if (string.Equals(themes[i].Id, id, StringComparison.Ordinal))
                return i;
        }
        return 0;
    }
}
