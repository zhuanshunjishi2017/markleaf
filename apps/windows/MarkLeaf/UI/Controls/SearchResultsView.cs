using System.ComponentModel;
using System.Drawing.Drawing2D;
using MarkLeaf.Services;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI.Controls;

internal sealed class SearchResultsView : Control
{
    private readonly MarkLeafScrollbar _scrollBar = new() { Dock = DockStyle.Right };
    private readonly List<SearchResult> _results = [];
    private readonly List<(SearchResult Result, Rectangle Bounds)> _visibleRows = [];
    private Font _metadataFont = new("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _fileNameFont = new("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
    private Font _snippetFont = new("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _folderIconFont = new(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);

    private Color _bgPrimary = Color.White;
    private Color _bgHover = Color.FromArgb(0xF0, 0xF0, 0xF0);
    private Color _textPrimary = Color.Black;
    private Color _textTertiary = Color.FromArgb(0x6D, 0x6D, 0x6D);

    private string? _hoveredPath;
    private int _rowHeight;

    public SearchResultsView()
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

    public event EventHandler<string>? ResultActivated;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoHideScrollbar
    {
        get => _scrollBar.AutoHide;
        set => _scrollBar.AutoHide = value;
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var c)) _bgPrimary = c;
        if (colors.TryGetValue("bg-hover", out c)) _bgHover = c;
        if (colors.TryGetValue("text-primary", out c)) _textPrimary = c;
        if (colors.TryGetValue("text-tertiary", out c)) _textTertiary = c;
        BackColor = _bgPrimary;
        ForeColor = _textPrimary;
        _scrollBar.ApplyThemeColors(colors);
        Invalidate();
    }

    public void SetResults(IReadOnlyList<SearchResult> results)
    {
        _results.Clear();
        _results.AddRange(results);
        UpdateScrollBar();
        Invalidate();
    }

