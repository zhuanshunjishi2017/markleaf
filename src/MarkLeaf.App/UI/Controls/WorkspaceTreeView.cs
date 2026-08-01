using System.ComponentModel;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI.Controls;

internal sealed class WorkspaceTreeView : Control
{
    private sealed class WorkspaceNode(WorkspaceEntry entry, int depth)
    {
        public WorkspaceEntry Entry { get; set; } = entry;
        public int Depth { get; } = depth;
        public bool Expanded { get; set; }
        public bool Loaded { get; set; }
        public string? ErrorText { get; set; }
        public List<WorkspaceNode> Children { get; } = [];
    }

    private readonly VScrollBar _scrollBar = new() { Dock = DockStyle.Right };
    private readonly List<(WorkspaceNode Node, Rectangle Bounds)> _visibleRows = [];
    private Font _treeFont = new("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private WorkspaceNode? _root;
    private string? _placeholderText = "暂未打开工作区";
    private string? _selectedPath;
    private int _rowHeight;

    public WorkspaceTreeView()
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

    public event EventHandler<WorkspaceTreeNodeEventArgs>? NodeExpanding;
    public event EventHandler<WorkspaceTreeNodeEventArgs>? NodeActivated;
    public event EventHandler<WorkspaceTreeContextEventArgs>? NodeContextRequested;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedPath
    {
        get => _selectedPath;
        set
        {
            _selectedPath = value;
            EnsureSelectionVisible();
            Invalidate();
        }
    }

    [Browsable(false)]
    public bool HasRoot => _root is not null;

    public void ConfigureTypography(int dpi)
    {
        var previousFont = _treeFont;
        _treeFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _rowHeight = (int)Math.Ceiling(_treeFont.GetHeight(dpi) * 1.5F);
        previousFont.Dispose();
        UpdateScrollBar();
        Invalidate();
    }

    public void SetPlaceholder(string text)
    {
        _root = null;
        _placeholderText = text;
        _selectedPath = null;
        _scrollBar.Value = 0;
        UpdateScrollBar();
        Invalidate();
    }

    public void SetRoot(WorkspaceEntry entry)
    {
        _root = new WorkspaceNode(entry, 0);
        _placeholderText = null;
        _selectedPath = null;
        _scrollBar.Value = 0;
        UpdateScrollBar();
        Invalidate();
    }

    public void SetChildren(string directory, IReadOnlyList<WorkspaceEntry> entries)
    {
        var node = FindNode(directory);
        if (node is null || !node.Entry.IsDirectory)
        {
            return;
        }

        var existing = node.Children.ToDictionary(child => child.Entry.FullPath, StringComparer.OrdinalIgnoreCase);
        node.Children.Clear();
        foreach (var entry in entries)
        {
            var child = new WorkspaceNode(entry, node.Depth + 1);
            if (existing.TryGetValue(entry.FullPath, out var previous))
            {
                child.Expanded = previous.Expanded;
                child.Loaded = previous.Loaded;
                child.ErrorText = previous.ErrorText;
                child.Children.AddRange(previous.Children);
            }
            node.Children.Add(child);
        }
        node.Loaded = true;
        node.ErrorText = null;
        UpdateScrollBar();
        Invalidate();
    }

    public void SetLoadError(string directory, string message)
    {
        var node = FindNode(directory);
        if (node is null)
        {
            return;
        }
        node.Children.Clear();
        node.Loaded = true;
        node.ErrorText = message;
        UpdateScrollBar();
        Invalidate();
    }

    public void Expand(string path)
    {
        var node = FindNode(path);
        if (node is null || !node.Entry.IsDirectory)
        {
            return;
        }
        node.Expanded = true;
        UpdateScrollBar();
        Invalidate();
    }

    public IReadOnlyList<string> GetExpandedDirectories()
    {
        return EnumerateAllNodes()
            .Where(node => node.Entry.IsDirectory && node.Expanded)
            .Select(node => node.Entry.FullPath)
            .ToArray();
    }

    public bool ContainsPath(string path) => FindNode(path) is not null;

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.Clear(BackColor);
        if (_root is null)
        {
            DrawText(eventArgs.Graphics, _placeholderText ?? string.Empty,
                new Rectangle(ScaleForDpi(12), 0, Math.Max(0, ClientSize.Width - ScaleForDpi(24)), _rowHeight),
                SystemColors.GrayText);
            return;
        }

        BuildVisibleRows();
        foreach (var (node, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }
            if (PathEquals(node.Entry.FullPath, _selectedPath))
            {
                using var selectionBrush = new SolidBrush(Color.FromArgb(232, 232, 232));
                eventArgs.Graphics.FillRectangle(selectionBrush, bounds);
            }

            var indent = ScaleForDpi(18) * node.Depth;
            var expanderBounds = new Rectangle(ScaleForDpi(4) + indent, bounds.Top, ScaleForDpi(16), bounds.Height);
            if (node.Entry.IsDirectory)
            {
                DrawExpander(eventArgs.Graphics, expanderBounds, node.Expanded);
            }
            var textBounds = new Rectangle(
                expanderBounds.Right,
                bounds.Top,
                Math.Max(0, bounds.Width - expanderBounds.Right - ScaleForDpi(4)),
                bounds.Height);
            DrawText(eventArgs.Graphics, node.Entry.Name, textBounds, ForeColor);

        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        var row = HitTestRow(eventArgs.Location);
        if (row is null)
        {
            return;
        }
        Focus();
        var node = row.Value.Node;
        SelectedPath = node.Entry.FullPath;
        if (eventArgs.Button == MouseButtons.Right)
        {
            NodeContextRequested?.Invoke(this, new WorkspaceTreeContextEventArgs(
                node.Entry,
                PointToScreen(eventArgs.Location)));
            return;
        }
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        if (eventArgs.Clicks != 1)
        {
            return;
        }

        if (node.Entry.IsDirectory)
        {
            ToggleNode(node);
        }
        else
        {
            NodeActivated?.Invoke(this, new WorkspaceTreeNodeEventArgs(node.Entry));
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
        var index = Array.FindIndex(nodes, node => PathEquals(node.Entry.FullPath, _selectedPath));
        switch (eventArgs.KeyCode)
        {
            case Keys.Up:
                SelectedPath = nodes[Math.Max(0, index <= 0 ? 0 : index - 1)].Entry.FullPath;
                break;
            case Keys.Down:
                SelectedPath = nodes[Math.Min(nodes.Length - 1, index < 0 ? 0 : index + 1)].Entry.FullPath;
                break;
            case Keys.Left when index >= 0 && nodes[index].Entry.IsDirectory:
                nodes[index].Expanded = false;
                UpdateScrollBar();
                Invalidate();
                break;
            case Keys.Right when index >= 0 && nodes[index].Entry.IsDirectory:
                ExpandNode(nodes[index]);
                break;
            case Keys.Enter when index >= 0 && !nodes[index].Entry.IsDirectory:
                NodeActivated?.Invoke(this, new WorkspaceTreeNodeEventArgs(nodes[index].Entry));
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
            _treeFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ToggleNode(WorkspaceNode node)
    {
        if (node.Expanded)
        {
            node.Expanded = false;
            UpdateScrollBar();
            Invalidate();
            return;
        }
        ExpandNode(node);
    }

    private void ExpandNode(WorkspaceNode node)
    {
        node.Expanded = true;
        if (!node.Loaded)
        {
            NodeExpanding?.Invoke(this, new WorkspaceTreeNodeEventArgs(node.Entry));
        }
        UpdateScrollBar();
        Invalidate();
    }

    private void BuildVisibleRows()
    {
        _visibleRows.Clear();
        var top = -_scrollBar.Value;
        var width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
        foreach (var node in EnumerateVisibleNodes())
        {
            _visibleRows.Add((node, new Rectangle(0, top, width, _rowHeight)));
            top += _rowHeight;
        }
    }

    private (WorkspaceNode Node, Rectangle Bounds)? HitTestRow(Point location)
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

    private IEnumerable<WorkspaceNode> EnumerateVisibleNodes()
    {
        if (_root is null)
        {
            yield break;
        }
        foreach (var node in EnumerateVisibleNodes(_root))
        {
            yield return node;
        }
    }

    private static IEnumerable<WorkspaceNode> EnumerateVisibleNodes(WorkspaceNode node)
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

    private IEnumerable<WorkspaceNode> EnumerateAllNodes()
    {
        if (_root is null)
        {
            yield break;
        }
        var pending = new Stack<WorkspaceNode>();
        pending.Push(_root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            for (var index = node.Children.Count - 1; index >= 0; index--)
            {
                pending.Push(node.Children[index]);
            }
        }
    }

    private WorkspaceNode? FindNode(string path)
    {
        return EnumerateAllNodes().FirstOrDefault(node => PathEquals(node.Entry.FullPath, path));
    }

    private int GetContentHeight() => EnumerateVisibleNodes().Count() * _rowHeight;

    private void UpdateScrollBar()
    {
        var contentHeight = GetContentHeight();
        _scrollBar.Visible = contentHeight > ClientSize.Height;
        _scrollBar.Minimum = 0;
        _scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
        _scrollBar.SmallChange = Math.Max(1, _rowHeight);
        _scrollBar.Maximum = Math.Max(0, contentHeight - 1);
        _scrollBar.Value = Math.Min(_scrollBar.Value, GetMaximumScrollValue());
    }

    private int GetMaximumScrollValue() => Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);

    private void EnsureSelectionVisible()
    {
        if (_selectedPath is null || !IsHandleCreated)
        {
            return;
        }
        BuildVisibleRows();
        var selected = _visibleRows.FirstOrDefault(row => PathEquals(row.Node.Entry.FullPath, _selectedPath));
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

    private void DrawExpander(Graphics graphics, Rectangle bounds, bool expanded)
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

    private void DrawText(Graphics graphics, string text, Rectangle bounds, Color color)
    {
        TextRenderer.DrawText(
            graphics,
            text,
            _treeFont,
            bounds,
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private static bool PathEquals(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private int ScaleForDpi(int value) => (int)Math.Round(value * DeviceDpi / 96d);
}

internal sealed class WorkspaceTreeNodeEventArgs(WorkspaceEntry entry) : EventArgs
{
    public WorkspaceEntry Entry { get; } = entry;
}

internal sealed class WorkspaceTreeContextEventArgs(WorkspaceEntry entry, Point screenPoint) : EventArgs
{
    public WorkspaceEntry Entry { get; } = entry;
    public Point ScreenPoint { get; } = screenPoint;
}
