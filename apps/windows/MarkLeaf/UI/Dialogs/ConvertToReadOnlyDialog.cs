using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class ConvertToReadOnlyDialog : Form
{
    public ConvertToReadOnlyDialog(string message)
    {
        Text = MarkLeaf.Services.Loc.Get("document.convertToReadOnlyTitle");
        BackColor = DialogColors.Secondary;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Padding = new Padding(this.ScaleForDpi(9));

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(this.ScaleForDpi(8)),
            MinimumSize = new Size(this.ScaleForDpi(360), 0),
        };
        content.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(this.ScaleForDpi(360), 0),
            Text = message,
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(7)),
        });

        var convertButton = new Button
        {
            AutoSize = true,
            Text = MarkLeaf.Services.Loc.Get("document.convertToReadOnlyConfirm"),
            DialogResult = DialogResult.OK,
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            Text = MarkLeaf.Services.Loc.Get("common.cancel"),
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
        buttons.Controls.Add(convertButton);
        content.Controls.Add(buttons);
        Controls.Add(content);
        AcceptButton = convertButton;
        CancelButton = cancelButton;
    }
}
