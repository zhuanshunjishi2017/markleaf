using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class PdfHeaderFooterDialog : Form
{
    private readonly TextBox _header = new() { Multiline = true, AcceptsReturn = true };
    private readonly TextBox _footer = new() { Multiline = true, AcceptsReturn = true };

    public string HeaderText => _header.Text;

    public string FooterText => _footer.Text;

    public PdfHeaderFooterDialog(string header, string footer)
    {
        Text = Loc.Get("export.customHeaderFooter");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(this.ScaleForDpi(420), this.ScaleForDpi(260));

        _header.Text = header;
        _footer.Text = footer;
        _header.PlaceholderText = Loc.Get("export.headerPlaceholder");
        _footer.PlaceholderText = Loc.Get("export.footerPlaceholder");

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(this.ScaleForDpi(12)),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(72)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(10)));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _header.Dock = DockStyle.Fill;
        _footer.Dock = DockStyle.Fill;
        grid.Controls.Add(NewLabel(Loc.Get("export.htmlHeader")), 0, 0);
        grid.Controls.Add(_header, 1, 0);
        grid.Controls.Add(NewLabel(Loc.Get("export.htmlFooter")), 0, 2);
        grid.Controls.Add(_footer, 1, 2);

        var okButton = new Button
        {
            Text = Loc.Get("common.ok"),
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.System,
            MinimumSize = new Size(this.ScaleForDpi(80), this.ScaleForDpi(26)),
        };
        var cancelButton = new Button
        {
            Text = Loc.Get("common.cancel"),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.System,
            MinimumSize = new Size(this.ScaleForDpi(80), this.ScaleForDpi(26)),
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, this.ScaleForDpi(8), 0, 0),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);
        grid.Controls.Add(buttons, 1, 3);

        Controls.Add(grid);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private static Label NewLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(0, 3, 6, 3),
        };
    }
}
