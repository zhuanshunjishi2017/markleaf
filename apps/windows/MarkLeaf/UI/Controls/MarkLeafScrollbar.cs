using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MarkLeaf.UI.Controls;

internal sealed class MarkLeafScrollbar : Control
{
    // 视觉宽度对齐编辑器滚动条（6 CSS px）。控件布局宽即视觉宽，
    // 滑块满宽绘制、贴右缘。
    // 视觉宽度基准是 CSS 像素（与编辑器 6px 一致），换算用 96dpi 而非点数。
    private const float ControlLayoutWidthPixels = 6F;
    private const int ThumbRadius = 3;
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

    // 滑块透明度动画。参数与共享内核 scrollbar-motion 的编辑器滚动条保持
    // 一致：单一过渡时长 200ms、空闲 800ms 后淡出、二次缓入缓出（quad
    // ease-in-out），让侧栏与编辑区的滚动条动画节奏相同。
    private const int FadeMilliseconds = 200;
    private const int IdleHideMilliseconds = 800;
    private const int AnimationIntervalMilliseconds = 16;
    private int _animationStartTick;
    private float _animationFromAlpha;
    private float _thumbTargetAlpha = 1f;
    private float _thumbAlpha = 1f;
    private int _lastActivityTick;
    private readonly System.Windows.Forms.Timer _alphaTimer = new() { Interval = AnimationIntervalMilliseconds };

    public MarkLeafScrollbar()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        TabStop = false;
        _alphaTimer.Tick += (_, _) => AlphaTick();
        Disposed += (_, _) => _alphaTimer.Dispose();
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
            if (!_autoHide)
            {
                _alphaTimer.Stop();
                _thumbAlpha = _thumbTargetAlpha = 1f;
            }
            else
            {
                _thumbAlpha = _thumbTargetAlpha = 0f;
            }
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

    /// <summary>宿主视图发生滚动（滚轮、键盘、程序滚动）时调用：滑块淡入并保持到空闲。</summary>
    public void NotifyScrollActivity()
    {
        if (!AutoHide) return;
        _lastActivityTick = Environment.TickCount;
        EnsureAlphaTimerRunning();
    }

    public void SetMouseNearRightEdge(bool near)
    {
        if (_mouseNearRightEdge != near)
        {
            _mouseNearRightEdge = near;
            EnsureAlphaTimerRunning();
            Invalidate();
        }
    }

    private float CurrentThumbAlpha()
    {
        return InteractionShowsThumb() ? 1f : _thumbAlpha;
    }

    private void BeginAlphaTransition(float target)
    {
        if (target == _thumbTargetAlpha) return;
        _thumbTargetAlpha = target;
        _animationFromAlpha = CurrentThumbAlpha();
        _animationStartTick = Environment.TickCount;
        EnsureAlphaTimerRunning();
    }

    private void EnsureAlphaTimerRunning()
    {
        if (!_alphaTimer.Enabled) _alphaTimer.Start();
    }

    /// <summary>
    /// 动画帧：目标透明度由交互状态与空闲时长决定；过渡使用与内核
    /// scrollbar-motion 相同的二次缓入缓出曲线和 200ms 时长，保证侧栏与
    /// 编辑区滚动条动画一致。完全隐藏或常显时停表。
    /// </summary>
    private void AlphaTick()
    {
        var target = 0f;
        if (!_autoHide || _mouseInControl || _mouseNearRightEdge || _dragging)
        {
            target = 1f;
        }
        else if (Environment.TickCount - _lastActivityTick < IdleHideMilliseconds)
        {
            target = 1f;
        }

        if (target != _thumbTargetAlpha)
        {
            _thumbTargetAlpha = target;
            _animationFromAlpha = CurrentThumbAlpha();
            _animationStartTick = Environment.TickCount;
        }

        var progress = Math.Clamp(
            (Environment.TickCount - _animationStartTick) / (float)FadeMilliseconds, 0f, 1f);
        // quad ease-in-out，与 scrollbarAlphaAnimation 的实现一致。
        var eased = progress < 0.5f
            ? 2 * progress * progress
            : 1f - MathF.Pow(-2 * progress + 2, 2) / 2f;
        var next = _animationFromAlpha + (_thumbTargetAlpha - _animationFromAlpha) * eased;

        if (Math.Abs(next - CurrentThumbAlpha()) > 0.001f)
        {
            _thumbAlpha = next;
            Invalidate();
        }
        else if (progress >= 1f)
        {
            _thumbAlpha = _thumbTargetAlpha;
        }

        var settled = progress >= 1f;
        var waitingForIdle = _autoHide
            && _thumbTargetAlpha > 0f
            && Environment.TickCount - _lastActivityTick < IdleHideMilliseconds
            && !_mouseInControl && !_mouseNearRightEdge && !_dragging;
        if (settled && !waitingForIdle && (!_autoHide || _thumbAlpha == 0f))
        {
            _alphaTimer.Stop();
        }
    }

    public void RaiseScroll(ScrollEventType type)
    {
        Scroll?.Invoke(this, new ScrollEventArgs(type, _value));
    }

    private bool InteractionShowsThumb() => !_autoHide || _mouseInControl || _mouseNearRightEdge || _dragging;

    private void UpdateLayoutWidth()
    {
        Width = Math.Max(1, (int)Math.Round(ControlLayoutWidthPixels * DeviceDpi / 96F));
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
        return new Rectangle(0, ThumbTop(), ClientSize.Width, ThumbHeight());
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
        var alpha = CurrentThumbAlpha();
        var showParts = alpha > 0.01f;

        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        if (canScroll && showParts)
        {
            var baseColor = _dragging || _thumbHovered ? _thumbActive : _thumbIdle;
            var color = Color.FromArgb(
                (byte)Math.Clamp(baseColor.A * alpha, 0, 255),
                baseColor.R, baseColor.G, baseColor.B);
            using var brush = new SolidBrush(color);
            SidebarGdi.FillRoundedRect(e.Graphics, ThumbBounds(), this.ScaleForDpi(ThumbRadius), brush);
        }

        e.Graphics.SmoothingMode = SmoothingMode.Default;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _mouseInControl = true;
        EnsureAlphaTimerRunning();
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouseInControl = false;
        _thumbHovered = false;
        _lastActivityTick = Environment.TickCount;
        EnsureAlphaTimerRunning();
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
            _lastActivityTick = Environment.TickCount;
            EnsureAlphaTimerRunning();
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
        _lastActivityTick = Environment.TickCount;
        EnsureAlphaTimerRunning();
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            RaiseScroll(ScrollEventType.ThumbPosition);
            Invalidate();
        }
    }

}
