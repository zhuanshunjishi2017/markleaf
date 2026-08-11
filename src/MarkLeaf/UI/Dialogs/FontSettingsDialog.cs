using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class FontSettingsDialog : Form
{
    private readonly TextBox _cjkFontTextBox = new()
    {
        ReadOnly = true,
    };
    private readonly TextBox _westernFontTextBox = new()
    {
        ReadOnly = true,
    };
    private readonly NumericUpDown _fontSizeNumeric = new()
    {
        Minimum = 12,
        Maximum = 24,
        Increment = 1,
    };

    private readonly Button _okButton = new()
    { Text = Loc.Get("common.ok"), FlatStyle = FlatStyle.System };
    private readonly Button _cancelButton = new()
    { Text = Loc.Get("common.cancel"), FlatStyle = FlatStyle.System };

    public string CjkFontFamily => _cjkFontTextBox.Text;
    public string WesternFontFamily => _westernFontTextBox.Text;
    public int FontSize => (int)_fontSizeNumeric.Value;

    public FontSettingsDialog(string cjkFontFamily, string westernFontFamily, int fontSize)
    {
        _cjkFontTextBox.Text = cjkFontFamily;
        _westernFontTextBox.Text = westernFontFamily;
        _fontSizeNumeric.Value = fontSize;

        var textBoxWidth = this.ScaleForDpi(200);
        _cjkFontTextBox.Width = textBoxWidth;
        _westernFontTextBox.Width = textBoxWidth;

        Text = Loc.Get("prefs.editor.fontSettings.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = SystemColors.ControlLightLight;
        Size = new Size(this.ScaleForDpi(420), this.ScaleForDpi(230));

        var selectCjkButton = new Button
        { Text = Loc.Get("prefs.editor.fontSettings.select"), AutoSize = true, FlatStyle = FlatStyle.System };
        selectCjkButton.Click += (_, _) => SelectFont(_cjkFontTextBox);

        var selectWesternButton = new Button
        { Text = Loc.Get("prefs.editor.fontSettings.select"), AutoSize = true, FlatStyle = FlatStyle.System };
        selectWesternButton.Click += (_, _) => SelectFont(_westernFontTextBox);

        _okButton.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = SystemColors.ControlLightLight,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(11), this.ScaleForDpi(14), this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Row 0: CJK font
        panel.Controls.Add(NewLabel(Loc.Get("prefs.editor.fontSettings.cjkFont")), 0, 0);
        panel.Controls.Add(BuildFontRow(_cjkFontTextBox, selectCjkButton), 1, 0);

        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(Gap(), 1, 1);

        // Row 2: Western font
        panel.Controls.Add(NewLabel(Loc.Get("prefs.editor.fontSettings.westernFont")), 0, 2);
        panel.Controls.Add(BuildFontRow(_westernFontTextBox, selectWesternButton), 1, 2);

        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(Gap(), 1, 3);

        // Row 4: Font size
        panel.Controls.Add(NewLabel(Loc.Get("prefs.editor.fontSettings.fontSize")), 0, 4);
        panel.Controls.Add(_fontSizeNumeric, 1, 4);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.ControlLightLight }, 0, 5);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.ControlLightLight }, 1, 5);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            BackColor = SystemColors.ControlLightLight,
            Margin = new Padding(this.ScaleForDpi(20), this.ScaleForDpi(23), this.ScaleForDpi(20), 0),
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_okButton);

        var okW = this.ScaleForDpi(86);
        var okH = this.ScaleForDpi(26);
        _okButton.Width = okW;
        _okButton.Height = okH;
        _cancelButton.Width = okW;
        _cancelButton.Height = okH;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.ControlLightLight,
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(23)),
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(panel, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        Controls.Add(layout);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void SelectFont(TextBox targetTextBox)
    {
        using var dialog = new FontDialog
        {
            FontMustExist = true,
            AllowScriptChange = false,
            ShowColor = false,
            ShowEffects = false,
        };
        if (!string.IsNullOrWhiteSpace(targetTextBox.Text))
        {
            try { dialog.Font = new Font(targetTextBox.Text, (float)_fontSizeNumeric.Value); }
            catch { }
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            targetTextBox.Text = dialog.Font.Name;
            dialog.Font.Dispose();
        }
    }

    private static Control BuildFontRow(TextBox textBox, Button selectButton)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            BackColor = SystemColors.ControlLightLight,
        };
        row.Controls.Add(textBox);
        row.Controls.Add(selectButton);
        return row;
    }

    private Label NewLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            BackColor = SystemColors.ControlLightLight,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Bold),
            Padding = new Padding(this.ScaleForDpi(6), this.ScaleForDpi(6), 0, 0),
        };
    }

    private Control Gap()
    {
        return new Panel
        {
            Height = this.ScaleGapForDpi(),
            Width = 0,
            Dock = DockStyle.None,
            BackColor = SystemColors.ControlLightLight,
        };
    }
}
