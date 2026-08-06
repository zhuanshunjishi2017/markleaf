using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MarkLeaf.UI.Controls;

internal sealed class MarkLeafScrollbar : Control
{
    private const int TrackWidth = 8;
    private const int ControlLayoutWidth = 14;
    private const int ThumbLeftInset = 2;
    private const int ArrowLeftInset = 0;
    private const int ArrowTopSpacing = 2;
    private const int ArrowBottomSpacing = 0;
    private const int TrackTopShift = 6;
    private const int ThumbRadius = 4;
    private const int MinThumbHeight = 24;
    private const int ArrowSizeDpi = 18;

    private static readonly Color ThumbIdle = Color.FromArgb(0x8B, 0x8B, 0x8B);
    private static readonly Color ThumbActive = Color.FromArgb(0x63, 0x63, 0x63);

    private readonly System.Windows.Forms.Timer _arrowTimer = new() { Interval = 50 };
    private readonly Font _arrowFont;

    private int _minimum;
    private int _maximum;
    private int _value;
    private int _largeChange = 1;
    private int _smallChange = 1;

    private bool _autoHide;
    private bool _mouseInControl;
    private bool _thumbHovered;
    private bool _dragging;
    private int _dragThumbOffset;
    private int _arrowDirection;
    private bool _arrowInitialDelay = true;

    public MarkLeafScrollbar()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        TabStop = false;
        Width = ScaleForDpi(ControlLayoutWidth);
        BackColor = Color.White;
        _arrowFont = new Font(SystemIconProvider.IconFontName, 7F, FontStyle.Regular, GraphicsUnit.Point);
        _arrowTimer.Tick += OnArrowTimerTick;
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

    public void RaiseScroll(ScrollEventType type)
    {
        Scroll?.Invoke(this, new ScrollEventArgs(type, _value));
    }

    private bool IsThumbVisible() => !_autoHide || _mouseInControl || _dragging || _arrowDirection != 0;

    private int GetMaximumScrollValue() => Math.Max(0, _maximum - _largeChange + 1);

    private int ArrowHeight() => ScaleForDpi(ArrowSizeDpi);

    private int TrackTop() => ArrowHeight() + ScaleForDpi(ArrowTopSpacing * 2 - TrackTopShift);

    private int TrackBottom() => ClientSize.Height - ArrowHeight() - ScaleForDpi(ArrowBottomSpacing);

    private int TrackHeight() => Math.Max(0, TrackBottom() - TrackTop());

    private int ThumbHeight()
    {
        if (_maximum <= _minimum) return TrackHeight();
        var range = _maximum - _minimum + _largeChange;
        var ratio = (double)_largeChange / range;
        return Math.Max(ScaleForDpi(MinThumbHeight), (int)(TrackHeight() * ratio));
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

    private int ThumbVisualLeft() => ScaleForDpi(ThumbLeftInset);

    private int ArrowVisualLeft() => ScaleForDpi(ArrowLeftInset);

    private int VisualWidth() => ScaleForDpi(TrackWidth);

    private Rectangle ThumbBounds()
    {
        var t = ThumbTop();
        return new Rectangle(ThumbVisualLeft(), t, VisualWidth(), ThumbHeight());
    }

    private Rectangle ThumbDragBounds()
    {
        var t = ThumbTop();
        return new Rectangle(0, t, ClientSize.Width, ThumbHeight());
    }

    private Rectangle UpArrowBounds()
        => new(ArrowVisualLeft(), ScaleForDpi(ArrowTopSpacing), VisualWidth(), ArrowHeight());

    private Rectangle DownArrowBounds()
        => new(ArrowVisualLeft(), ClientSize.Height - ArrowHeight() - ScaleForDpi(ArrowBottomSpacing), VisualWidth(), ArrowHeight());

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
            var upColor = _arrowDirection == -1 ? ThumbActive : ThumbIdle;
            using var upBrush = new SolidBrush(upColor);
            DrawArrowChar(e.Graphics, _arrowFont, upBrush, UpArrowBounds(), SystemIconProvider.ScrollUpArrow);
        }

        if (canScroll && showParts)
        {
            var color = _dragging || _thumbHovered ? ThumbActive : ThumbIdle;
            using var brush = new SolidBrush(color);
            using var path = CreateRoundedRect(ThumbBounds(), ScaleForDpi(ThumbRadius));
            e.Graphics.FillPath(brush, path);
        }

        if (canScroll && showParts)
        {
            var downColor = _arrowDirection == 1 ? ThumbActive : ThumbIdle;
            using var downBrush = new SolidBrush(downColor);
            DrawArrowChar(e.Graphics, _arrowFont, downBrush, DownArrowBounds(), SystemIconProvider.ScrollDownArrow);
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

        if (UpArrowBounds().Contains(e.Location))
        {
            StartArrowScroll(-1);
            return;
        }

        if (DownArrowBounds().Contains(e.Location))
        {
            StartArrowScroll(1);
            return;
        }

        var trackBounds = TrackBounds();
        if (trackBounds.Contains(e.Location))
        {
            if (e.Y < thumbBounds.Top)
            {
                Value = _value - _largeChange;
                RaiseScroll(ScrollEventType.LargeDecrement);
            }
            else
            {
                Value = _value + _largeChange;
                RaiseScroll(ScrollEventType.LargeIncrement);
            }
        }
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
        StopArrowScroll();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _arrowTimer.Dispose();
            _arrowFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void StartArrowScroll(int direction)
    {
        _arrowDirection = direction;
        _arrowInitialDelay = true;
        _arrowTimer.Interval = 400;
        Value = _value + direction * _smallChange;
        RaiseScroll(ScrollEventType.SmallDecrement);
        _arrowTimer.Start();
    }

    private void StopArrowScroll()
    {
        _arrowDirection = 0;
        _arrowTimer.Stop();
        Invalidate();
    }

    private void OnArrowTimerTick(object? sender, EventArgs e)
    {
        if (_arrowDirection == 0)
        {
            _arrowTimer.Stop();
            return;
        }

        if (_arrowInitialDelay)
        {
            _arrowInitialDelay = false;
            _arrowTimer.Interval = 50;
        }

        Value = _value + _arrowDirection * _smallChange;
        RaiseScroll(ScrollEventType.SmallDecrement);
    }

    private static void DrawArrowChar(Graphics g, Font font, Brush brush, Rectangle bounds, string text)
    {
        using var path = new GraphicsPath();
        path.AddString(
            text,
            font.FontFamily,
            (int)font.Style,
            g.DpiY * font.SizeInPoints / 72f,
            bounds,
            StringFormat.GenericDefault);
        g.FillPath(brush, path);
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
