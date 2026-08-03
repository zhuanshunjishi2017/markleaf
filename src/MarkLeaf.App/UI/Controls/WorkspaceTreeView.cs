using System.ComponentModel;
using System.Drawing.Drawing2D;
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

    private readonly VScrollBar _scrollBar = new() { Dock = DockStyle.Right, BackColor = Color.White };
    private readonly List<(WorkspaceNode Node, Rectangle Bounds)> _visibleRows = [];
    private Font _treeFont = new("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _rootTitleFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _iconFont = new("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _arrowFont = new("Segoe Fluent Icons", 8F, FontStyle.Regular, GraphicsUnit.Point);
    private WorkspaceNode? _root;
    private string? _placeholderText = "暂未打开工作区";
    private string? _selectedPath;
    private string? _hoveredPath;
    private int _rowHeight;
    private int _rootTitleHeight;
    private WorkspaceDocumentSortOrder _sortOrder = WorkspaceDocumentSortOrder.ModifiedTimeDescending;

    public WorkspaceTreeView()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        Dock = DockStyle.Fill;
        TabStop = true;
        BackColor = Color.FromArgb(0xF9, 0xF9, 0xF9);
        ForeColor = SystemColors.WindowText;
        Controls.Add(_scrollBar);
        _scrollBar.Scroll += (_, _) => Invalidate();
        ConfigureTypography(DeviceDpi);
    }

    public event EventHandler<WorkspaceTreeNodeEventArgs>? NodeExpanding;
    public event EventHandler<WorkspaceTreeNodeEventArgs>? NodeActivated;
    public event EventHandler<WorkspaceTreeContextEventArgs>? NodeContextRequested;
    public event EventHandler<WorkspaceTreeContextEventArgs>? WorkspaceMenuRequested;

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
        var previousRootFont = _rootTitleFont;
        var previousIconFont = _iconFont;
        var previousArrowFont = _arrowFont;
        _treeFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _rootTitleFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        _iconFont = new Font("Segoe Fluent Icons", 11F, FontStyle.Regular, GraphicsUnit.Point);
        _arrowFont = new Font("Segoe Fluent Icons", 8F, FontStyle.Regular, GraphicsUnit.Point);
        _rowHeight = (int)Math.Ceiling(_treeFont.GetHeight(dpi) * 1.75F);
        _rootTitleHeight = (int)Math.Ceiling(_rootTitleFont.GetHeight(dpi) * 1.75F) + ScaleForDpi(4);
        previousFont.Dispose();
        previousRootFont.Dispose();
        previousIconFont.Dispose();
        previousArrowFont.Dispose();
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
        foreach (var entry in SortEntries(entries))
        {
            var child = new WorkspaceNode(entry, node.Depth + 1);
            if (node == _root)
            {
                child = new WorkspaceNode(entry, 0);
            }
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

    public void SetSortOrder(WorkspaceDocumentSortOrder sortOrder)
    {
        _sortOrder = sortOrder;
        foreach (var node in EnumerateAllNodes().Where(node => node.Entry.IsDirectory))
        {
            node.Children.Sort(CompareNodes);
        }
        UpdateScrollBar();
        Invalidate();
    }

    private IEnumerable<WorkspaceEntry> SortEntries(IReadOnlyList<WorkspaceEntry> entries)
        => entries.OrderBy(entry => entry, Comparer<WorkspaceEntry>.Create((left, right) => CompareEntries(left, right)));

    private int CompareNodes(WorkspaceNode left, WorkspaceNode right) => CompareEntries(left.Entry, right.Entry);

    private int CompareEntries(WorkspaceEntry left, WorkspaceEntry right)
    {
        if (left.IsDirectory != right.IsDirectory)
        {
            return left.IsDirectory ? -1 : 1;
        }

        var result = _sortOrder switch
        {
            WorkspaceDocumentSortOrder.FileNameAscending => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name),
            WorkspaceDocumentSortOrder.FileNameDescending => StringComparer.CurrentCultureIgnoreCase.Compare(right.Name, left.Name),
            WorkspaceDocumentSortOrder.ModifiedTimeAscending => GetLastWriteTime(left).CompareTo(GetLastWriteTime(right)),
            _ => GetLastWriteTime(right).CompareTo(GetLastWriteTime(left)),
        };
        return result != 0
            ? result
            : StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
    }

    private static DateTime GetLastWriteTime(WorkspaceEntry entry)
    {
        try
        {
            return entry.IsDirectory
                ? Directory.GetLastWriteTime(entry.FullPath)
                : File.GetLastWriteTime(entry.FullPath);
        }
        catch (IOException)
        {
            return DateTime.MinValue;
        }
        catch (UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
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
                new Rectangle(ScaleForDpi(16), 0, Math.Max(0, ClientSize.Width - ScaleForDpi(28)), _rowHeight),
                SystemColors.GrayText);
            return;
        }

        BuildVisibleRows();
        if (_root is not null)
        {
            var titleBounds = new Rectangle(
                0,
                -_scrollBar.Value,
                ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0),
                _rootTitleHeight);
            if (titleBounds.Bottom > 0 && titleBounds.Top < ClientSize.Height)
            {
                DrawText(eventArgs.Graphics, _root.Entry.Name,
                    new Rectangle(ScaleForDpi(24), titleBounds.Top + ScaleForDpi(4),
                        Math.Max(0, titleBounds.Width - ScaleForDpi(28)),
                        titleBounds.Height - ScaleForDpi(8)),
                    Color.FromArgb(0x55, 0x55, 0x55), _rootTitleFont);
            }
        }
        foreach (var (node, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }
            var isSelected = !node.Entry.IsDirectory && PathEquals(node.Entry.FullPath, _selectedPath);
            var isHovered = PathEquals(node.Entry.FullPath, _hoveredPath);
            var bgBounds = new Rectangle(
                bounds.X + ScaleForDpi(4), bounds.Y,
                Math.Max(0, bounds.Width - ScaleForDpi(8)), bounds.Height);
            if (isSelected && isHovered)
            {
                using var brush = new SolidBrush(Color.FromArgb(0xD0, 0xD0, 0xD0));
                using var path = CreateRoundedRect(bgBounds, ScaleForDpi(8));
                eventArgs.Graphics.FillPath(brush, path);
            }
            else if (isSelected)
            {
                using var brush = new SolidBrush(Color.FromArgb(0xE0, 0xE0, 0xE0));
                using var path = CreateRoundedRect(bgBounds, ScaleForDpi(8));
                eventArgs.Graphics.FillPath(brush, path);
            }
            else if (isHovered)
            {
                using var brush = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0));
                using var path = CreateRoundedRect(bgBounds, ScaleForDpi(8));
                eventArgs.Graphics.FillPath(brush, path);
            }

            var indent = ScaleForDpi(18) * node.Depth;
            var expanderBounds = new Rectangle(ScaleForDpi(8) + indent, bounds.Top, ScaleForDpi(16), bounds.Height);
            if (node.Entry.IsDirectory)
            {
                DrawExpander(eventArgs.Graphics, expanderBounds, node.Expanded);
            }
            var iconAdvance = ScaleForDpi(18);
            var iconBounds = new Rectangle(expanderBounds.Right, bounds.Top, iconAdvance, bounds.Height);
            DrawText(eventArgs.Graphics, GetIconChar(node), iconBounds, ForeColor, _iconFont);
            var textBounds = new Rectangle(
                iconBounds.Right,
                bounds.Top,
                Math.Max(0, bounds.Width - iconBounds.Right - ScaleForDpi(8)),
                bounds.Height);
            DrawText(eventArgs.Graphics, node.Entry.Name, textBounds, ForeColor);

        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        var row = HitTestRow(eventArgs.Location);
        if (_root is not null && RootTitleBounds().Contains(eventArgs.Location))
        {
            Focus();
            if (eventArgs.Button == MouseButtons.Right)
            {
                WorkspaceMenuRequested?.Invoke(this, new WorkspaceTreeContextEventArgs(
                    _root.Entry,
                    PointToScreen(eventArgs.Location)));
            }
            return;
        }
        if (row is null)
        {
            if (eventArgs.Button == MouseButtons.Right && _root is not null)
            {
                WorkspaceMenuRequested?.Invoke(this, new WorkspaceTreeContextEventArgs(
                    _root.Entry,
                    PointToScreen(eventArgs.Location)));
            }
            return;
        }
        Focus();
        var node = row.Value.Node;
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
            SelectedPath = node.Entry.FullPath;
            NodeActivated?.Invoke(this, new WorkspaceTreeNodeEventArgs(node.Entry));
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        var row = HitTestRow(eventArgs.Location);
        var hoveredPath = row?.Node.Entry.FullPath;
        if (!PathEquals(hoveredPath, _hoveredPath))
        {
            _hoveredPath = hoveredPath;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (_hoveredPath is not null)
        {
            _hoveredPath = null;
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
        var index = Array.FindIndex(nodes, node => PathEquals(node.Entry.FullPath, _selectedPath));
        switch (eventArgs.KeyCode)
        {
            case Keys.Up:
                for (var i = Math.Max(0, index - 1); i >= 0; i--)
                {
                    if (!nodes[i].Entry.IsDirectory)
                    {
                        SelectedPath = nodes[i].Entry.FullPath;
                        break;
                    }
                }
                break;
            case Keys.Down:
                for (var i = Math.Min(nodes.Length - 1, index + 1); i < nodes.Length; i++)
                {
                    if (!nodes[i].Entry.IsDirectory)
                    {
                        SelectedPath = nodes[i].Entry.FullPath;
                        break;
                    }
                }
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
            _rootTitleFont.Dispose();
            _iconFont.Dispose();
            _arrowFont.Dispose();
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
        var top = _root is null ? 0 : _rootTitleHeight - _scrollBar.Value;
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
        foreach (var node in _root.Children)
        {
            foreach (var visible in EnumerateVisibleNodes(node))
            {
                yield return visible;
            }
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

    private int GetContentHeight() => (_root is null ? 0 : _rootTitleHeight) + EnumerateVisibleNodes().Count() * _rowHeight;

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

    private static string GetIconChar(WorkspaceNode node)
    {
        if (node.Entry.IsDirectory)
        {
            return node.Expanded ? "" : "";
        }
        var ext = Path.GetExtension(node.Entry.Name);
        return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase) ? "" : "";
    }

    private void DrawExpander(Graphics graphics, Rectangle bounds, bool expanded)
    {
        TextRenderer.DrawText(
            graphics,
            expanded ? "" : "",
            _arrowFont,
            bounds,
            SystemColors.ControlDarkDark,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private Rectangle RootTitleBounds() => new(
        0,
        -_scrollBar.Value,
        ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0),
        _rootTitleHeight);

    private void DrawText(Graphics graphics, string text, Rectangle bounds, Color color, Font? font = null)
    {
        TextRenderer.DrawText(
            graphics,
            text,
            font ?? _treeFont,
            bounds,
            color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private static bool PathEquals(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

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
