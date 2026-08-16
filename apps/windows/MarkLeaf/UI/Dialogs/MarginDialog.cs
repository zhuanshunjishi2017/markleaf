using MarkLeaf.Services;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class MarginDialog : Form
{
    private readonly NumericUpDown _top = CreateMarginInput();
    private readonly NumericUpDown _bottom = CreateMarginInput();
    private readonly NumericUpDown _left = CreateMarginInput();
    private readonly NumericUpDown _right = CreateMarginInput();

    public float MarginTop => (float)_top.Value;
    public float MarginBottom => (float)_bottom.Value;
    public float MarginLeft => (float)_left.Value;
    public float MarginRight => (float)_right.Value;

    public MarginDialog(float top, float bottom, float left, float right)
    {
        Text = Loc.Get("export.customMargin");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(this.ScaleForDpi(380), this.ScaleForDpi(170));

        _top.Value = (decimal)top;
        _bottom.Value = (decimal)bottom;
        _left.Value = (decimal)left;
        _right.Value = (decimal)right;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        grid.Controls.Add(CreateMarginCell(Loc.Get("export.marginTop"), _top), 0, 0);
        grid.Controls.Add(CreateMarginCell(Loc.Get("export.marginBottom"), _bottom), 1, 0);
        grid.Controls.Add(CreateMarginCell(Loc.Get("export.marginLeft"), _left), 0, 1);
        grid.Controls.Add(CreateMarginCell(Loc.Get("export.marginRight"), _right), 1, 1);

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

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(this.ScaleForDpi(12)),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(grid, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        Controls.Add(layout);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private static Control CreateMarginCell(string text, NumericUpDown input)
    {
        var cell = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        cell.Controls.Add(NewLabel(text));
        cell.Controls.Add(input);
        return cell;
    }

    private static NumericUpDown CreateMarginInput()
    {
        return new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            DecimalPlaces = 1,
            Increment = 1,
            Width = 70,
        };
    }

    private static Label NewLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 6, 3),
        };
    }
}