    public void ConfigureTypography(int dpi)
    {
        var previousMetadata = _metadataFont;
        var previousFileName = _fileNameFont;
        var previousSnippet = _snippetFont;
        var previousFolderIcon = _folderIconFont;
        _metadataFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _fileNameFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        _snippetFont = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        _folderIconFont = new Font(SystemIconProvider.IconFontName, 8F, FontStyle.Regular, GraphicsUnit.Point);
        var verticalPadding = this.ScaleForDpi(18);
        _rowHeight = (int)Math.Ceiling(
            _metadataFont.GetHeight(dpi) + _fileNameFont.GetHeight(dpi)
            + _snippetFont.GetHeight(dpi) + verticalPadding);
        previousMetadata.Dispose();
        previousFileName.Dispose();
        previousSnippet.Dispose();
        previousFolderIcon.Dispose();
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.Clear(BackColor);
        if (_results.Count == 0)
        {
            DrawText(
                eventArgs.Graphics,
                Loc.Get("sidebar.noSearchResults"),
                _snippetFont,
                new Rectangle(this.ScaleForDpi(16), 0, Math.Max(0, ClientSize.Width - this.ScaleForDpi(28)), _rowHeight),
                _textTertiary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return;
        }

        BuildVisibleRows();
        foreach (var (result, bounds) in _visibleRows)
        {
            if (bounds.Bottom <= 0 || bounds.Top >= ClientSize.Height)
            {
                continue;
            }

            var isHovered = string.Equals(result.FullPath, _hoveredPath, StringComparison.OrdinalIgnoreCase);
            var bgBounds = new Rectangle(
                bounds.X + this.ScaleForDpi(4), bounds.Y,
                Math.Max(0, bounds.Width - this.ScaleForDpi(8)), bounds.Height);
            if (isHovered)
            {
                using var brush = new SolidBrush(_bgHover);
                SidebarGdi.FillRoundedRect(eventArgs.Graphics, bgBounds, this.ScaleForDpi(8), brush);
            }

            DrawResult(eventArgs.Graphics, result, bounds);
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

        if (eventArgs.Button == MouseButtons.Left)
        {
            ResultActivated?.Invoke(this, row.Value.Result.FullPath);
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        var row = HitTestRow(eventArgs.Location);
        var hoveredPath = row?.Result.FullPath;
        if (!string.Equals(hoveredPath, _hoveredPath, StringComparison.OrdinalIgnoreCase))
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
            _fileNameFont.Dispose();
            _snippetFont.Dispose();
            _folderIconFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void DrawResult(Graphics graphics, SearchResult result, Rectangle bounds)
    {
        var horizontalPadding = this.ScaleForDpi(10);
        var topPadding = this.ScaleForDpi(3);
        var availableWidth = Math.Max(0, bounds.Width - horizontalPadding * 2);
        var metadataHeight = (int)Math.Ceiling(_metadataFont.GetHeight(DeviceDpi)) + this.ScaleForDpi(2);
        var iconAdvance = this.ScaleForDpi(14);

        // 第一行：文件夹图标 + 文件夹名称 + 修改时间
        var metadataBounds = new Rectangle(
            bounds.Left + horizontalPadding,
            bounds.Top + topPadding + this.ScaleForDpi(4),
            availableWidth,
            metadataHeight);
        var modifiedText = WorkspaceDocumentTimeFormatter.Format(result.LastWriteTime, DateTime.Now);
        var modifiedWidth = TextRenderer.MeasureText(
            graphics, modifiedText, _metadataFont,
            Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        var gap = this.ScaleForDpi(8);
        var iconBounds = new Rectangle(
            metadataBounds.Left, metadataBounds.Top, iconAdvance, metadataBounds.Height);
        var folderBounds = new Rectangle(
            metadataBounds.Left + iconAdvance,
            metadataBounds.Top,
            Math.Max(0, metadataBounds.Width - modifiedWidth - gap - iconAdvance),
            metadataBounds.Height);
        var modifiedBounds = new Rectangle(
            Math.Max(metadataBounds.Left, metadataBounds.Right - modifiedWidth - this.ScaleForDpi(4)),
            metadataBounds.Top,
            Math.Min(modifiedWidth, metadataBounds.Width),
            metadataBounds.Height);
        DrawText(
            graphics, SystemIconProvider.FolderIcon, _folderIconFont, iconBounds, _textTertiary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine);
        DrawText(
            graphics, result.FolderName, _metadataFont, folderBounds, _textTertiary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        DrawText(
            graphics, modifiedText, _metadataFont, modifiedBounds, _textTertiary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        // 第二行：文件名称
        var fileNameBounds = new Rectangle(
            metadataBounds.Left,
            bounds.Top + topPadding + metadataHeight + this.ScaleForDpi(4),
            availableWidth,
            (int)Math.Ceiling(_fileNameFont.GetHeight(DeviceDpi)) + this.ScaleForDpi(2));
        DrawText(
            graphics, GetDisplayName(result.FileName), _fileNameFont, fileNameBounds, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        // 第三行：匹配片段
        var snippetText = string.IsNullOrWhiteSpace(result.Snippet) ? result.FolderName : result.Snippet;
        var snippetBounds = new Rectangle(
            metadataBounds.Left,
            fileNameBounds.Bottom,
            availableWidth,
            (int)Math.Ceiling(_snippetFont.GetHeight(DeviceDpi)) + this.ScaleForDpi(2));
        DrawText(
            graphics, snippetText, _snippetFont, snippetBounds, _textTertiary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    private void BuildVisibleRows()
    {
        _visibleRows.Clear();
        var top = 0 - _scrollBar.Value;
        var width = ClientSize.Width - (_scrollBar.Visible ? _scrollBar.Width : 0) - this.ScaleForDpi(4);
        foreach (var result in _results)
        {
            _visibleRows.Add((result, new Rectangle(this.ScaleForDpi(4), top, width, _rowHeight)));
            top += _rowHeight + this.ScaleForDpi(2);
        }
    }

    private (SearchResult Result, Rectangle Bounds)? HitTestRow(Point location)
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
        var contentHeight = _results.Count * (_rowHeight + this.ScaleForDpi(2)) - this.ScaleForDpi(2);
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

    private static string GetDisplayName(string name)
    {
        return string.Equals(Path.GetExtension(name), ".md", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
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
}
