namespace MarkLeaf.UI.Dialogs;

internal sealed class TextInputDialog : Form
{
    private readonly TextBox _input = new() { Dock = DockStyle.Top };

    public TextInputDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _input.Text = initialValue;
        _input.SelectAll();
        var okButton = new Button { AutoSize = true, Text = "确定", DialogResult = DialogResult.OK };
        var cancelButton = new Button { AutoSize = true, Text = "取消", DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);
        var content = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            MinimumSize = new Size(420, 0),
        };
        content.Controls.Add(new Label { AutoSize = true, Text = prompt, Padding = new Padding(0, 0, 0, 6) });
        content.Controls.Add(_input);
        content.Controls.Add(buttons);
        Controls.Add(content);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string InputText => _input.Text.Trim();
}
