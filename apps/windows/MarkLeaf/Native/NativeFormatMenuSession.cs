using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MarkLeaf.Commands;
using MarkLeaf.UI;

namespace MarkLeaf.Native;

/// <summary>Draws one native context menu with Fluent metrics and hosts the quick-action row.</summary>
internal sealed class NativeFormatMenuSession : IDisposable
{
    internal const uint ItemId = 0x7F01;
    private readonly nint _menu;
    private readonly nint _owner;
    private readonly Action<string> _diagnostic;
    private readonly NativeFormatToolbar? _toolbar;
    private readonly ModernMenuBuilder? _ownerDraw;
    private readonly ModernMenuTheme _theme;
    private readonly ModernMenuMetrics _metrics;
    private readonly NativeMenuService.HookProc _hookProc;
    private readonly nint _hook;
    private bool _popupStyled;
    private bool _rowSelected;
    private nint _popupWindow;
    private AppCommand? _pressedCommand;
    internal AppCommand? SelectedCommand { get; private set; }

    internal NativeFormatMenuSession(nint owner, nint menu, int dpi, bool dark,
        Func<AppCommand, CommandState> state, Action<string?> preview, Action<string> diagnostic,
        ModernMenuBuilder? ownerDraw = null, bool quickActions = false)
    {
        _owner = owner;
        _menu = menu;
        _diagnostic = diagnostic;
        _ownerDraw = ownerDraw;
        _theme = ModernMenuTheme.Create(dark);
        _metrics = ModernMenuMetrics.ForDpi(dpi);
        _toolbar = quickActions ? new NativeFormatToolbar(dpi, state, preview) : null;
        _hookProc = FilterMenuMessage;

        if (_toolbar is not null
            && (!InsertMenu(menu, 0, NativeMethods.MfByPosition | NativeMethods.MfSeparator, 0, null)
                || !InsertMenu(menu, 0, NativeMethods.MfByPosition | 0x0100 /* MF_OWNERDRAW */, ItemId, null)))
        {
            throw new Win32Exception();
        }

        _hook = NativeMethods.SetWindowsHookEx(-1 /* WH_MSGFILTER */,
            Marshal.GetFunctionPointerForDelegate(_hookProc), 0, NativeMethods.GetCurrentThreadId());
        if (_hook == 0) throw new Win32Exception();
    }

    internal bool HandleMessage(ref Message message)
    {
        if (message.Msg == 0x002C /* WM_MEASUREITEM */)
        {
            var item = Marshal.PtrToStructure<MenuBarDarkMode.MeasureItemStruct>(message.LParam);
            if (item.CtlType != 1) return false;
            if (item.ItemId == ItemId && _toolbar is not null)
            {
                item.ItemWidth = _toolbar.Size.Width;
                item.ItemHeight = _toolbar.Size.Height;
            }
            else if (_ownerDraw is null || !_ownerDraw.TryGetItem(item.ItemData, out var menuItem))
            {
                return false;
            }
            else
            {
                using var font = CreateTextFont();
                item.ItemWidth = Math.Max(_metrics.MinimumWidth, MeasureItemWidth(menuItem, font));
                item.ItemHeight = menuItem.Kind == ModernMenuKind.Separator
                    ? _metrics.SeparatorHeight
                    : _metrics.ItemHeight;
            }

            Marshal.StructureToPtr(item, message.LParam, false);
            message.Result = 1;
            return true;
        }

        if (message.Msg == 0x002B /* WM_DRAWITEM */)
        {
            var item = Marshal.PtrToStructure<MenuBarDarkMode.DrawItemStruct>(message.LParam);
            if (item.CtlType != 1) return false;
            if (item.ItemId == ItemId && _toolbar is not null)
            {
                if (item.HwndItem != _menu) return false;
            }
            else if (_ownerDraw is null || !_ownerDraw.TryGetItem(item.ItemData, out _))
            {
                return false;
            }

            Draw(item);
            message.Result = 1;
            return true;
        }

        if (message.Msg == 0x011F /* WM_MENUSELECT */)
        {
            _rowSelected = _toolbar is not null
                && message.LParam == _menu
                && (message.WParam.ToInt64() & 0xffff) == ItemId;
            if (!_rowSelected && _toolbar?.ClearHover() == true) RedrawRow();
        }
        return false;
    }

