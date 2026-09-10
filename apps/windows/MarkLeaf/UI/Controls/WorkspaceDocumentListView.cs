using System.ComponentModel;
using System.Drawing.Drawing2D;
using MarkLeaf.Services;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI.Controls;

internal sealed class WorkspaceDocumentListView : Control
{
    private readonly MarkLeafScrollbar _scrollBar = new() { Dock = DockStyle.Right };
    private readonly List<WorkspaceDocumentEntry> _documents = [];
    private readonly List<(WorkspaceDocumentEntry Document, Rectangle Bounds)> _rows = [];
    private readonly Dictionary<string, int> _rowIndexByPath = new(StringComparer.OrdinalIgnoreCase);
    private Font _metadataFont = new("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _documentFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _previewFont = new("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _folderIconFont = new(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);
    private TextBox? _renameEditor;
    private WorkspaceDocumentEntry? _renameDocument;

    // Theme colors (defaults match white theme).
    private Color _bgPrimary = Color.White;
    private Color _bgHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
    private Color _themeLight = Color.FromArgb(0xE0, 0xE0, 0xE0);
    private Color _themeDark = Color.FromArgb(0xD0, 0xD0, 0xD0);
    private Color _textPrimary = Color.Black;
    private Color _textSecondary = Color.FromArgb(0x55, 0x55, 0x55);
    private Color _textTertiary = Color.FromArgb(0x6D, 0x6D, 0x6D);
    private Color _textSelected = Color.Black;
    private Color _textTertiarySelected = Color.Black;

    private string? _selectedPath;
    private string? _hoveredPath;
    private string? _contextMenuPath;
    private int _rowHeight;
    private int TopInset => 0;

    public WorkspaceDocumentListView()
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

    public event EventHandler<string>? DocumentActivated;
    public event EventHandler<WorkspaceDocumentContextEventArgs>? DocumentContextRequested;
    public event EventHandler<WorkspaceBackgroundContextEventArgs>? BackgroundContextRequested;
    public event EventHandler<WorkspaceFilesDroppedEventArgs>? FilesDropped;
    public event EventHandler<WorkspaceRenameRequestedEventArgs>? RenameRequested;

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
    public string PlaceholderText { get; set; } = Loc.Get("sidebar.noDocuments");

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
        if (colors.TryGetValue("text-tertiary-selected", out c)) _textTertiarySelected = c;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        _scrollBar.ApplyThemeColors(colors);
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
        RebuildRows();
        EnsureSelectionVisible();
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        var previousMetadata = _metadataFont;
        var previousDocument = _documentFont;
        var previousPreview = _previewFont;
        var previousFolderIcon = _folderIconFont;
        _metadataFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _documentFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        _previewFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _folderIconFont = new Font(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);
        var verticalPadding = this.ScaleForDpi(18);
        _rowHeight = (int)Math.Ceiling(
            _metadataFont.GetHeight(dpi) + _documentFont.GetHeight(dpi)
            + _previewFont.GetHeight(dpi) + verticalPadding);
        previousMetadata.Dispose();
        previousDocument.Dispose();
        previousPreview.Dispose();
        previousFolderIcon.Dispose();
        UpdateScrollBar();
        RebuildRows();
        Invalidate();
    }

