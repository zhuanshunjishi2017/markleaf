using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class CaptionInputDialog : Form
{
    private readonly TextBox _caption = new()
    {
        Dock = DockStyle.Top,
    };

    public CaptionInputDialog(string initial)
    {
        Text = Loc.Get("dialog.captionTitle");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _caption.Text = initial;

        var okButton = new Button
        {
            AutoSize = true,
            Text = Loc.Get("common.ok"),
            DialogResult = DialogResult.OK,
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            Text = Loc.Get("common.cancel"),
            DialogResult = DialogResult.Cancel,
        };

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
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(this.ScaleForDpi(8)),
            MinimumSize = new Size(this.ScaleForDpi(280), 0),
        };
        content.Controls.Add(_caption, 0, 0);
        content.Controls.Add(buttons, 0, 1);
        Controls.Add(content);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string Caption => _caption.Text.Trim();
}