    private nint FilterMenuMessage(int code, nint wParam, nint lParam)
    {
        if (code == 2 /* MSGF_MENU */)
        {
            try
            {
                var message = Marshal.PtrToStructure<MenuMessage>(lParam);
                FindPopupWindow();
                if (_popupWindow != 0 && !_popupStyled) StylePopupWindow(_popupWindow);

                if (_toolbar is not null && GetMenuItemRect(_owner, _menu, 0, out var rect))
                {
                    var row = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    var point = new Point(message.X, message.Y);
                    if (message.Message == 0x0200 /* WM_MOUSEMOVE */)
                    {
                        if (_toolbar.Hover(point, row)) RedrawRow();
                    }
                    else if (message.Message == 0x0201 /* WM_LBUTTONDOWN */ && row.Contains(point))
                    {
                        _pressedCommand = _toolbar.CommandAt(point, row);
                        return 1;
                    }
                    else if (message.Message == 0x0202 /* WM_LBUTTONUP */ && row.Contains(point))
                    {
                        var command = _toolbar.CommandAt(point, row);
                        if (command.HasValue && command == _pressedCommand) Select(command.Value);
                        _pressedCommand = null;
                        return 1;
                    }
                    else if (message.Message == 0x0100 /* WM_KEYDOWN */ && _rowSelected)
                    {
                        var key = (Keys)message.WParam;
                        if (key is Keys.Left or Keys.Right)
                        {
                            if (_toolbar.MoveFocus(key == Keys.Left ? -1 : 1)) RedrawRow();
                            return 1;
                        }
                        if (key is Keys.Enter or Keys.Space)
                        {
                            if (_toolbar.HotIndex < 0) _toolbar.MoveFocus(1);
                            if (_toolbar.HotIndex >= 0)
                            {
                                Select(NativeFormatToolbar.Commands[_toolbar.HotIndex]);
                                return 1;
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                // Managed exceptions must never unwind through a native hook callback.
                _diagnostic($"Modern menu event failed: {error}");
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private void Select(AppCommand command)
    {
        SelectedCommand = command;
        _toolbar?.ClearHover();
        NativeMethods.EndMenu();
    }

    private void RedrawRow()
    {
        FindPopupWindow();
        if (_popupWindow == 0 || _popupWindow == _owner) return;
        InvalidateRect(_popupWindow, 0, false);
        // TrackPopupMenu runs its own message loop. Flush the quick row before the
        // native loop replaces its highlight state.
        UpdateWindow(_popupWindow);
    }

    private void FindPopupWindow()
    {
        if (_popupWindow != 0) return;
        if (!GetMenuItemRect(_owner, _menu, 0, out var row)) return;

        // Themed menus draw into a memory DC; WindowFromDC and hit-testing the
        // desktop cannot reliably identify their popup window.
        EnumThreadWindows(NativeMethods.GetCurrentThreadId(), (window, _) =>
        {
            var name = new StringBuilder(32);
            _ = GetClassName(window, name, name.Capacity);
            if (name.ToString() == "#32768" && GetWindowRect(window, out var bounds)
                && bounds.Left <= row.Left && bounds.Top <= row.Top
                && bounds.Right >= row.Right && bounds.Bottom >= row.Bottom)
            {
                _popupWindow = window;
                return false;
            }
            return true;
        }, 0);
    }

    private void StylePopupWindow(nint window)
    {
        _popupStyled = true;
        var useDark = _theme.Background.GetBrightness() < 0.5f ? 1 : 0;
        _ = DwmSetWindowAttribute(window, 20 /* immersive dark mode */, ref useDark, sizeof(int));
        var round = 2 /* DWMWCP_ROUND */;
        _ = DwmSetWindowAttribute(window, 33 /* corner preference */, ref round, sizeof(int));
        var noBorder = unchecked((int)0xFFFFFFFE /* DWMWA_COLOR_NONE */);
        _ = DwmSetWindowAttribute(window, 34 /* border color */, ref noBorder, sizeof(int));
    }

    private Font CreateTextFont() => new(SystemIconProvider.IconFontName, 9f, FontStyle.Regular, GraphicsUnit.Point);

    private int MeasureItemWidth(ModernMenuItem item, Font font)
    {
        var textOptions = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var width = TextRenderer.MeasureText(item.Label, font, Size.Empty, textOptions).Width;
        if (!string.IsNullOrEmpty(item.Shortcut))
            width += TextRenderer.MeasureText(item.Shortcut, font, Size.Empty, textOptions).Width + _metrics.HorizontalPadding;
        if (item.Kind == ModernMenuKind.Submenu) width += _metrics.TextIndent;
        return width + _metrics.TextIndent + _metrics.HorizontalPadding * 2;
    }

    private void Draw(MenuBarDarkMode.DrawItemStruct item)
    {
        var bounds = Rectangle.FromLTRB(item.RcItem.Left, item.RcItem.Top, item.RcItem.Right, item.RcItem.Bottom);
        using var graphics = Graphics.FromHdc(item.Hdc);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        if (item.ItemId == ItemId && _toolbar is not null)
        {
            DrawQuickActions(graphics, bounds);
            return;
        }

        if (_ownerDraw?.TryGetItem(item.ItemData, out var menuItem) == true)
        {
            using var font = CreateTextFont();
            DrawMenuItem(graphics, bounds, menuItem,
                selected: (item.ItemState & (MenuBarDarkMode.OdsSelected | MenuBarDarkMode.OdsHotLight)) != 0,
                disabled: (item.ItemState & (MenuBarDarkMode.OdsDisabled | MenuBarDarkMode.OdsGrayed)) != 0,
                font);
        }
    }

    private void DrawMenuItem(Graphics graphics, Rectangle bounds, ModernMenuItem item, bool selected, bool disabled, Font font)
    {
        using (var background = new SolidBrush(_theme.Background))
        {
            graphics.FillRectangle(background, bounds);
        }

        if (item.Kind == ModernMenuKind.Separator)
        {
            var y = bounds.Top + bounds.Height / 2;
            using var separator = new Pen(_theme.Separator, 1);
            graphics.DrawLine(separator, bounds.Left + _metrics.TextIndent, y, bounds.Right - _metrics.TextIndent, y);
            return;
        }

        if (selected)
        {
            using var path = RoundedBounds(_metrics.HoverBounds(bounds), _metrics.CornerRadius);
            using var hover = new SolidBrush(_theme.Hover);
            graphics.FillPath(hover, path);
        }

        var textColor = disabled ? _theme.DisabledText : _theme.Text;
        var textBounds = bounds with { X = bounds.Left + _metrics.TextIndent, Width = bounds.Width - _metrics.TextIndent - _metrics.HorizontalPadding * 2 };
        TextRenderer.DrawText(graphics, item.Label, font, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var right = bounds.Right - _metrics.HorizontalPadding * 2;
        if (item.Kind == ModernMenuKind.Submenu)
        {
            using var arrow = new Pen(_theme.SecondaryText, 1.6f);
            arrow.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            arrow.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            arrow.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
            var y = bounds.Top + bounds.Height / 2;
            var x = right - 4;
            graphics.DrawLines(arrow, [new Point(x - 3, y - 4), new Point(x + 1, y), new Point(x - 3, y + 4)]);
        }
        else if (!string.IsNullOrEmpty(item.Shortcut))
        {
            var shortcutBounds = bounds with
            {
                Width = _metrics.TextIndent * 2,
                X = bounds.Right - _metrics.TextIndent * 2 - _metrics.HorizontalPadding * 2
            };
            TextRenderer.DrawText(graphics, item.Shortcut, font, shortcutBounds, _theme.SecondaryText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        if (item.State.IsChecked && item.Command is not null)
        {
            var size = Math.Max(10, _metrics.ItemHeight / 3);
            var checkBounds = new Rectangle(bounds.Left + _metrics.HorizontalPadding,
                bounds.Top + (bounds.Height - size) / 2, size, size);
            using var path = RoundedBounds(checkBounds, size / 3);
            using var brush = new SolidBrush(disabled ? _theme.DisabledText : _theme.Accent);
            graphics.FillPath(brush, path);
            using var markPen = new Pen(Color.White, 1.8f);
            markPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
            markPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
            markPen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
            graphics.DrawLines(markPen,
            [
                new((int)Math.Round(checkBounds.Left + checkBounds.Width * 0.25f), (int)Math.Round(checkBounds.Top + checkBounds.Height * 0.55f)),
                new((int)Math.Round(checkBounds.Left + checkBounds.Width * 0.45f), (int)Math.Round(checkBounds.Bottom - checkBounds.Height * 0.25f)),
                new((int)Math.Round(checkBounds.Left + checkBounds.Width * 0.78f), (int)Math.Round(checkBounds.Top + checkBounds.Height * 0.25f))
            ]);
        }
    }

    private void DrawQuickActions(Graphics graphics, Rectangle row)
    {
        if (_toolbar is null) return;
        using (var background = new SolidBrush(_theme.Background)) graphics.FillRectangle(background, row);
        using var font = new Font(
            SystemIconProvider.IconFontName,
            Math.Max(12f, 14f * _metrics.ToolbarButtonSize / 32f),
            FontStyle.Regular,
            GraphicsUnit.Pixel);

        for (var index = 0; index < NativeFormatToolbar.Commands.Length; index++)
        {
            var bounds = _toolbar.ButtonBounds(row, index);
            var state = _toolbar.States[index];
            var hot = index == _toolbar.HotIndex;
            if (hot || state.IsChecked)
            {
                using var path = RoundedBounds(bounds, _metrics.CornerRadius);
                using var fill = new SolidBrush(
                    hot ? _theme.Hover : Color.FromArgb(48, _theme.Accent));
                graphics.FillPath(fill, path);
            }

            var color = !state.IsEnabled ? _theme.DisabledText
                : state.IsChecked ? _theme.Accent
                : _theme.Text;
            TextRenderer.DrawText(graphics, NativeFormatToolbar.Glyphs[index], font, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedBounds(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        NativeMethods.UnhookWindowsHookEx(_hook);
        _toolbar?.ClearHover();
        GC.KeepAlive(_hookProc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "InsertMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenu(nint menu, uint position, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMenuItemRect(nint owner, nint menu, uint item, out MenuBarDarkMode.Rect rect);

    private delegate bool EnumThreadWindowProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowProc callback, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out MenuBarDarkMode.Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(nint window, nint rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateWindow(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
