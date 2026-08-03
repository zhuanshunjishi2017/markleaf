using System.ComponentModel;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI.Controls;

internal sealed class WorkspaceDocumentListView : Control
{
    private readonly VScrollBar _scrollBar = new() { Dock = DockStyle.Right, BackColor = Color.White };
    private readonly List<WorkspaceDocumentEntry> _documents = [];
    private readonly List<(WorkspaceDocumentEntry Document, Rectangle Bounds)> _visibleRows = [];
    private Font _metadataFont = new("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _documentFont = new("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
    private string? _selectedPath;
    private string? _hoveredPath;
    private int _rowHeight;

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
        _metadataFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _documentFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
        var verticalPadding = ScaleForDpi(30);
        _rowHeight = (int)Math.Ceiling(
            _metadataFont.GetHeight(dpi) + _documentFont.GetHeight(dpi) + verticalPadding);
        previousMetadata.Dispose();
        previousDocument.Dispose();
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.Clear(BackColor);
        if (_documents.Count == 0)
        {
            DrawText(
                eventArgs.Graphics,
                PlaceholderText,
                _metadataFont,
                new Rectangle(ScaleForDpi(12), 0, Math.Max(0, ClientSize.Width - ScaleForDpi(24)), _rowHeight),
                SystemColors.GrayText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return;
        }

        BuildVisibleRows();
        foreach (var (document, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }

            if (PathEquals(document.FullPath, _selectedPath))
            {
                using var selectionBrush = new SolidBrush(
                    PathEquals(document.FullPath, _hoveredPath)
                        ? Color.FromArgb(0x8B, 0xDE, 0xB1)
                        : Color.FromArgb(0xCC, 0xED, 0xD9));
                eventArgs.Graphics.FillRectangle(selectionBrush, bounds);
            }
            else if (PathEquals(document.FullPath, _hoveredPath))
            {
                using var hoverBrush = new SolidBrush(Color.FromArgb(0xE0, 0xE0, 0xE0));
                eventArgs.Graphics.FillRectangle(hoverBrush, bounds);
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

        SelectedPath = row.Value.Document.FullPath;
        if (eventArgs.Button == MouseButtons.Right)
        {
            DocumentContextRequested?.Invoke(this, new WorkspaceDocumentContextEventArgs(
                row.Value.Document,
                PointToScreen(eventArgs.Location)));
        }
        else if (eventArgs.Button == MouseButtons.Left)
        {
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
        }
        base.Dispose(disposing);
    }

    private void DrawDocument(Graphics graphics, WorkspaceDocumentEntry document, Rectangle bounds)
    {
        var horizontalPadding = ScaleForDpi(10);
        var topPadding = ScaleForDpi(6);
        var availableWidth = Math.Max(0, bounds.Width - horizontalPadding * 2);
        var metadataHeight = (int)Math.Ceiling(_metadataFont.GetHeight(DeviceDpi)) + ScaleForDpi(2);
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
        var folderBounds = new Rectangle(
            metadataBounds.Left,
            metadataBounds.Top,
            Math.Max(0, metadataBounds.Width - modifiedWidth - gap),
            metadataBounds.Height);
        var modifiedBounds = new Rectangle(
            Math.Max(metadataBounds.Left, metadataBounds.Right - modifiedWidth),
            metadataBounds.Top,
            Math.Min(modifiedWidth, metadataBounds.Width),
            metadataBounds.Height);
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
            bounds.Top + topPadding + metadataHeight + ScaleForDpi(6),
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
        var top = -_scrollBar.Value;
        var width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0);
        foreach (var document in _documents)
        {
            _visibleRows.Add((document, new Rectangle(0, top, width, _rowHeight)));
            top += _rowHeight;
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
        var contentHeight = _documents.Count * _rowHeight;
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
