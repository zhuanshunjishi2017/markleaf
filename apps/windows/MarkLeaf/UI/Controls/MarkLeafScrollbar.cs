using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MarkLeaf.UI.Controls;

internal sealed class MarkLeafScrollbar : Control
{
    private const float ControlLayoutWidthPoints = 7F;
    private const float ThumbRightPaddingPoints = 1F;
    private const int ThumbRadius = 4;
    private const int MinThumbHeight = 24;
    private const int MaxThumbHeight = 128;

    private Color _thumbIdle = Color.FromArgb(0x8B, 0x8B, 0x8B);
    private Color _thumbActive = Color.FromArgb(0x63, 0x63, 0x63);

    private int _minimum;
    private int _maximum;
    private int _value;
    private int _largeChange = 1;
    private int _smallChange = 1;

    private bool _autoHide;
    private bool _mouseInControl;
    private bool _mouseNearRightEdge;
    private bool _thumbHovered;
    private bool _dragging;
    private int _dragThumbOffset;

    public MarkLeafScrollbar()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        TabStop = false;
        UpdateLayoutWidth();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateLayoutWidth();
    }

    public void ApplyThemeColors(IReadOnlyDictionary<string, Color> colors)
    {
        if (colors.TryGetValue("bg-primary", out var bg)) BackColor = bg;
        if (colors.TryGetValue("scrollbar-idle", out var idle)) _thumbIdle = idle;
        if (colors.TryGetValue("scrollbar-active", out var active)) _thumbActive = active;
        Invalidate();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, GetMaximumScrollValue());
            if (_value != clamped)
            {
                _value = clamped;
                Invalidate();
            }
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange
    {
        get => _largeChange;
        set { _largeChange = Math.Max(1, value); Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SmallChange
    {
        get => _smallChange;
        set { _smallChange = Math.Max(1, value); Invalidate(); }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoHide
    {
        get => _autoHide;
        set
        {
            _autoHide = value;
            Invalidate();
        }
    }

    public event ScrollEventHandler? Scroll;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool Visible
    {
        get => base.Visible;
        set => base.Visible = value;
    }

    public void SetMouseNearRightEdge(bool near)
    {
        if (_mouseNearRightEdge != near)
        {
            _mouseNearRightEdge = near;
            Invalidate();
        }
    }

    public void RaiseScroll(ScrollEventType type)
    {
        Scroll?.Invoke(this, new ScrollEventArgs(type, _value));
    }

    private bool IsThumbVisible() => !_autoHide || _mouseInControl || _mouseNearRightEdge || _dragging;

    private void UpdateLayoutWidth()
    {
        Width = Math.Max(1, (int)Math.Round(ControlLayoutWidthPoints * DeviceDpi / 72F));
    }

    private int GetMaximumScrollValue() => Math.Max(0, _maximum - _largeChange + 1);

    private int TrackTop() => 0;

    private int TrackHeight() => ClientSize.Height;

    private int ThumbHeight()
    {
        if (_maximum <= _minimum) return TrackHeight();
        var range = _maximum - _minimum + _largeChange;
        var ratio = (double)_largeChange / range;
        var minimumHeight = Math.Min(TrackHeight(), this.ScaleForDpi(MinThumbHeight));
        var maximumHeight = Math.Min(TrackHeight(), this.ScaleForDpi(MaxThumbHeight));
        return Math.Clamp(
            (int)(TrackHeight() * ratio),
            minimumHeight,
            maximumHeight);
    }

    private int ThumbTop()
    {
        if (_maximum <= _minimum) return TrackTop();
        var thumbH = ThumbHeight();
        var available = TrackHeight() - thumbH;
        if (available <= 0) return TrackTop();
        var maxScroll = Math.Max(1, GetMaximumScrollValue());
        return TrackTop() + (int)((_value - _minimum) / (double)maxScroll * available);
    }

    private Rectangle ThumbBounds()
    {
        var t = ThumbTop();
        var rightPadding = Math.Max(1, (int)Math.Round(ThumbRightPaddingPoints * DeviceDpi / 72F));
        return new Rectangle(0, t, Math.Max(1, ClientSize.Width - rightPadding), ThumbHeight());
    }

    private Rectangle ThumbDragBounds()
    {
        var t = ThumbTop();
        return new Rectangle(0, t, ClientSize.Width, ThumbHeight());
    }

    private Rectangle TrackBounds() => new(0, TrackTop(), ClientSize.Width, TrackHeight());

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);

        var canScroll = _maximum > _minimum;
        var showParts = !_autoHide || IsThumbVisible();

        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        if (canScroll && showParts)
        {
            var color = _dragging || _thumbHovered ? _thumbActive : _thumbIdle;
            using var brush = new SolidBrush(color);
            SidebarGdi.FillRoundedRect(e.Graphics, ThumbBounds(), this.ScaleForDpi(ThumbRadius), brush);
        }

        e.Graphics.SmoothingMode = SmoothingMode.Default;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _mouseInControl = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInControl = false;
        _thumbHovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (_minimum >= GetMaximumScrollValue()) return;

        var thumbBounds = ThumbDragBounds();
        if (thumbBounds.Contains(e.Location))
        {
            _dragging = true;
            _dragThumbOffset = e.Y - thumbBounds.Top;
            Capture = true;
            Invalidate();
            return;
        }

        var trackBounds = TrackBounds();
        if (trackBounds.Contains(e.Location))
        {
            PositionThumbToClick(e.Y);
        }
    }

    /// <summary>
    /// 点击轨道（不在滑块上）时，把滑块中心移动到点击处并将内容滚动到该位置，
    /// 随后进入拖动状态，便于继续拖拽。
    /// </summary>
    private void PositionThumbToClick(int y)
    {
        var thumbH = ThumbHeight();
        var available = TrackHeight() - thumbH;
        if (available <= 0)
        {
            return;
        }

        var targetThumbTop = y - thumbH / 2;
        var ratio = Math.Clamp((targetThumbTop - TrackTop()) / (double)available, 0.0, 1.0);
        var maxScroll = GetMaximumScrollValue();
        Value = _minimum + (int)(ratio * maxScroll);
        RaiseScroll(ScrollEventType.ThumbTrack);

        _dragging = true;
        _dragThumbOffset = thumbH / 2;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            var thumbH = ThumbHeight();
            var available = TrackHeight() - thumbH;
            if (available > 0)
            {
                var targetThumbTop = e.Y - _dragThumbOffset;
                var ratio = Math.Clamp((targetThumbTop - TrackTop()) / (double)available, 0.0, 1.0);
                var maxScroll = GetMaximumScrollValue();
                Value = _minimum + (int)(ratio * maxScroll);
                RaiseScroll(ScrollEventType.ThumbTrack);
            }
            return;
        }

        var wasHovered = _thumbHovered;
        _thumbHovered = ThumbDragBounds().Contains(e.Location);
        if (wasHovered != _thumbHovered) Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            RaiseScroll(ScrollEventType.ThumbPosition);
            Invalidate();
        }
    }

}
