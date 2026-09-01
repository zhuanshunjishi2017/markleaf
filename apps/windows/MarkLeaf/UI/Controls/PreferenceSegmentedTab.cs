using System.ComponentModel;
using MarkLeaf.Services;

namespace MarkLeaf.UI.Controls;

internal sealed class PreferenceSegmentedTab : Control
{
    private const int SelectionAnimationDurationMs = 180;

    private readonly string[] _tabs;
    private Rectangle[] _tabBounds;
    private readonly System.Windows.Forms.Timer _selectionAnimationTimer = new() { Interval = 15 };
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private Color _background = SystemColors.ControlLight;
    private Color _containerBackground = SystemColors.ControlLight;
    private Color _selection = SystemColors.Highlight;
    private Color _textPrimary = SystemColors.ControlText;
    private Color _textSelected = SystemColors.HighlightText;
    private Font _textFont = new("Microsoft YaHei", 8F, FontStyle.Bold, GraphicsUnit.Point);
    private RectangleF _selectionBounds;
    private RectangleF _selectionAnimationStartBounds;
    private RectangleF _selectionAnimationTargetBounds;
    private long _selectionAnimationStartedAt;
    private bool _selectionBoundsInitialized;
    private bool _selectionAnimationActive;

    public PreferenceSegmentedTab(params string[] tabs)
    {
        _tabs = tabs;
        _tabBounds = new Rectangle[tabs.Length];
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        Height = this.ScaleForDpi(34);
        TabStop = true;
        _selectionAnimationTimer.Tick += SelectionAnimationTimer_Tick;
    }

    public event EventHandler<int>? TabChanged;

    public int PreferredTabWidth
    {
        get
        {
            var horizontalPadding = this.ScaleForDpi(12);
            return _tabs.Sum(tab => TextRenderer.MeasureText(
                tab, _textFont, Size.Empty,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + horizontalPadding * 2);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < 0 || value >= _tabs.Length || value == _selectedIndex) return;
            _selectedIndex = value;
            if (_selectionBoundsInitialized && _tabBounds[value].Width > 0)
                StartSelectionAnimation(_tabBounds[value]);
            else
                ResetSelectionAnimation();
            Invalidate();
            TabChanged?.Invoke(this, value);
        }
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-selected", out var color)) _background = color;
        if (colors.TryGetValue("bg-secondary", out color)) _containerBackground = color;
        if (colors.TryGetValue("theme-light", out color)) _selection = color;
        if (colors.TryGetValue("text-primary", out color)) _textPrimary = color;
        if (colors.TryGetValue("text-selected", out color)) _textSelected = color;
        BackColor = _background;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);
        if (_tabs.Length == 0) return;

        using (var containerBrush = new SolidBrush(_containerBackground))
        {
            var lowerHalfTop = ClientSize.Height / 2;
            e.Graphics.FillRectangle(
                containerBrush,
                0,
                lowerHalfTop,
                ClientSize.Width,
                ClientSize.Height - lowerHalfTop);
        }

