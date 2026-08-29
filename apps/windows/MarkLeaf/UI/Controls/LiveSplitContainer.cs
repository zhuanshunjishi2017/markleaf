namespace MarkLeaf.UI.Controls;

internal sealed class LiveSplitContainer : SplitContainer
{
    private const int SplitterHitPadding = 5;

    private readonly Dictionary<Control, Cursor> _originalChildCursors = [];
    private bool _draggingSplitter;
    private bool _liveDraggingEnabled = true;

    public event EventHandler? LiveSplitterMoved;

    public event EventHandler? LiveSplitterDragCompleted;

    public LiveSplitContainer()
    {
        // Disable WinForms' reversible preview line. This control moves the real
        // splitter directly so the drag indicator and panel edge remain identical.
        IsSplitterFixed = true;
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (TryBeginSplitterDrag(eventArgs.Button, eventArgs.Location))
        {
            return;
        }

        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        if (_draggingSplitter)
        {
            MoveSplitterToPointer(eventArgs.Location);
            return;
        }

        Cursor = _liveDraggingEnabled && SplitterHitRectangle().Contains(eventArgs.Location)
            ? Orientation == Orientation.Vertical ? Cursors.VSplit : Cursors.HSplit
            : Cursors.Default;
        base.OnMouseMove(eventArgs);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        HookMouseMove(e.Control);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        if (_draggingSplitter && eventArgs.Button == MouseButtons.Left)
        {
            MoveSplitterToPointer(eventArgs.Location);
            _draggingSplitter = false;
            Capture = false;
            LiveSplitterDragCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnMouseUp(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        if (!_draggingSplitter)
        {
            Cursor = Cursors.Default;
        }
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseCaptureChanged(EventArgs eventArgs)
    {
        base.OnMouseCaptureChanged(eventArgs);
        if (_draggingSplitter && !Capture)
        {
            _draggingSplitter = false;
            LiveSplitterDragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void SetLiveDraggingEnabled(bool enabled)
    {
        _liveDraggingEnabled = enabled;
        if (!enabled && _draggingSplitter)
        {
            _draggingSplitter = false;
            Capture = false;
        }
    }

    private void MoveSplitterToPointer(Point pointer)
    {
        var minimum = Panel1MinSize;
        var available = Orientation == Orientation.Vertical ? ClientSize.Width : ClientSize.Height;
        var maximum = Math.Max(
            minimum,
            available - Panel2MinSize - SplitterWidth);
        var pointerPosition = Orientation == Orientation.Vertical ? pointer.X : pointer.Y;
        var distance = Math.Clamp(pointerPosition, minimum, maximum);
        if (SplitterDistance == distance)
        {
            return;
        }

        SplitterDistance = distance;
        PerformLayout();
        Update();
        LiveSplitterMoved?.Invoke(this, EventArgs.Empty);
    }

    private Rectangle SplitterHitRectangle()
    {
        var splitter = SplitterRectangle;
        return Orientation == Orientation.Vertical
            ? new Rectangle(splitter.Left - SplitterHitPadding, splitter.Top,
                splitter.Width + SplitterHitPadding * 2, splitter.Height)
            : new Rectangle(splitter.Left, splitter.Top - SplitterHitPadding,
                splitter.Width, splitter.Height + SplitterHitPadding * 2);
    }

    private void HookMouseMove(Control? control)
    {
        if (control is null)
            return;

        _originalChildCursors.TryAdd(control, control.Cursor);
        control.MouseMove -= ChildControl_MouseMove;
        control.MouseMove += ChildControl_MouseMove;
        control.MouseLeave -= ChildControl_MouseLeave;
        control.MouseLeave += ChildControl_MouseLeave;
        control.MouseDown -= ChildControl_MouseDown;
        control.MouseDown += ChildControl_MouseDown;
        foreach (Control child in control.Controls)
            HookMouseMove(child);
        control.ControlAdded -= ChildControlAdded;
        control.ControlAdded += ChildControlAdded;
    }

    private void ChildControlAdded(object? sender, ControlEventArgs e)
    {
        HookMouseMove(e.Control);
    }

    private void ChildControl_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_draggingSplitter)
            return;

        var point = PointToClient(Cursor.Position);
        if (sender is Control control)
        {
            control.Cursor = _liveDraggingEnabled && SplitterHitRectangle().Contains(point)
                ? Orientation == Orientation.Vertical ? Cursors.VSplit : Cursors.HSplit
                : _originalChildCursors.GetValueOrDefault(control, Cursors.Default);
        }
    }

    private void ChildControl_MouseLeave(object? sender, EventArgs e)
    {
        if (!_draggingSplitter && sender is Control control
            && _originalChildCursors.TryGetValue(control, out var cursor))
        {
            control.Cursor = cursor;
        }
    }

    private void ChildControl_MouseDown(object? sender, MouseEventArgs e)
    {
        var point = PointToClient(Cursor.Position);
        if (TryBeginSplitterDrag(e.Button, point))
            return;
    }

    private bool TryBeginSplitterDrag(MouseButtons button, Point point)
    {
        if (!_liveDraggingEnabled || button != MouseButtons.Left || !SplitterHitRectangle().Contains(point))
            return false;

        _draggingSplitter = true;
        Capture = true;
        MoveSplitterToPointer(point);
        return true;
    }

}
