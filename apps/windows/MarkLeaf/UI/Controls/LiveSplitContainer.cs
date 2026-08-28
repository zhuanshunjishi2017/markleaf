namespace MarkLeaf.UI.Controls;

internal sealed class LiveSplitContainer : SplitContainer
{
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
        if (_liveDraggingEnabled
            && eventArgs.Button == MouseButtons.Left
            && SplitterRectangle.Contains(eventArgs.Location))
        {
            _draggingSplitter = true;
            Capture = true;
            MoveSplitterToPointer(eventArgs.Location);
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

        Cursor = _liveDraggingEnabled && SplitterRectangle.Contains(eventArgs.Location)
            ? Orientation == Orientation.Vertical ? Cursors.VSplit : Cursors.HSplit
            : Cursors.Default;
        base.OnMouseMove(eventArgs);
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
}
