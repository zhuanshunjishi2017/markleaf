using MarkLeaf.Editor;
using System.ComponentModel;

namespace MarkLeaf.UI.Controls;

internal sealed class OutlineTreeView : Control
{
    private sealed class OutlineNode(EditorOutlineItem item, int depth)
    {
        public EditorOutlineItem Item { get; } = item;
        public int Depth { get; } = depth;
        public bool Expanded { get; set; } = true;
        public List<OutlineNode> Children { get; } = [];
    }

    private readonly VScrollBar _scrollBar = new() { Dock = DockStyle.Right };
    private readonly List<OutlineNode> _roots = [];
    private readonly List<(OutlineNode Node, Rectangle Bounds)> _visibleRows = [];
    private Font _primaryFont = new("宋体-简", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _secondaryFont = new("宋体-简", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private int? _selectedPosition;
    private int _primaryRowHeight;
    private int _secondaryRowHeight;

    public OutlineTreeView()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        Dock = DockStyle.Fill;
        TabStop = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
        Controls.Add(_scrollBar);
        _scrollBar.Scroll += (_, _) => Invalidate();
        ConfigureTypography(DeviceDpi);
    }

    public event EventHandler<int>? NodeActivated;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? SelectedPosition
    {
        get => _selectedPosition;
        set
        {
            _selectedPosition = value;
            EnsureSelectionVisible();
            Invalidate();
        }
    }

    public void SetItems(IReadOnlyList<EditorOutlineItem> items)
    {
        _roots.Clear();
        var parents = new Stack<OutlineNode>();
        foreach (var item in items)
        {
            while (parents.Count > 0 && parents.Peek().Item.Level >= item.Level)
            {
                parents.Pop();
            }

            var node = new OutlineNode(item, parents.Count);
            if (parents.Count == 0)
            {
                _roots.Add(node);
            }
            else
            {
                parents.Peek().Children.Add(node);
            }
            parents.Push(node);
        }

        UpdateScrollBar();
        EnsureSelectionVisible();
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        var previousPrimary = _primaryFont;
        var previousSecondary = _secondaryFont;
        _primaryFont = new Font("宋体-简", 11F, FontStyle.Regular, GraphicsUnit.Point);
        _secondaryFont = new Font("宋体-简", 11F, FontStyle.Regular, GraphicsUnit.Point);
        _primaryRowHeight = (int)Math.Ceiling(_primaryFont.GetHeight(dpi) * 1.75F);
        _secondaryRowHeight = (int)Math.Ceiling(_secondaryFont.GetHeight(dpi) * 1.75F);
        previousPrimary.Dispose();
        previousSecondary.Dispose();
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.Clear(BackColor);
        if (_roots.Count == 0)
        {
            DrawNodeText(
                eventArgs.Graphics,
                "暂无文档大纲",
                _secondaryFont,
                new Rectangle(ScaleForDpi(20), 0, ClientSize.Width - ScaleForDpi(24), _secondaryRowHeight));
            return;
        }
        BuildVisibleRows();
        foreach (var (node, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }

            var selected = node.Item.Position == _selectedPosition;
            if (selected)
            {
                using var selectionBrush = new SolidBrush(Color.FromArgb(232, 232, 232));
                eventArgs.Graphics.FillRectangle(selectionBrush, bounds);
            }

            var indent = ScaleForDpi(18) * node.Depth;
            var expanderBounds = new Rectangle(ScaleForDpi(4) + indent, bounds.Top, ScaleForDpi(16), bounds.Height);
            if (node.Children.Count > 0)
            {
                DrawExpander(eventArgs.Graphics, expanderBounds, node.Expanded, selected);
            }

            var textBounds = new Rectangle(
                expanderBounds.Right,
                bounds.Top,
                Math.Max(
                    0,
                    ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0)
                        - expanderBounds.Right - ScaleForDpi(4)),
                bounds.Height);
            DrawNodeText(
                eventArgs.Graphics,
                string.IsNullOrWhiteSpace(node.Item.Text) ? "(无标题)" : node.Item.Text,
                node.Item.Level <= 2 ? _primaryFont : _secondaryFont,
                textBounds);
        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        Focus();
        var row = HitTestRow(eventArgs.Location);
        if (row is null)
        {
            return;
        }

        var (node, _) = row.Value;
        var expanderRight = ScaleForDpi(20) + ScaleForDpi(18) * node.Depth;
        if (eventArgs.X <= expanderRight && node.Children.Count > 0)
        {
            node.Expanded = !node.Expanded;
            UpdateScrollBar();
            Invalidate();
            return;
        }

        SelectedPosition = node.Item.Position;
        NodeActivated?.Invoke(this, node.Item.Position);
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        if (!_scrollBar.Visible)
        {
            return;
        }
        var delta = eventArgs.Delta > 0 ? -_scrollBar.SmallChange : _scrollBar.SmallChange;
        _scrollBar.Value = Math.Clamp(_scrollBar.Value + delta, 0, GetMaximumScrollValue());
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        var nodes = EnumerateVisibleNodes().ToArray();
        if (nodes.Length == 0)
        {
            return;
        }
        var index = Array.FindIndex(nodes, node => node.Item.Position == _selectedPosition);
        switch (eventArgs.KeyCode)
        {
            case Keys.Up:
                SelectNode(nodes[Math.Max(0, index <= 0 ? 0 : index - 1)]);
                break;
            case Keys.Down:
                SelectNode(nodes[Math.Min(nodes.Length - 1, index < 0 ? 0 : index + 1)]);
                break;
            case Keys.Left when index >= 0 && nodes[index].Children.Count > 0:
                nodes[index].Expanded = false;
                UpdateScrollBar();
                Invalidate();
                break;
            case Keys.Right when index >= 0 && nodes[index].Children.Count > 0:
                nodes[index].Expanded = true;
                UpdateScrollBar();
                Invalidate();
                break;
            case Keys.Enter when index >= 0:
                NodeActivated?.Invoke(this, nodes[index].Item.Position);
                break;
            default:
                return;
        }
        eventArgs.Handled = true;
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        UpdateScrollBar();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _primaryFont.Dispose();
            _secondaryFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void SelectNode(OutlineNode node) => SelectedPosition = node.Item.Position;

    private void BuildVisibleRows()
    {
        _visibleRows.Clear();
        var top = -_scrollBar.Value;
        foreach (var node in EnumerateVisibleNodes())
        {
            var height = GetRowHeight(node);
            _visibleRows.Add((node, new Rectangle(
                0,
                top,
                ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0),
                height)));
            top += height;
        }
    }

    private (OutlineNode Node, Rectangle Bounds)? HitTestRow(Point location)
    {
        BuildVisibleRows();
        foreach (var row in _visibleRows)
        {
            if (row.Bounds.Contains(location))
            {
                return row;
            }
        }
        return null;
    }

    private IEnumerable<OutlineNode> EnumerateVisibleNodes()
    {
        foreach (var root in _roots)
        {
            foreach (var node in EnumerateVisibleNodes(root))
            {
                yield return node;
            }
        }
    }

    private static IEnumerable<OutlineNode> EnumerateVisibleNodes(OutlineNode node)
    {
        yield return node;
        if (!node.Expanded)
        {
            yield break;
        }
        foreach (var child in node.Children)
        {
            foreach (var descendant in EnumerateVisibleNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private int GetContentHeight() => EnumerateVisibleNodes().Sum(GetRowHeight);
    private int GetRowHeight(OutlineNode node) => node.Item.Level <= 2 ? _primaryRowHeight : _secondaryRowHeight;

    private void UpdateScrollBar()
    {
        var contentHeight = GetContentHeight();
        _scrollBar.Visible = contentHeight > ClientSize.Height;
        _scrollBar.Minimum = 0;
        _scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
        _scrollBar.SmallChange = Math.Max(1, _secondaryRowHeight);
        _scrollBar.Maximum = Math.Max(0, contentHeight - 1);
        _scrollBar.Value = Math.Min(_scrollBar.Value, GetMaximumScrollValue());
    }

    private int GetMaximumScrollValue()
    {
        return Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);
    }

    private void EnsureSelectionVisible()
    {
        if (_selectedPosition is null || !IsHandleCreated)
        {
            return;
        }
        BuildVisibleRows();
        var selected = _visibleRows.FirstOrDefault(row => row.Node.Item.Position == _selectedPosition);
        if (selected.Node is null)
        {
            return;
        }
        if (selected.Bounds.Top < 0)
        {
            _scrollBar.Value = Math.Clamp(_scrollBar.Value + selected.Bounds.Top, 0, GetMaximumScrollValue());
        }
        else if (selected.Bounds.Bottom > ClientSize.Height)
        {
            _scrollBar.Value = Math.Clamp(
                _scrollBar.Value + selected.Bounds.Bottom - ClientSize.Height,
                0,
                GetMaximumScrollValue());
        }
    }

    private void DrawExpander(Graphics graphics, Rectangle bounds, bool expanded, bool selected)
    {
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var size = ScaleForDpi(4);
        var points = expanded
            ? new[]
            {
                new Point(center.X - size, center.Y - size / 2),
                new Point(center.X + size, center.Y - size / 2),
                new Point(center.X, center.Y + size),
            }
            : new[]
            {
                new Point(center.X - size / 2, center.Y - size),
                new Point(center.X - size / 2, center.Y + size),
                new Point(center.X + size, center.Y),
            };
        using var brush = new SolidBrush(SystemColors.ControlDarkDark);
        graphics.FillPolygon(brush, points);
    }

    private void DrawNodeText(Graphics graphics, string text, Font font, Rectangle bounds)
    {
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            bounds,
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private int ScaleForDpi(int value) => (int)Math.Round(value * DeviceDpi / 96d);
}
