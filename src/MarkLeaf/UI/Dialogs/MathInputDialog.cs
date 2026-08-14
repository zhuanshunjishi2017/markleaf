using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class MathInputDialog : Form
{
    private readonly TextBox _latex = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Vertical,
        PlaceholderText = "x^2 + y^2",
    };

    public MathInputDialog(bool isBlock)
    {
        Text = isBlock ? Loc.Get("dialog.mathBlockTitle") : Loc.Get("dialog.mathInlineTitle");
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(this.ScaleForDpi(460), this.ScaleForDpi(320));

        var label = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = Loc.Get("dialog.mathLatexLabel"),
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(3)),
        };
        label.UseMnemonic = true;

        var okButton = new Button
        {
            AutoSize = true,
            Text = Loc.Get("common.ok"),
            DialogResult = DialogResult.OK,
            Enabled = false,
        };
        var cancelButton = new Button
        {
            AutoSize = true,
            Text = Loc.Get("common.cancel"),
            DialogResult = DialogResult.Cancel,
        };
        _latex.TextChanged += (_, _) => okButton.Enabled = !string.IsNullOrWhiteSpace(_latex.Text);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, this.ScaleForDpi(7), 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(this.ScaleForDpi(8)),
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(label, 0, 0);
        content.Controls.Add(_latex, 0, 1);
        content.Controls.Add(buttons, 0, 2);
        Controls.Add(content);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string Latex => _latex.Text.Trim();
}