        var radius = this.ScaleForDpi(5);
        var horizontalPadding = this.ScaleForDpi(10);
        var verticalPadding = this.ScaleForDpi(3);
        var textHeight = TextRenderer.MeasureText(
            e.Graphics, "Ag", _textFont, Size.Empty,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Height;
        var tabHeight = textHeight + verticalPadding * 2;
        var widths = new int[_tabs.Length];
        var totalWidth = 0;
        for (var index = 0; index < _tabs.Length; index++)
        {
            widths[index] = TextRenderer.MeasureText(
                e.Graphics, _tabs[index], _textFont, Size.Empty,
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + horizontalPadding * 2;
            totalWidth += widths[index];
        }

        var groupBounds = new Rectangle(
            Math.Max(0, (ClientSize.Width - totalWidth) / 2),
            Math.Max(0, (ClientSize.Height - tabHeight) / 2),
            Math.Min(totalWidth, ClientSize.Width),
            tabHeight);
        using (var backgroundBrush = new SolidBrush(_background))
            SidebarGdi.FillRoundedRect(e.Graphics, groupBounds, radius, backgroundBrush);

        var x = groupBounds.Left;
        for (var index = 0; index < _tabs.Length; index++)
        {
            var width = index == _tabs.Length - 1 ? groupBounds.Right - x : widths[index];
            var bounds = new Rectangle(x, groupBounds.Top, width, groupBounds.Height);
            _tabBounds[index] = bounds;
            x += width;
        }

        var targetBounds = (RectangleF)_tabBounds[_selectedIndex];
        if (!_selectionBoundsInitialized)
        {
            _selectionBounds = targetBounds;
            _selectionBoundsInitialized = true;
        }
        else if (_selectionAnimationActive)
        {
            _selectionAnimationTargetBounds = targetBounds;
        }

        using (var selectionBrush = new SolidBrush(_selection))
            SidebarGdi.FillRoundedRect(e.Graphics, Rectangle.Round(_selectionBounds), radius, selectionBrush);

        for (var index = 0; index < _tabs.Length; index++)
        {
            TextRenderer.DrawText(
                e.Graphics,
                _tabs[index],
                _textFont,
                _tabBounds[index],
                index == _selectedIndex ? _textSelected : _textPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    private void StartSelectionAnimation(Rectangle targetBounds)
    {
        _selectionAnimationStartBounds = _selectionBounds;
        _selectionAnimationTargetBounds = targetBounds;
        _selectionAnimationStartedAt = Environment.TickCount64;
        _selectionAnimationActive = true;
        _selectionAnimationTimer.Start();
    }

    private void SelectionAnimationTimer_Tick(object? sender, EventArgs e)
    {
        var progress = Math.Clamp(
            (Environment.TickCount64 - _selectionAnimationStartedAt) / (double)SelectionAnimationDurationMs,
            0.0,
            1.0);
        var eased = progress < 0.5
            ? 4.0 * progress * progress * progress
            : 1.0 - Math.Pow(-2.0 * progress + 2.0, 3.0) / 2.0;

        _selectionBounds = InterpolateBounds(
            _selectionAnimationStartBounds,
            _selectionAnimationTargetBounds,
            (float)eased);

        if (progress >= 1.0)
        {
            _selectionBounds = _selectionAnimationTargetBounds;
            _selectionAnimationActive = false;
            _selectionAnimationTimer.Stop();
        }

        Invalidate();
    }

    private static RectangleF InterpolateBounds(RectangleF start, RectangleF end, float progress)
    {
        return new RectangleF(
            start.X + (end.X - start.X) * progress,
            start.Y + (end.Y - start.Y) * progress,
            start.Width + (end.Width - start.Width) * progress,
            start.Height + (end.Height - start.Height) * progress);
    }

    private void ResetSelectionAnimation()
    {
        _selectionAnimationTimer.Stop();
        _selectionAnimationActive = false;
        _selectionBoundsInitialized = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hoveredIndex = HitTest(e.Location);
        if (hoveredIndex == _hoveredIndex) return;
        _hoveredIndex = hoveredIndex;
        Cursor = hoveredIndex >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredIndex = -1;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var index = HitTest(e.Location);
        if (index >= 0)
        {
            Focus();
            SelectedIndex = index;
        }
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Left or Keys.Right or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selectedIndex - 1);
        if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(_tabs.Length - 1, _selectedIndex + 1);
    }

    private int HitTest(Point point)
    {
        for (var index = 0; index < _tabBounds.Length; index++)
        {
            if (_tabBounds[index].Contains(point)) return index;
        }
        return -1;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        ResetSelectionAnimation();
        base.OnSizeChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _selectionAnimationTimer.Dispose();
            _textFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class PreferenceOptionsContainer : Control
{
    private readonly PreferenceSegmentedTab? _tab;
    private readonly Panel _contentHost = new();
    private readonly Control[] _pages;
    private Color _background = SystemColors.ControlLight;
    private Color _border = SystemColors.Control;

    public PreferenceOptionsContainer(string[] labels, Control[] pages)
    {
        _pages = pages;
        _tab = new PreferenceSegmentedTab(labels);
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        _contentHost.Controls.Add(pages[0]);
        _tab.TabChanged += (_, index) =>
        {
            _contentHost.Controls.Clear();
            _contentHost.Controls.Add(_pages[index]);
        };
        Controls.Add(_contentHost);
        Controls.Add(_tab);
    }

    public PreferenceOptionsContainer(Control page)
    {
        _pages = [page];
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        _contentHost.Controls.Add(page);
        Controls.Add(_contentHost);
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-secondary", out var color)) _background = color;
        if (colors.TryGetValue("bg-selected", out color)) _border = color;
        _contentHost.BackColor = _background;
        foreach (var page in _pages)
            page.BackColor = _background;
        _tab?.ApplyThemeColors(colors);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(Parent?.BackColor ?? BackColor);

        var margin = this.ScaleForDpi(20);
        var bottomMargin = this.ScaleForDpi(10);
        var top = _tab is null ? this.ScaleForDpi(15) : _tab.Top + _tab.Height / 2;
        var bounds = new Rectangle(
            margin,
            top,
            Math.Max(1, ClientSize.Width - margin * 2 - 1),
            Math.Max(1, ClientSize.Height - top - bottomMargin - 1));
        var radius = this.ScaleForDpi(4);
        using (var backgroundBrush = new SolidBrush(_background))
            SidebarGdi.FillRoundedRect(e.Graphics, bounds, radius, backgroundBrush);
        using var borderPen = new Pen(_border, 1F);
        SidebarGdi.DrawRoundedRect(e.Graphics, bounds, radius, borderPen);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var margin = this.ScaleForDpi(20);
        var bottomMargin = this.ScaleForDpi(10);
        var borderTop = this.ScaleForDpi(15);
        if (_tab is not null)
        {
            var tabHeight = this.ScaleForDpi(34);
            var tabWidth = Math.Min(_tab.PreferredTabWidth, Math.Max(1, ClientSize.Width - margin * 2));
            _tab.SetBounds(Math.Max(0, (ClientSize.Width - tabWidth) / 2), 0, tabWidth, tabHeight);
            borderTop = _tab.Top + _tab.Height / 2;
        }
        _contentHost.SetBounds(
            margin + 1,
            borderTop + 1,
            Math.Max(1, ClientSize.Width - margin * 2 - 3),
            Math.Max(1, ClientSize.Height - borderTop - bottomMargin - 3));
        _tab?.BringToFront();
    }
}
