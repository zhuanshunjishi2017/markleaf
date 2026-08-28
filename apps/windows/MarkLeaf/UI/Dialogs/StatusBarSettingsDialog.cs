using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class StatusBarSettingsDialog : Form
{
    private readonly CheckBox _sidebarToggleCheck = CreateCheckBox(Loc.Get("prefs.statusBar.sidebarToggle"));
    private readonly Label _commandStatusLabel = new()
    {
        Text = Loc.Get("prefs.statusBar.commandStatus"),
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Left,
    };
    private readonly ComboBox _commandStatusCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _wordCountCheck = CreateCheckBox(Loc.Get("prefs.statusBar.wordCount"));
    private readonly CheckBox _blockTypeCheck = CreateCheckBox(Loc.Get("prefs.statusBar.blockType"));
    private readonly CheckBox _positionCheck = CreateCheckBox(Loc.Get("prefs.statusBar.position"));
    private readonly CheckBox _encodingCheck = CreateCheckBox(Loc.Get("prefs.statusBar.encoding"));
    private readonly CheckBox _newLineCheck = CreateCheckBox(Loc.Get("prefs.statusBar.newLine"));
    private readonly CheckBox _modeToggleCheck = CreateCheckBox(Loc.Get("prefs.statusBar.modeToggle"));
    private readonly CheckBox _zoomCheck = CreateCheckBox(Loc.Get("prefs.statusBar.zoom"));
    private readonly Button _okButton = new() { Text = Loc.Get("common.ok"), FlatStyle = FlatStyle.System };
    private readonly Button _cancelButton = new() { Text = Loc.Get("common.cancel"), FlatStyle = FlatStyle.System };

    public StatusBarSettings Settings { get; private set; }

    public StatusBarSettingsDialog(StatusBarSettings settings)
    {
        Settings = settings.Clone();

        Text = Loc.Get("prefs.statusBar.title");
        BackColor = SystemColors.ControlLightLight;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(this.ScaleForDpi(320), this.ScaleForDpi(338));

        _commandStatusCombo.Items.Add(Loc.Get("prefs.statusBar.commandStatus.always"));
        _commandStatusCombo.Items.Add(Loc.Get("prefs.statusBar.commandStatus.temporary"));
        _commandStatusCombo.Items.Add(Loc.Get("prefs.statusBar.commandStatus.hidden"));

        LoadSettings();
        ApplyDpiSizes();

        _okButton.Click += (_, _) =>
        {
            SaveSettings();
            DialogResult = DialogResult.OK;
            Close();
        };
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(this.ScaleForDpi(18), this.ScaleForDpi(14), this.ScaleForDpi(18), this.ScaleForDpi(10)),
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddCheckBox(list, _sidebarToggleCheck, 0);
        list.Controls.Add(BuildCommandStatusPanel(), 0, 1);
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(30)));
        AddCheckBox(list, _wordCountCheck, 2);
        AddCheckBox(list, _blockTypeCheck, 3);
        AddCheckBox(list, _positionCheck, 4);
        AddCheckBox(list, _encodingCheck, 5);
        AddCheckBox(list, _newLineCheck, 6);
        AddCheckBox(list, _modeToggleCheck, 7);
        AddCheckBox(list, _zoomCheck, 8);
        list.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        list.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 9);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, this.ScaleForDpi(8), 0, 0),
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_okButton);
        list.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(38)));
        list.Controls.Add(buttons, 0, 10);

        Controls.Add(list);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void LoadSettings()
    {
        _sidebarToggleCheck.Checked = Settings.SidebarToggleVisible;
        _commandStatusCombo.SelectedIndex = Settings.CommandDisplayMode switch
        {
            StatusBarCommandDisplayMode.Temporary => 1,
            StatusBarCommandDisplayMode.Hidden => 2,
            _ => 0,
        };
        _wordCountCheck.Checked = Settings.WordCountVisible;
        _blockTypeCheck.Checked = Settings.BlockTypeVisible;
        _positionCheck.Checked = Settings.PositionVisible;
        _encodingCheck.Checked = Settings.EncodingVisible;
        _newLineCheck.Checked = Settings.NewLineVisible;
        _modeToggleCheck.Checked = Settings.ModeToggleVisible;
        _zoomCheck.Checked = Settings.ZoomVisible;
    }

    private void SaveSettings()
    {
        Settings.SidebarToggleVisible = _sidebarToggleCheck.Checked;
        Settings.CommandStatusVisible = true;
        Settings.CommandDisplayMode = _commandStatusCombo.SelectedIndex switch
        {
            1 => StatusBarCommandDisplayMode.Temporary,
            2 => StatusBarCommandDisplayMode.Hidden,
            _ => StatusBarCommandDisplayMode.Always,
        };
        Settings.WordCountVisible = _wordCountCheck.Checked;
        Settings.BlockTypeVisible = _blockTypeCheck.Checked;
        Settings.PositionVisible = _positionCheck.Checked;
        Settings.EncodingVisible = _encodingCheck.Checked;
        Settings.NewLineVisible = _newLineCheck.Checked;
        Settings.ModeToggleVisible = _modeToggleCheck.Checked;
        Settings.ZoomVisible = _zoomCheck.Checked;
    }

    private void ApplyDpiSizes()
    {
        var btnW = this.ScaleForDpi(86);
        var btnH = this.ScaleForDpi(26);
        _commandStatusCombo.Width = this.ScaleForDpi(138);
        _okButton.Width = btnW;
        _okButton.Height = btnH;
        _cancelButton.Width = btnW;
        _cancelButton.Height = btnH;
    }

    private static CheckBox CreateCheckBox(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.System,
    };

    private static void AddCheckBox(TableLayoutPanel panel, CheckBox checkBox, int row)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.Controls.Add(checkBox, 0, row);
    }

    private Control BuildCommandStatusPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_commandStatusLabel, 0, 0);
        panel.Controls.Add(_commandStatusCombo, 1, 0);
        return panel;
    }
}
