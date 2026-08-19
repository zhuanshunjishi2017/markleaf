using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class FootnoteInputDialog : Form
{
    private readonly TextBox _label = new()
    {
        Dock = DockStyle.Top,
        PlaceholderText = "1",
    };

    private readonly TextBox _note = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
    };

    public FootnoteInputDialog()
    {
        Text = Loc.Get("dialog.footnoteTitle");
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(this.ScaleForDpi(420), this.ScaleForDpi(260));

        var labelLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = Loc.Get("dialog.footnoteLabel"),
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(3)),
        };
        labelLabel.UseMnemonic = true;

        var noteLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = Loc.Get("dialog.footnoteText"),
            Padding = new Padding(0, this.ScaleForDpi(7), 0, this.ScaleForDpi(3)),
        };
        noteLabel.UseMnemonic = true;

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

        void UpdateOkState() =>
            okButton.Enabled = !string.IsNullOrWhiteSpace(_label.Text) && !string.IsNullOrWhiteSpace(_note.Text);

        _label.TextChanged += (_, _) => UpdateOkState();
        _note.TextChanged += (_, _) => UpdateOkState();

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
            RowCount = 5,
            Padding = new Padding(this.ScaleForDpi(8)),
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(labelLabel, 0, 0);
        content.Controls.Add(_label, 0, 1);
        content.Controls.Add(noteLabel, 0, 2);
        content.Controls.Add(_note, 0, 3);
        content.Controls.Add(buttons, 0, 4);
        Controls.Add(content);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string FootnoteLabel => _label.Text.Trim();

    public string FootnoteText => _note.Text.Trim();
}
