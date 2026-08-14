using System.ComponentModel;
using System.Drawing.Drawing2D;
using MarkLeaf.Services;
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

    private readonly MarkLeafScrollbar _scrollBar = new() { Dock = DockStyle.Right };
    private readonly List<(WorkspaceNode Node, Rectangle Bounds)> _visibleRows = [];
    private Font _treeFont = new("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _rootTitleFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _iconFont = new(SystemIconProvider.IconFontName, 11F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _arrowFont = new(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);

    // Theme colors (defaults match white theme).
    private Color _bgPrimary = Color.White;
    private Color _bgHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
    private Color _themeLight = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private Color _themeDark = Color.FromArgb(0xD0, 0xD0, 0xD0);
    private Color _textPrimary = Color.Black;
    private Color _textSecondary = Color.FromArgb(0x55, 0x55, 0x55);
    private Color _textTertiary = Color.FromArgb(0x6D, 0x6D, 0x6D);
    private Color _textSelected = Color.Black;
    private Color _icon = Color.FromArgb(0x80, 0x80, 0x80);
    private Color _iconSelected = Color.Black;
    private Color _iconSecondary = Color.FromArgb(0x80, 0x80, 0x80);

    private WorkspaceNode? _root;
    private string? _placeholderText = Loc.Get("sidebar.noWorkspace");
    private string? _selectedPath;
    private string? _hoveredPath;
    private string? _keyboardHoverPath;
    private string? _contextMenuPath;
    private Point _mouseDownPoint;
    private string? _mouseDownPath;
    private bool _mouseDownIsDirectory;
    private string? _dropTargetDir;
    private Rectangle _dropTargetBounds;
    private bool _internalDragActive;
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
        AllowDrop = true;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        Controls.Add(_scrollBar);
        _scrollBar.Scroll += (_, _) => Invalidate();
        ConfigureTypography(DeviceDpi);
    }

    public event EventHandler<WorkspaceTreeNodeEventArgs>? NodeExpanding;
    public event EventHandler<WorkspaceTreeNodeEventArgs>? NodeActivated;
    public event EventHandler<WorkspaceTreeContextEventArgs>? NodeContextRequested;
    public event EventHandler<WorkspaceTreeContextEventArgs>? WorkspaceMenuRequested;
    public event EventHandler<WorkspaceFilesDroppedEventArgs>? FilesDropped;

    public event EventHandler<WorkspaceNodeMovedEventArgs>? NodeMoved;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoHideScrollbar
    {
        get => _scrollBar.AutoHide;
        set => _scrollBar.AutoHide = value;
    }

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

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) _bgPrimary = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("theme-light", out c)) _themeLight = c;
        if (colors.TryGetValue("theme-dark", out c)) _themeDark = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        if (colors.TryGetValue("text-secondary", out c)) _textSecondary = c;
        if (colors.TryGetValue("text-tertiary", out c)) _textTertiary = c;
        if (colors.TryGetValue("text-selected", out c)) _textSelected = c;
        if (colors.TryGetValue("icon", out c)) _icon = c;
        if (colors.TryGetValue("icon-selected", out c)) _iconSelected = c;
        if (colors.TryGetValue("icon-secondary", out c)) _iconSecondary = c;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        var previousFont = _treeFont;
        var previousRootFont = _rootTitleFont;
        var previousIconFont = _iconFont;
        var previousArrowFont = _arrowFont;
        _treeFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        _rootTitleFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        _iconFont = new Font(SystemIconProvider.IconFontName, 11F, FontStyle.Regular, GraphicsUnit.Point);
        _arrowFont = new Font(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);
        _rowHeight = (int)Math.Ceiling(_treeFont.GetHeight(dpi) * 1.75F);
        _rootTitleHeight = (int)Math.Ceiling(_rootTitleFont.GetHeight(dpi) * 1.75F) + this.ScaleForDpi(4);
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
                new Rectangle(this.ScaleForDpi(16), 0, Math.Max(0, ClientSize.Width - this.ScaleForDpi(28)), _rowHeight),
                _textTertiary);
            return;
        }

        BuildVisibleRows();
        if (_root is not null)
        {
            var titleBounds = new Rectangle(
                0,
                0 - _scrollBar.Value,
                ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0),
                _rootTitleHeight);
            if (titleBounds.Bottom > 0 && titleBounds.Top < ClientSize.Height)
            {
                DrawText(eventArgs.Graphics, _root.Entry.Name,
                    new Rectangle(this.ScaleForDpi(24), titleBounds.Top + this.ScaleForDpi(4),
                        Math.Max(0, titleBounds.Width - this.ScaleForDpi(28)),
                        titleBounds.Height - this.ScaleForDpi(8)),
                    _textSecondary, _rootTitleFont);
            }
        }
        foreach (var (node, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }
            var isSelected = !node.Entry.IsDirectory && PathEquals(node.Entry.FullPath, _selectedPath);
            var isHovered = PathEquals(node.Entry.FullPath, _hoveredPath)
                || PathEquals(node.Entry.FullPath, _keyboardHoverPath);
            var bgBounds = new Rectangle(
                bounds.X + this.ScaleForDpi(4), bounds.Y,
                Math.Max(0, bounds.Width - this.ScaleForDpi(8)), bounds.Height);
            if (isSelected && isHovered)
            {
                using var brush = new SolidBrush(_themeDark);
                SidebarGdi.FillRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), brush);
            }
            else if (isSelected)
            {
                using var brush = new SolidBrush(_themeLight);
                SidebarGdi.FillRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), brush);
            }
            else if (isHovered || PathEquals(node.Entry.FullPath, _contextMenuPath))
            {
                using var brush = new SolidBrush(_bgHover);
                SidebarGdi.FillRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), brush);
            }

            if (PathEquals(node.Entry.FullPath, _contextMenuPath))
            {
                using var pen = new Pen(_textSecondary, 2);
                SidebarGdi.DrawRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), pen);
            }

            var indent = this.ScaleForDpi(18) * node.Depth;
            var expanderBounds = new Rectangle(this.ScaleForDpi(8) + indent, bounds.Top, this.ScaleForDpi(16), bounds.Height);
            if (node.Entry.IsDirectory)
            {
                DrawExpander(eventArgs.Graphics, expanderBounds, node.Expanded);
            }
            var iconAdvance = this.ScaleForDpi(18);
            var iconBounds = new Rectangle(expanderBounds.Right, bounds.Top, iconAdvance, bounds.Height);
            DrawText(eventArgs.Graphics, GetIconChar(node), iconBounds, isSelected ? _iconSelected : _icon, _iconFont);
            var textBounds = new Rectangle(
                iconBounds.Right,
                bounds.Top,
                Math.Max(0, bounds.Width - iconBounds.Right - this.ScaleForDpi(4)),
                bounds.Height);
            DrawText(eventArgs.Graphics, node.Entry.Name, textBounds, isSelected ? _textSelected : ForeColor);

        }

        if (_dropTargetDir is not null && _dropTargetBounds != Rectangle.Empty)
        {
            var bgBounds = new Rectangle(
                _dropTargetBounds.X + this.ScaleForDpi(4),
                _dropTargetBounds.Y,
                Math.Max(0, _dropTargetBounds.Width - this.ScaleForDpi(8)),
                _dropTargetBounds.Height);
            using var pen = new Pen(_textSecondary, 2);
            SidebarGdi.DrawRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), pen);
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
            _mouseDownPath = null;
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
            _contextMenuPath = node.Entry.FullPath;
            Invalidate();
            NodeContextRequested?.Invoke(this, new WorkspaceTreeContextEventArgs(
                node.Entry,
                PointToScreen(eventArgs.Location)));
            return;
        }
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _mouseDownPoint = eventArgs.Location;
        _mouseDownPath = node.Entry.FullPath;
        _mouseDownIsDirectory = node.Entry.IsDirectory;

        if (node.Entry.IsDirectory)
        {
            if (eventArgs.Clicks == 1)
            {
                ToggleNode(node);
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        base.OnMouseUp(eventArgs);
        if (_mouseDownPath is null || eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        var row = HitTestRow(eventArgs.Location);
        if (row is null || row.Value.Node.Entry.FullPath != _mouseDownPath)
        {
            _mouseDownPath = null;
            return;
        }

        if (!_mouseDownIsDirectory)
        {
            SelectedPath = row.Value.Node.Entry.FullPath;
            _keyboardHoverPath = null;
            NodeActivated?.Invoke(this, new WorkspaceTreeNodeEventArgs(row.Value.Node.Entry));
        }

        _mouseDownPath = null;
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        if (_mouseDownPath is not null && eventArgs.Button == MouseButtons.Left)
        {
            var dragSize = SystemInformation.DragSize;
            if (Math.Abs(eventArgs.X - _mouseDownPoint.X) >= dragSize.Width
                || Math.Abs(eventArgs.Y - _mouseDownPoint.Y) >= dragSize.Height)
            {
                var sourcePath = _mouseDownPath;
                _mouseDownPath = null;
                var data = new DataObject();
                data.SetData(typeof(string), sourcePath);
                data.SetData(DataFormats.FileDrop, new[] { sourcePath });
                DoDragDrop(data, DragDropEffects.Move | DragDropEffects.Copy);
                _dropTargetDir = null;
                _dropTargetBounds = Rectangle.Empty;
                _internalDragActive = false;
                Invalidate();
                return;
            }
        }

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

        var referencePath = _keyboardHoverPath ?? _selectedPath;
        var index = Array.FindIndex(nodes, node => PathEquals(node.Entry.FullPath, referencePath));
        if (index < 0) index = 0;

        switch (eventArgs.KeyCode)
        {
            case Keys.Up when index > 0:
                _keyboardHoverPath = nodes[index - 1].Entry.FullPath;
                EnsureNodeVisible(nodes[index - 1]);
                Invalidate();
                break;
            case Keys.Down when index < nodes.Length - 1:
                _keyboardHoverPath = nodes[index + 1].Entry.FullPath;
                EnsureNodeVisible(nodes[index + 1]);
                Invalidate();
                break;
            case Keys.Left when index >= 0 && nodes[index].Entry.IsDirectory && nodes[index].Expanded:
                nodes[index].Expanded = false;
                UpdateScrollBar();
                Invalidate();
                break;
            case Keys.Right when index >= 0 && nodes[index].Entry.IsDirectory:
                ExpandNode(nodes[index]);
                break;
            case Keys.Enter when index >= 0:
                if (nodes[index].Entry.IsDirectory)
                {
                    ToggleNode(nodes[index]);
                }
                else
                {
                    SelectedPath = nodes[index].Entry.FullPath;
                    _keyboardHoverPath = null;
                    NodeActivated?.Invoke(this, new WorkspaceTreeNodeEventArgs(nodes[index].Entry));
                }
                break;
            default:
                return;
        }
        eventArgs.Handled = true;
    }

    private void EnsureNodeVisible(WorkspaceNode node)
    {
        if (!IsHandleCreated) return;
        BuildVisibleRows();
        var row = _visibleRows.FirstOrDefault(r => PathEquals(r.Node.Entry.FullPath, node.Entry.FullPath));
        if (row.Node is null) return;
        if (row.Bounds.Top < 0)
            _scrollBar.Value = Math.Clamp(_scrollBar.Value + row.Bounds.Top, 0, GetMaximumScrollValue());
        else if (row.Bounds.Bottom > ClientSize.Height)
            _scrollBar.Value = Math.Clamp(_scrollBar.Value + row.Bounds.Bottom - ClientSize.Height, 0, GetMaximumScrollValue());
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

    protected override void OnDragEnter(DragEventArgs eventArgs)
    {
        base.OnDragEnter(eventArgs);
        if (eventArgs.Data?.GetDataPresent(typeof(string)) == true)
        {
            eventArgs.Effect = DragDropEffects.Move;
            _internalDragActive = true;
            return;
        }
        eventArgs.Effect = eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true
            && GetDroppableFiles(eventArgs.Data).Length > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    protected override void OnDragOver(DragEventArgs eventArgs)
    {
        base.OnDragOver(eventArgs);
        if (!_internalDragActive && eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) != true)
        {
            eventArgs.Effect = DragDropEffects.None;
            return;
        }

        var clientPoint = PointToClient(new Point(eventArgs.X, eventArgs.Y));
        var oldDir = _dropTargetDir;
        _dropTargetDir = null;
        _dropTargetBounds = Rectangle.Empty;

        var row = HitTestRow(clientPoint);
        if (row is not null)
        {
            if (row.Value.Node.Entry.IsDirectory)
            {
                _dropTargetDir = row.Value.Node.Entry.FullPath;
                _dropTargetBounds = row.Value.Bounds;
            }
            else
            {
                _dropTargetDir = Path.GetDirectoryName(row.Value.Node.Entry.FullPath);
                _dropTargetBounds = row.Value.Bounds;
            }
        }
        else if (_root is not null)
        {
            _dropTargetDir = _root.Entry.FullPath;
        }

        eventArgs.Effect = _dropTargetDir is not null
            ? (_internalDragActive ? DragDropEffects.Move : DragDropEffects.Copy)
            : DragDropEffects.None;

        if (_dropTargetDir != oldDir) Invalidate();

        // Auto-scroll near edges.
        if (clientPoint.Y < _rowHeight)
            _scrollBar.Value = Math.Max(0, _scrollBar.Value - _scrollBar.SmallChange);
        else if (clientPoint.Y > ClientSize.Height - _rowHeight)
            _scrollBar.Value = Math.Min(GetMaximumScrollValue(), _scrollBar.Value + _scrollBar.SmallChange);
    }

    protected override void OnDragLeave(EventArgs eventArgs)
    {
        base.OnDragLeave(eventArgs);
        _dropTargetDir = null;
        _dropTargetBounds = Rectangle.Empty;
        _internalDragActive = false;
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs eventArgs)
    {
        base.OnDragDrop(eventArgs);
        var targetDir = _dropTargetDir;
        var wasInternal = _internalDragActive;
        _dropTargetDir = null;
        _dropTargetBounds = Rectangle.Empty;
        _internalDragActive = false;
        Invalidate();

        if (wasInternal && eventArgs.Data?.GetData(typeof(string)) is string sourcePath && targetDir is not null)
        {
            NodeMoved?.Invoke(this, new WorkspaceNodeMovedEventArgs(sourcePath, targetDir));
            return;
        }

        var paths = GetDroppableFiles(eventArgs.Data);
        if (paths.Length == 0) return;
        FilesDropped?.Invoke(this, new WorkspaceFilesDroppedEventArgs(paths));
    }



    public void ClearContextMenuHighlight()
    {
        if (_contextMenuPath is null) return;
        _contextMenuPath = null;
        Invalidate();
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
        var top = 0 + (_root is null ? 0 : _rootTitleHeight) - _scrollBar.Value;
        var width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
        foreach (var node in EnumerateVisibleNodes())
        {
            _visibleRows.Add((node, new Rectangle(0, top, width, _rowHeight)));
            top += _rowHeight + this.ScaleForDpi(2);
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

    private int GetContentHeight() => 0 + (_root is null ? 0 : _rootTitleHeight)
        + EnumerateVisibleNodes().Count() * (_rowHeight + this.ScaleForDpi(2));

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
            return node.Expanded
                ? SystemIconProvider.FolderExpandedIcon
                : SystemIconProvider.FolderCollapsedIcon;
        }
        var ext = Path.GetExtension(node.Entry.Name);
        return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase)
            ? SystemIconProvider.TextFileIcon
            : SystemIconProvider.MarkdownFileIcon;
    }

    private void DrawExpander(Graphics graphics, Rectangle bounds, bool expanded)
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

    private Rectangle RootTitleBounds() => new(
        0,
        0 - _scrollBar.Value,
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

    private static string[] GetDroppableFiles(IDataObject? data)
    {
        if (data?.GetDataPresent(DataFormats.FileDrop) != true) return [];
        var paths = data.GetData(DataFormats.FileDrop) as string[];
        return paths?.Where(IsDroppableFile).Take(32).ToArray() ?? [];
    }

    internal static bool IsDroppableFile(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase);
    }
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

internal sealed class WorkspaceFilesDroppedEventArgs(IReadOnlyList<string> paths) : EventArgs
{
    public IReadOnlyList<string> Paths { get; } = paths;
}

internal sealed class WorkspaceNodeMovedEventArgs(string sourcePath, string targetDirectory) : EventArgs
{
    public string SourcePath { get; } = sourcePath;
    public string TargetDirectory { get; } = targetDirectory;
}
