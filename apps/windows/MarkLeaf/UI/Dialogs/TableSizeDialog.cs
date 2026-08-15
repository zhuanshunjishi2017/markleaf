using MarkLeaf.Services;

namespace MarkLeaf.UI.Dialogs;

/// <summary>
/// 插入表格的尺寸选择对话框：提供 10×10 网格悬停预览 + 点击提交（类 Word），
/// 以及行数/列数自定义输入（1..100）。
/// </summary>
internal sealed class TableSizeDialog : Form
{
    private const int GridSize = 10;

    public int Rows { get; private set; } = 3;

    public int Columns { get; private set; } = 3;

    private readonly Label _sizeLabel;
    private readonly TableSizeGrid _grid;
    private readonly NumericUpDown _rowsInput;
    private readonly NumericUpDown _colsInput;

    public TableSizeDialog()
    {
        _sizeLabel = new Label
        {
            Text = Loc.Get("dialog.tableSizeTitle"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _grid = new TableSizeGrid(GridSize);
        _rowsInput = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 3 };
        _colsInput = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 3 };

        var okButton = new Button { Text = Loc.Get("common.ok"), AutoSize = true, FlatStyle = FlatStyle.System };
        var cancelButton = new Button { Text = Loc.Get("common.cancel"), AutoSize = true, FlatStyle = FlatStyle.System };

        _grid.HoverChanged += (_, size) =>
        {
            _sizeLabel.Text = size.Rows > 0 && size.Columns > 0
                ? Loc.Format("dialog.tableSizeGrid", size.Rows, size.Columns)
                : Loc.Get("dialog.tableSizeTitle");
        };
        _grid.SizeCommitted += (_, size) =>
        {
            Rows = size.Rows;
            Columns = size.Columns;
            DialogResult = DialogResult.OK;
            Close();
        };
        okButton.Click += (_, _) =>
        {
            Rows = (int)_rowsInput.Value;
            Columns = (int)_colsInput.Value;
            DialogResult = DialogResult.OK;
            Close();
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var customPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
        };
        customPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        customPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        customPanel.Controls.Add(new Label
        {
            Text = Loc.Get("dialog.tableRows"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        customPanel.Controls.Add(_rowsInput, 1, 0);
        customPanel.Controls.Add(new Label
        {
            Text = Loc.Get("dialog.tableColumns"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 1);
        customPanel.Controls.Add(_colsInput, 1, 1);

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        buttonRow.Controls.Add(cancelButton);
        buttonRow.Controls.Add(okButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 12, 14, 10),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(_sizeLabel, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(customPanel, 0, 2);
        layout.Controls.Add(buttonRow, 0, 3);

        Controls.Add(layout);

        Text = Loc.Get("dialog.tableSizeTitle");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}

/// <summary>
/// 尺寸网格：悬停高亮 (0..hoverRow, 0..hoverCol)，点击提交。
/// </summary>
internal sealed class TableSizeGrid : Control
{
    private const int CellSize = 20;
    private const int CellGap = 2;

    private readonly int _gridSize;
    private int _hoverRow = -1;
    private int _hoverColumn = -1;

    public event EventHandler<(int Rows, int Columns)>? HoverChanged;
    public event EventHandler<(int Rows, int Columns)>? SizeCommitted;

    public TableSizeGrid(int gridSize)
    {
        _gridSize = Math.Max(1, gridSize);
        var extent = _gridSize * CellSize + (_gridSize - 1) * CellGap;
        Size = new Size(extent, extent);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw,
            true);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var fillBrush = new SolidBrush(SystemColors.Window);
        using var selectedBrush = new SolidBrush(SystemColors.Highlight);
        using var borderPen = new Pen(SystemColors.ControlDark);
        using var selectedPen = new Pen(SystemColors.Highlight);
        for (var row = 0; row < _gridSize; row++)
        {
            for (var col = 0; col < _gridSize; col++)
            {
                var rect = CellRect(row, col);
                var selected = row <= _hoverRow && col <= _hoverColumn;
                g.FillRectangle(selected ? selectedBrush : fillBrush, rect);
                g.DrawRectangle(selected ? selectedPen : borderPen, rect);
            }
        }
    }

    private Rectangle CellRect(int row, int col)
    {
        return new Rectangle(
            col * (CellSize + CellGap),
            row * (CellSize + CellGap),
            CellSize,
            CellSize);
    }

    private (int Row, int Col)? HitTest(Point point)
    {
        for (var row = 0; row < _gridSize; row++)
        {
            for (var col = 0; col < _gridSize; col++)
            {
                if (CellRect(row, col).Contains(point))
                {
                    return (row, col);
                }
            }
        }

        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit is not { } cell)
        {
            return;
        }

        if (cell.Row != _hoverRow || cell.Col != _hoverColumn)
        {
            _hoverRow = cell.Row;
            _hoverColumn = cell.Col;
            Invalidate();
            HoverChanged?.Invoke(this, (cell.Row + 1, cell.Col + 1));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverRow = -1;
        _hoverColumn = -1;
        Invalidate();
        HoverChanged?.Invoke(this, (0, 0));
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (HitTest(e.Location) is not { } cell)
        {
            return;
        }

        SizeCommitted?.Invoke(this, (cell.Row + 1, cell.Col + 1));
    }
}
