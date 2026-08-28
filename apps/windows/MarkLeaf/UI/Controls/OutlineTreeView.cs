using MarkLeaf.Editor;
using MarkLeaf.Services;
using System.ComponentModel;
using System.Drawing.Drawing2D;

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

    private readonly MarkLeafScrollbar _scrollBar = new() { Dock = DockStyle.Right };
    private readonly List<OutlineNode> _roots = [];
    private readonly List<(OutlineNode Node, Rectangle Bounds)> _visibleRows = [];
    private Font _primaryFont = new("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _secondaryFont = new("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _selectedFont = new("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _arrowFont = new(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);

    // Theme colors (defaults match white theme).
    private Color _bgPrimary = Color.White;
    private Color _bgHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
    private Color _bgSelected = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private Color _bgSelectedHover = Color.FromArgb(0xD0, 0xD0, 0xD0);
    private Color _textPrimary = Color.Black;
    private Color _textSecondary = Color.FromArgb(0x6D, 0x6D, 0x6D);
    private Color _textTertiary = Color.FromArgb(0x6D, 0x6D, 0x6D);
    private Color _textSelected = Color.Black;
    private Color _icon = Color.FromArgb(0x80, 0x80, 0x80);
    private Color _iconSecondary = Color.FromArgb(0x80, 0x80, 0x80);
    private Color _iconSelected = Color.Black;

    private int? _selectedPosition;
    private int? _hoveredPosition;
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
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        Controls.Add(_scrollBar);
        _scrollBar.Scroll += (_, _) => Invalidate();
        ConfigureTypography(DeviceDpi);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoHideScrollbar
    {
        get => _scrollBar.AutoHide;
        set
        {
            _scrollBar.AutoHide = value;
            Invalidate();
        }
    }

    public event EventHandler<int>? NodeActivated;

    public event EventHandler<Point>? ContextMenuRequested;

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

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) _bgPrimary = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("bg-selected", out c)) _bgSelected = c;
        if (colors.TryGetValue("bg-selected-hover", out c)) _bgSelectedHover = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        if (colors.TryGetValue("text-secondary", out c)) _textSecondary = c;
        if (colors.TryGetValue("text-tertiary", out c)) _textTertiary = c;
        if (colors.TryGetValue("text-selected", out c)) _textSelected = c;
        if (colors.TryGetValue("icon", out c)) _icon = c;
        if (colors.TryGetValue("icon-selected", out c)) _iconSelected = c;
        if (colors.TryGetValue("icon-secondary", out c)) _iconSecondary = c;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        _scrollBar.ApplyThemeColors(colors);
        Invalidate();
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

    public void SetFlatItems(IReadOnlyList<EditorOutlineItem> items)
    {
        _roots.Clear();
        foreach (var item in items)
        {
            _roots.Add(new OutlineNode(item, 0));
        }

        UpdateScrollBar();
        EnsureSelectionVisible();
        Invalidate();
    }

    public void ExpandAll()
    {
        SetExpandedRecursively(_roots, expanded: true);
        UpdateScrollBar();
        EnsureSelectionVisible();
        Invalidate();
    }

    public void CollapseAll()
    {
        SetExpandedRecursively(_roots, expanded: false);
        UpdateScrollBar();
        Invalidate();
    }

    public bool RevealPosition(int position)
    {
        var found = false;
        foreach (var root in _roots)
        {
            if (ExpandPathToPosition(root, position))
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            return false;
        }

        _selectedPosition = position;
        UpdateScrollBar();
        EnsureSelectionVisible();
        Invalidate();
        return true;
    }

    public void ConfigureTypography(int dpi)
    {
        var previousPrimary = _primaryFont;
        var previousSecondary = _secondaryFont;
        var previousSelected = _selectedFont;
        var previousArrowFont = _arrowFont;
        _primaryFont = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _secondaryFont = new Font("Microsoft YaHei", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _selectedFont = new Font("Microsoft YaHei", 10F, FontStyle.Bold, GraphicsUnit.Point);
        _arrowFont = new Font(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);
        _primaryRowHeight = (int)Math.Ceiling(_primaryFont.GetHeight(dpi) * 1.75F);
        _secondaryRowHeight = (int)Math.Ceiling(_secondaryFont.GetHeight(dpi) * 1.75F);
        previousPrimary.Dispose();
        previousSecondary.Dispose();
        previousSelected.Dispose();
        previousArrowFont.Dispose();
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
                Loc.Get("sidebar.noOutline"),
                _secondaryFont,
                new Rectangle(
                    this.ScaleForDpi(24),
                    0,
                    Math.Max(0, ClientSize.Width
                        - (_scrollBar.Visible ? _scrollBar.Width : 0)
                        - this.ScaleForDpi(24)
                        - ContentRightPadding(this.ScaleForDpi(4))),
                    _secondaryRowHeight));
            return;
        }
        BuildVisibleRows();
        foreach (var (node, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }

            var isSelected = node.Item.Position == _selectedPosition;
            var isHovered = node.Item.Position == _hoveredPosition;
            var rightPadding = ContentRightPadding(this.ScaleForDpi(4));
            var bgBounds = new Rectangle(
                bounds.X + this.ScaleForDpi(4), bounds.Y,
                Math.Max(0, bounds.Width - this.ScaleForDpi(4) - rightPadding), bounds.Height);
            var indent = this.ScaleForDpi(18) * node.Depth;
            var expanderBounds = new Rectangle(this.ScaleForDpi(8) + indent, bounds.Top, this.ScaleForDpi(16), bounds.Height);
            if (node.Children.Count > 0)
            {
                DrawExpander(eventArgs.Graphics, expanderBounds, node.Expanded, isSelected);
            }

            var textBounds = new Rectangle(
                expanderBounds.Right,
                bounds.Top,
                Math.Max(
                    0,
                    ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0)
                        - expanderBounds.Right - ContentRightPadding(this.ScaleForDpi(4))),
                bounds.Height);
            DrawNodeText(
                eventArgs.Graphics,
                string.IsNullOrWhiteSpace(node.Item.Text) ? Loc.Get("sidebar.untitled") : node.Item.Text,
                isSelected ? _selectedFont : (node.Item.Level <= 2 ? _primaryFont : _secondaryFont),
                textBounds,
                isSelected || isHovered ? null : _textSecondary);
        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        Focus();
        if (eventArgs.Button == MouseButtons.Right)
        {
            ContextMenuRequested?.Invoke(this, PointToScreen(eventArgs.Location));
            return;
        }
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }
        var row = HitTestRow(eventArgs.Location);
        if (row is null)
        {
            return;
        }

        var (node, _) = row.Value;
        var expanderRight = this.ScaleForDpi(24) + this.ScaleForDpi(18) * node.Depth;
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

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        var row = HitTestRow(eventArgs.Location);
        var hoveredPosition = row?.Node.Item.Position;
        if (_hoveredPosition != hoveredPosition)
        {
            _hoveredPosition = hoveredPosition;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (_hoveredPosition is not null)
        {
            _hoveredPosition = null;
            Invalidate();
        }
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
            _selectedFont.Dispose();
            _arrowFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void SelectNode(OutlineNode node) => SelectedPosition = node.Item.Position;

    private void BuildVisibleRows()
    {
        _visibleRows.Clear();
        var top = 0 - _scrollBar.Value;
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

    private int ContentRightPadding(int defaultPadding)
        => _scrollBar.Visible && _scrollBar.AutoHide ? 0 : defaultPadding;

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

    private static void SetExpandedRecursively(IEnumerable<OutlineNode> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            node.Expanded = expanded;
            SetExpandedRecursively(node.Children, expanded);
        }
    }

    private static bool ExpandPathToPosition(OutlineNode node, int position)
    {
        if (node.Item.Position == position)
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (!ExpandPathToPosition(child, position))
            {
                continue;
            }

            node.Expanded = true;
            return true;
        }

        return false;
    }

    private int GetContentHeight() => 0 + EnumerateVisibleNodes().Sum(GetRowHeight);
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
        TextRenderer.DrawText(
            graphics,
            expanded ? SystemIconProvider.DownArrow : SystemIconProvider.RightArrow,
            _arrowFont,
            bounds,
            _iconSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void DrawNodeText(Graphics graphics, string text, Font font, Rectangle bounds, Color? color = null)
    {
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            bounds,
            color ?? ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

}
