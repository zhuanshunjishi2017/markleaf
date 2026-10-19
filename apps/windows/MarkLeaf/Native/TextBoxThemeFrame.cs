using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MarkLeaf.Services.Styles;

namespace MarkLeaf.Native;

/// <summary>
/// Theme the native edit frame only. The native client, metrics, scrollbars,
/// caret, IME, selection and accessibility remain owned by the standard TextBox.
/// </summary>
internal sealed class TextBoxThemeFrame : NativeWindow
{
    private static readonly ConditionalWeakTable<TextBox, TextBoxThemeFrame> Frames = new();
    private readonly TextBox _input;

    private TextBoxThemeFrame(TextBox input)
    {
        _input = input;
        input.HandleCreated += (_, _) => AssignHandle(input.Handle);
        input.HandleDestroyed += (_, _) => ReleaseHandle();
        input.MouseEnter += (_, _) => DrawFrame();
        input.MouseLeave += (_, _) => DrawFrame();
        if (input.IsHandleCreated) AssignHandle(input.Handle);
    }

    internal static void Apply(TextBox input)
    {
        var frame = Frames.GetValue(input, key => new(key));
        // Let the native control restore its own frame on light/high-contrast
        // themes too, rather than leaving the last dark border on screen.
        if (frame.Handle != 0) RedrawWindow(frame.Handle, 0, 0, 0x0501); // INVALIDATE | FRAME | UPDATENOW
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Native EDIT can repaint its frame during client painting and focus
        // changes as well as WM_NCPAINT. Repaint only the existing border area.
        if (m.Msg is 0x0085 or 0x000F or 0x0007 or 0x0008 or 0x000A or 0x0005 or 0x031A)
            DrawFrame();
    }

    private void DrawFrame()
    {
        if (Handle == 0 || _input.IsDisposed || SystemInformation.HighContrast
            || _input.BorderStyle == BorderStyle.None || _input.BackColor.GetBrightness() >= .5f) return;
        if (!GetWindowRect(Handle, out var window)) return;
        var client = _input.RectangleToScreen(_input.ClientRectangle);
        // One side can include a native scrollbar (including RTL left-side
        // scrollbars). Use the smaller inset, never the scrollbar's width.
        var borderX = Math.Max(0, Math.Min(client.Left - window.Left, window.Right - client.Right));
        var borderY = Math.Max(0, Math.Min(client.Top - window.Top, window.Bottom - client.Bottom));
        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;
        if (borderX == 0 || borderY == 0 || width <= 0 || height <= 0) return;

        var colors = ColorThemeService.GetActiveColors();
        var hover = _input.ClientRectangle.Contains(_input.PointToClient(Cursor.Position));
        var key = !_input.Enabled ? "bg-selected" : _input.Focused ? "theme-light"
            : hover ? "bg-selected-hover" : "bg-selected";
        var color = colors.GetValueOrDefault(key, _input.Focused ? SystemColors.Highlight : Color.DimGray);
        var dc = GetWindowDC(Handle);
        if (dc == 0) return;
        try
        {
            using var graphics = Graphics.FromHdc(dc);
            using var background = new SolidBrush(_input.BackColor);
            // Clear the bevel, without painting over the client or scrollbar.
            graphics.FillRectangle(background, 0, 0, width, borderY);
            graphics.FillRectangle(background, 0, height - borderY, width, borderY);
            graphics.FillRectangle(background, 0, borderY, borderX, height - borderY * 2);
            graphics.FillRectangle(background, width - borderX, borderY, borderX, height - borderY * 2);
            using var pen = new Pen(color);
            graphics.DrawRectangle(pen, 0, 0, width - 1, height - 1);
        }
        finally { ReleaseDC(Handle, dc); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowRect rect);
    [DllImport("user32.dll")] private static extern nint GetWindowDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint window, nint updateRect, nint updateRegion, uint flags);
}
