using System.ComponentModel;
using System.Drawing.Drawing2D;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI.Controls;

internal sealed class WorkspaceDocumentListView : Control
{
    private readonly VScrollBar _scrollBar = new() { Dock = DockStyle.Right, BackColor = Color.White };
    private readonly List<WorkspaceDocumentEntry> _documents = [];
    private readonly List<(WorkspaceDocumentEntry Document, Rectangle Bounds)> _visibleRows = [];
    private Font _metadataFont = new("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _documentFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _folderIconFont = new(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _rootTitleFont = new("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
    private string? _workspaceName;
    private string? _selectedPath;
    private string? _hoveredPath;
    private string? _contextMenuPath;
    private int _rowHeight;
    private int _rootTitleHeight;

    public WorkspaceDocumentListView()
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

    public event EventHandler<string>? DocumentActivated;
    public event EventHandler<WorkspaceDocumentContextEventArgs>? DocumentContextRequested;
    public event EventHandler<WorkspaceBackgroundContextEventArgs>? BackgroundContextRequested;

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
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PlaceholderText { get; set; } = "暂无可用文档";

    public void SetWorkspaceName(string? name)
    {
        _workspaceName = name;
        UpdateScrollBar();
        Invalidate();
    }

    public void SetDocuments(IReadOnlyList<WorkspaceDocumentEntry> documents)
    {
        _documents.Clear();
        _documents.AddRange(documents);
        if (_selectedPath is not null
            && !_documents.Any(document => PathEquals(document.FullPath, _selectedPath)))
        {
            _selectedPath = null;
        }
        UpdateScrollBar();
        EnsureSelectionVisible();
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        var previousMetadata = _metadataFont;
        var previousDocument = _documentFont;
        var previousFolderIcon = _folderIconFont;
        var previousRootTitle = _rootTitleFont;
        _metadataFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _documentFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        _folderIconFont = new Font(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);
        _rootTitleFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        _rootTitleHeight = (int)Math.Ceiling(_rootTitleFont.GetHeight(dpi) * 1.75F) + ScaleForDpi(4);
        var verticalPadding = ScaleForDpi(21);
        _rowHeight = (int)Math.Ceiling(
            _metadataFont.GetHeight(dpi) + _documentFont.GetHeight(dpi) + verticalPadding);
        previousMetadata.Dispose();
        previousDocument.Dispose();
        previousFolderIcon.Dispose();
        previousRootTitle.Dispose();
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.Clear(BackColor);
        if (_documents.Count == 0 && _workspaceName is null)
        {
            DrawText(
                eventArgs.Graphics,
                PlaceholderText,
                _metadataFont,
                new Rectangle(ScaleForDpi(16), ScaleForDpi(8), Math.Max(0, ClientSize.Width - ScaleForDpi(28)), _rowHeight),
                SystemColors.GrayText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return;
        }

        if (_workspaceName is not null)
        {
            var titleBounds = new Rectangle(
                0,
                -_scrollBar.Value,
                ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0),
                _rootTitleHeight);
            if (titleBounds.Bottom > 0 && titleBounds.Top < ClientSize.Height)
            {
                DrawText(
                    eventArgs.Graphics,
                    _workspaceName,
                    _rootTitleFont,
                    new Rectangle(ScaleForDpi(14), titleBounds.Top + ScaleForDpi(4),
                        Math.Max(0, titleBounds.Width - ScaleForDpi(18)),
                        titleBounds.Height - ScaleForDpi(4)),
                    Color.FromArgb(0x55, 0x55, 0x55),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                        | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        BuildVisibleRows();
        foreach (var (document, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }

            var isSelected = PathEquals(document.FullPath, _selectedPath);
            var isHovered = PathEquals(document.FullPath, _hoveredPath);
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
            else if (isHovered || PathEquals(document.FullPath, _contextMenuPath))
            {
                using var brush = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0));
                using var path = CreateRoundedRect(bgBounds, ScaleForDpi(8));
                eventArgs.Graphics.FillPath(brush, path);
            }

            if (PathEquals(document.FullPath, _contextMenuPath))
            {
                using var pen = new Pen(Color.FromArgb(0x55, 0x55, 0x55), 2);
                using var borderPath = CreateRoundedRect(bgBounds, ScaleForDpi(8));
                eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                eventArgs.Graphics.DrawPath(pen, borderPath);
                eventArgs.Graphics.SmoothingMode = SmoothingMode.Default;
            }

            DrawDocument(eventArgs.Graphics, document, bounds);
        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        Focus();
        var row = HitTestRow(eventArgs.Location);
        if (row is null)
        {
            if (eventArgs.Button == MouseButtons.Right)
            {
                BackgroundContextRequested?.Invoke(this, new WorkspaceBackgroundContextEventArgs(
                    PointToScreen(eventArgs.Location)));
            }
            return;
        }

        if (eventArgs.Button == MouseButtons.Right)
        {
            _contextMenuPath = row.Value.Document.FullPath;
            Invalidate();
            DocumentContextRequested?.Invoke(this, new WorkspaceDocumentContextEventArgs(
                row.Value.Document,
                PointToScreen(eventArgs.Location)));
        }
        else if (eventArgs.Button == MouseButtons.Left)
        {
            SelectedPath = row.Value.Document.FullPath;
            DocumentActivated?.Invoke(this, row.Value.Document.FullPath);
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        var row = HitTestRow(eventArgs.Location);
        var hoveredPath = row?.Document.FullPath;
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
        return keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        base.OnKeyDown(eventArgs);
        if (_documents.Count == 0)
        {
            return;
        }

        var index = _selectedPath is null
            ? -1
            : _documents.FindIndex(document => PathEquals(document.FullPath, _selectedPath));
        switch (eventArgs.KeyCode)
        {
            case Keys.Up:
                SelectedPath = _documents[Math.Max(0, index <= 0 ? 0 : index - 1)].FullPath;
                break;
            case Keys.Down:
                SelectedPath = _documents[Math.Min(_documents.Count - 1, index < 0 ? 0 : index + 1)].FullPath;
                break;
            case Keys.Enter when index >= 0:
                DocumentActivated?.Invoke(this, _documents[index].FullPath);
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
            _metadataFont.Dispose();
            _documentFont.Dispose();
            _folderIconFont.Dispose();
            _rootTitleFont.Dispose();
        }
        base.Dispose(disposing);
    }

    public void ClearContextMenuHighlight()
    {
        if (_contextMenuPath is null) return;
        _contextMenuPath = null;
        Invalidate();
    }

    private void DrawDocument(Graphics graphics, WorkspaceDocumentEntry document, Rectangle bounds)
    {
        var horizontalPadding = ScaleForDpi(10);
        var topPadding = ScaleForDpi(3);
        var availableWidth = Math.Max(0, bounds.Width - horizontalPadding * 2);
        var metadataHeight = (int)Math.Ceiling(_metadataFont.GetHeight(DeviceDpi)) + ScaleForDpi(2);
        var iconAdvance = ScaleForDpi(14);
        var metadataBounds = new Rectangle(
            bounds.Left + horizontalPadding,
            bounds.Top + topPadding + ScaleForDpi(4),
            availableWidth,
            metadataHeight);
        var modifiedText = WorkspaceDocumentTimeFormatter.Format(document.LastWriteTime, DateTime.Now);
        var modifiedWidth = TextRenderer.MeasureText(
            graphics,
            modifiedText,
            _metadataFont,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var gap = ScaleForDpi(8);
        var iconBounds = new Rectangle(
            metadataBounds.Left,
            metadataBounds.Top,
            iconAdvance,
            metadataBounds.Height);
        var folderBounds = new Rectangle(
            metadataBounds.Left + iconAdvance,
            metadataBounds.Top,
            Math.Max(0, metadataBounds.Width - modifiedWidth - gap - iconAdvance),
            metadataBounds.Height);
        var modifiedBounds = new Rectangle(
            Math.Max(metadataBounds.Left, metadataBounds.Right - modifiedWidth - ScaleForDpi(4)),
            metadataBounds.Top,
            Math.Min(modifiedWidth, metadataBounds.Width),
            metadataBounds.Height);
        DrawText(
            graphics,
            SystemIconProvider.FolderIcon,
            _folderIconFont,
            iconBounds,
            SystemColors.GrayText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine);
        DrawText(
            graphics,
            document.FolderName,
            _metadataFont,
            folderBounds,
            SystemColors.GrayText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        DrawText(
            graphics,
            modifiedText,
            _metadataFont,
            modifiedBounds,
            SystemColors.GrayText,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        var documentBounds = new Rectangle(
            metadataBounds.Left,
            bounds.Top + topPadding + metadataHeight + ScaleForDpi(4),
            metadataBounds.Width,
            (int)Math.Ceiling(_documentFont.GetHeight(DeviceDpi)) + ScaleForDpi(2));
        DrawText(
            graphics,
            GetDisplayName(document.Name),
            _documentFont,
            documentBounds,
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void BuildVisibleRows()
    {
        _visibleRows.Clear();
        var top = (_workspaceName is null ? ScaleForDpi(8) : _rootTitleHeight) - _scrollBar.Value;
        var width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0) - ScaleForDpi(8);
        foreach (var document in _documents)
        {
            _visibleRows.Add((document, new Rectangle(ScaleForDpi(4), top, width, _rowHeight)));
            top += _rowHeight + ScaleForDpi(2);
        }
    }

    private (WorkspaceDocumentEntry Document, Rectangle Bounds)? HitTestRow(Point location)
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

    private void UpdateScrollBar()
    {
        var titleOffset = _workspaceName is null ? ScaleForDpi(8) : _rootTitleHeight;
        var contentHeight = titleOffset + _documents.Count * (_rowHeight + ScaleForDpi(2)) - ScaleForDpi(2);
        _scrollBar.Visible = contentHeight > ClientSize.Height;
        _scrollBar.Minimum = 0;
        _scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
        _scrollBar.SmallChange = Math.Max(1, _rowHeight);
        _scrollBar.Maximum = Math.Max(0, contentHeight - 1);
        _scrollBar.Value = Math.Min(_scrollBar.Value, GetMaximumScrollValue());
    }

    private int GetMaximumScrollValue()
    {
        return Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);
    }

    private void EnsureSelectionVisible()
    {
        if (_selectedPath is null || !IsHandleCreated)
        {
            return;
        }

        BuildVisibleRows();
        var selected = _visibleRows.FirstOrDefault(row => PathEquals(row.Document.FullPath, _selectedPath));
        if (selected.Document is null)
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

    private static string GetDisplayName(string name)
    {
        return string.Equals(Path.GetExtension(name), ".md", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
    }

    private static bool PathEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        Font font,
        Rectangle bounds,
        Color color,
        TextFormatFlags flags)
    {
        TextRenderer.DrawText(graphics, text, font, bounds, color, flags);
    }

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

internal sealed class WorkspaceDocumentContextEventArgs(
    WorkspaceDocumentEntry document,
    Point screenPoint) : EventArgs
{
    public WorkspaceDocumentEntry Document { get; } = document;
    public Point ScreenPoint { get; } = screenPoint;
}

internal sealed class WorkspaceBackgroundContextEventArgs(Point screenPoint) : EventArgs
{
    public Point ScreenPoint { get; } = screenPoint;
}