    public void BeginInlineRename(WorkspaceEntry entry)
    {
        CancelInlineRename();
        _selectedPath = entry.FullPath;
        EnsureSelectionVisible();
        var index = _documents.FindIndex(document => PathEquals(document.FullPath, entry.FullPath));
        if (index < 0)
        {
            return;
        }

        var document = _documents[index];
        var rowBounds = GetDisplayBounds(_rows[index].Bounds);

        var horizontalPadding = this.ScaleForDpi(10);
        var metadataHeight = (int)Math.Ceiling(_metadataFont.GetHeight(DeviceDpi)) + this.ScaleForDpi(2);
        var top = rowBounds.Top + this.ScaleForDpi(3) + metadataHeight + this.ScaleForDpi(4);
        var bounds = new Rectangle(
            rowBounds.Left + horizontalPadding,
            top,
            Math.Max(this.ScaleForDpi(40), rowBounds.Width - horizontalPadding
                - ContentRightPadding(horizontalPadding)),
            _documentFont.Height + this.ScaleForDpi(2));
        _renameDocument = document;
        _renameEditor = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = _documentFont,
            BackColor = _themeLight,
            ForeColor = _textSelected,
            Text = entry.Name,
            Bounds = bounds,
        };
        _renameEditor.KeyDown += OnRenameEditorKeyDown;
        _renameEditor.LostFocus += (_, _) => CancelInlineRename();
        Controls.Add(_renameEditor);
        _renameEditor.BringToFront();
        BeginInvoke(() => FocusRenameEditor(_renameEditor));
        Invalidate(rowBounds);
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
                new Rectangle(
                    this.ScaleForDpi(16),
                    0,
                    Math.Max(0, ClientSize.Width
                        - (_scrollBar.Visible ? _scrollBar.Width : 0)
                        - this.ScaleForDpi(16)
                        - ContentRightPadding(this.ScaleForDpi(12))),
                    _rowHeight),
                _textTertiary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return;
        }

        var (firstIndex, lastIndex) = GetVisibleRowRange();
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var row = _rows[index];
            var document = row.Document;
            var bounds = GetDisplayBounds(row.Bounds);
            var isSelected = PathEquals(document.FullPath, _selectedPath);
            var isHovered = PathEquals(document.FullPath, _hoveredPath);
            var rightPadding = _scrollBar.Visible
                ? ContentRightPadding(this.ScaleForDpi(4))
                : _scrollBar.Width;
            var bgBounds = new Rectangle(
                bounds.X + this.ScaleForDpi(4), bounds.Y,
                Math.Max(0, bounds.Width - this.ScaleForDpi(4) - rightPadding), bounds.Height);
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
            else if (isHovered || PathEquals(document.FullPath, _contextMenuPath))
            {
                using var brush = new SolidBrush(_bgHover);
                SidebarGdi.FillRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), brush);
            }

            if (PathEquals(document.FullPath, _contextMenuPath))
            {
                using var pen = new Pen(_textSecondary, 2);
                SidebarGdi.DrawRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), pen);
            }

            DrawDocument(eventArgs.Graphics, document, bounds, isSelected);
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
            InvalidatePath(_hoveredPath);
            _hoveredPath = hoveredPath;
            InvalidatePath(_hoveredPath);
        }
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (_hoveredPath is not null)
        {
            var previous = _hoveredPath;
            _hoveredPath = null;
            InvalidatePath(previous);
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
        RebuildRows();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelInlineRename();
            _metadataFont.Dispose();
            _documentFont.Dispose();
            _previewFont.Dispose();
            _folderIconFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnDragEnter(DragEventArgs eventArgs)
    {
        base.OnDragEnter(eventArgs);
        eventArgs.Effect = eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) == true
            && GetDroppableFiles(eventArgs.Data).Length > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs eventArgs)
    {
        base.OnDragDrop(eventArgs);
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

    private void DrawDocument(Graphics graphics, WorkspaceDocumentEntry document, Rectangle bounds, bool isSelected)
    {
        var metaColor = isSelected ? _textTertiarySelected : _textTertiary;
        var horizontalPadding = this.ScaleForDpi(10);
        var topPadding = this.ScaleForDpi(3);
        var availableWidth = Math.Max(0, bounds.Width - horizontalPadding
            - ContentRightPadding(horizontalPadding));
        var metadataHeight = (int)Math.Ceiling(_metadataFont.GetHeight(DeviceDpi)) + this.ScaleForDpi(2);
        var iconAdvance = this.ScaleForDpi(14);
        var metadataBounds = new Rectangle(
            bounds.Left + horizontalPadding,
            bounds.Top + topPadding + this.ScaleForDpi(4),
            availableWidth,
            metadataHeight);
        var modifiedText = WorkspaceDocumentTimeFormatter.Format(document.LastWriteTime, DateTime.Now);
        var modifiedWidth = TextRenderer.MeasureText(
            graphics,
            modifiedText,
            _metadataFont,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var gap = this.ScaleForDpi(8);
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
            Math.Max(metadataBounds.Left, metadataBounds.Right - modifiedWidth - this.ScaleForDpi(9)),
            metadataBounds.Top,
            Math.Min(modifiedWidth, metadataBounds.Width),
            metadataBounds.Height);
        DrawText(
            graphics,
            SystemIconProvider.FolderIcon,
            _folderIconFont,
            iconBounds,
            metaColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine);
        DrawText(
            graphics,
            document.FolderName,
            _metadataFont,
            folderBounds,
            metaColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        DrawText(
            graphics,
            modifiedText,
            _metadataFont,
            modifiedBounds,
            metaColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        var documentBounds = new Rectangle(
            metadataBounds.Left,
            bounds.Top + topPadding + metadataHeight + this.ScaleForDpi(4),
            metadataBounds.Width,
            (int)Math.Ceiling(_documentFont.GetHeight(DeviceDpi)) + this.ScaleForDpi(2));
        if (!PathEquals(document.FullPath, _renameDocument?.FullPath))
        {
            DrawText(
                graphics,
                GetDisplayName(document.Name),
                _documentFont,
                documentBounds,
                isSelected ? _textSelected : ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        var previewBounds = new Rectangle(
            metadataBounds.Left,
            documentBounds.Bottom,
            availableWidth,
            (int)Math.Ceiling(_previewFont.GetHeight(DeviceDpi)) + this.ScaleForDpi(2));
        DrawText(
            graphics,
            document.Preview ?? string.Empty,
            _previewFont,
            previewBounds,
            metaColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void RebuildRows()
    {
        _rows.Clear();
        _rowIndexByPath.Clear();
        var width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0) - this.ScaleForDpi(4);
        var top = TopInset;
        for (var index = 0; index < _documents.Count; index++)
        {
            var document = _documents[index];
            _rows.Add((document, new Rectangle(this.ScaleForDpi(4), top, width, _rowHeight)));
            _rowIndexByPath[document.FullPath] = index;
            top += RowStride;
        }
    }

    private int RowStride => _rowHeight + this.ScaleForDpi(2);

    private Rectangle GetDisplayBounds(Rectangle contentBounds)
        => new(contentBounds.X, contentBounds.Y - _scrollBar.Value, contentBounds.Width, contentBounds.Height);

    private (int First, int Last) GetVisibleRowRange()
    {
        if (_rows.Count == 0)
        {
            return (0, -1);
        }

        var first = Math.Clamp(
            Math.Max(0, _scrollBar.Value - TopInset) / RowStride,
            0,
            _rows.Count - 1);
        var last = Math.Clamp(
            Math.Max(0, _scrollBar.Value + ClientSize.Height - TopInset) / RowStride,
            first,
            _rows.Count - 1);
        return (first, last);
    }

    private void InvalidatePath(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (_rowIndexByPath.TryGetValue(path, out var index))
        {
            Invalidate(GetDisplayBounds(_rows[index].Bounds));
        }
    }

    private int ContentRightPadding(int defaultPadding)
        => _scrollBar.Visible && _scrollBar.AutoHide ? 0 : defaultPadding;

    private (WorkspaceDocumentEntry Document, Rectangle Bounds)? HitTestRow(Point location)
    {
        var contentY = location.Y + _scrollBar.Value - TopInset;
        if (contentY < 0)
        {
            return null;
        }

        var index = contentY / RowStride;
        if (index < 0 || index >= _rows.Count || contentY % RowStride >= _rowHeight)
        {
            return null;
        }

        var row = _rows[index];
        var bounds = GetDisplayBounds(row.Bounds);
        return bounds.Contains(location) ? (row.Document, bounds) : null;
    }

    private void UpdateScrollBar()
    {
        var contentHeight = TopInset + _documents.Count * RowStride - this.ScaleForDpi(2);
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

        var index = _documents.FindIndex(document => PathEquals(document.FullPath, _selectedPath));
        if (index < 0)
        {
            return;
        }

        var bounds = GetDisplayBounds(_rows[index].Bounds);
        if (bounds.Top < 0)
        {
            _scrollBar.Value = Math.Clamp(_scrollBar.Value + bounds.Top, 0, GetMaximumScrollValue());
        }
        else if (bounds.Bottom > ClientSize.Height)
        {
            _scrollBar.Value = Math.Clamp(
                _scrollBar.Value + bounds.Bottom - ClientSize.Height,
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

    private static void FocusRenameEditor(TextBox? editor)
    {
        if (editor is null || editor.IsDisposed)
        {
            return;
        }
        editor.Focus();
        var extensionLength = Path.GetExtension(editor.Text).Length;
        editor.Select(0, Math.Max(0, editor.Text.Length - extensionLength));
    }

    private void OnRenameEditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.Escape)
        {
            eventArgs.SuppressKeyPress = true;
            CancelInlineRename();
            Focus();
            return;
        }
        if (eventArgs.KeyCode != Keys.Enter || _renameEditor is null || _renameDocument is null)
        {
            return;
        }

        eventArgs.SuppressKeyPress = true;
        var document = _renameDocument;
        var name = _renameEditor.Text;
        CancelInlineRename();
        RenameRequested?.Invoke(this, new WorkspaceRenameRequestedEventArgs(
            new WorkspaceEntry(document.Name, document.FullPath, false),
            name));
    }

    private void CancelInlineRename()
    {
        var editor = _renameEditor;
        _renameEditor = null;
        _renameDocument = null;
        if (editor is not null)
        {
            Controls.Remove(editor);
            editor.Dispose();
            Invalidate();
        }
    }


    private static string[] GetDroppableFiles(IDataObject? data)
    {
        if (data?.GetDataPresent(DataFormats.FileDrop) != true) return [];
        var paths = data.GetData(DataFormats.FileDrop) as string[];
        return paths?.Where(WorkspaceTreeView.IsDroppableFile).Take(32).ToArray() ?? [];
    }
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
