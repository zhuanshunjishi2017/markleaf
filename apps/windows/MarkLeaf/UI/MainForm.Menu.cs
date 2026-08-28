using System.Runtime.InteropServices;
using MarkLeaf.Native;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private void OverpaintMenuSeparator()
    {
        try
        {
            MenuBarDarkMode.GetWindowRect(Handle, out var rcWindow);
            MenuBarDarkMode.GetClientRect(Handle, out var rcClient);
            var clientTopLeft = new System.Drawing.Point(0, 0);
            MenuBarDarkMode.ClientToScreen(Handle, ref clientTopLeft);

            var clientTop = clientTopLeft.Y - rcWindow.Top;
            var stripLeft = clientTopLeft.X - rcWindow.Left;
            var stripWidth = rcClient.Right - rcClient.Left;

            var dc = MenuBarDarkMode.GetWindowDC(Handle);
            if (dc == 0) return;
            try
            {
                using var g = Graphics.FromHdc(dc);
                g.FillRectangle(_menuHighlightBrush, stripLeft, clientTop - 1, stripWidth, 1);
            }
            finally
            {
                MenuBarDarkMode.ReleaseDC(Handle, dc);
            }
        }
        catch (ArgumentException) { }
    }

    private void DrawMenuBarBackground(ref Message m)
    {
        try
        {
            var um = Marshal.PtrToStructure<MenuBarDarkMode.UahMenu>(m.LParam);
            if (um.Hdc == 0) return;

            var mbi = new MenuBarDarkMode.MenuBarInfo { CbSize = Marshal.SizeOf<MenuBarDarkMode.MenuBarInfo>() };
            if (!MenuBarDarkMode.GetMenuBarInfo(Handle, MenuBarDarkMode.ObjidMenuId, 0, ref mbi))
                return;

            MenuBarDarkMode.GetWindowRect(Handle, out var rcWindow);
            var rc = mbi.RcBar;
            rc.Left -= rcWindow.Left;
            rc.Top -= rcWindow.Top;
            rc.Right -= rcWindow.Left;
            rc.Bottom -= rcWindow.Top;

            using var g = Graphics.FromHdc(um.Hdc);
            g.FillRectangle(_menuBgBrush, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            m.Result = 0;
        }
        catch (ArgumentException) { }
    }

    private void DrawDarkMenuItem(ref Message m)
    {
        try
        {
            var di = Marshal.PtrToStructure<MenuBarDarkMode.UahDrawMenuItem>(m.LParam);
            var rc = di.Dis.RcItem;
            var state = di.Dis.ItemState;

            var isSelected = (state & MenuBarDarkMode.OdsSelected) != 0;
            var isHot = (state & MenuBarDarkMode.OdsHotLight) != 0;
            var isDisabled = (state & (MenuBarDarkMode.OdsGrayed | MenuBarDarkMode.OdsDisabled)) != 0;

            using var g = Graphics.FromHdc(di.Um.Hdc);

            g.FillRectangle(
                isSelected || isHot ? _menuHighlightBrush : _menuBgBrush,
                rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);

            var itemText = GetMenuItemText(di.Um.HMenu, di.Umi.IPosition);
            if (string.IsNullOrEmpty(itemText))
            {
                m.Result = 0;
                return;
            }

            var color = isDisabled ? ((SolidBrush)_menuDisabledBrush).Color
                                   : ((SolidBrush)_menuTextBrush).Color;
            var textRect = new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            var prefixFlags = (state & MenuBarDarkMode.OdsNoAccel) != 0
                ? TextFormatFlags.HidePrefix
                : TextFormatFlags.Default;

            TextRenderer.DrawText(g, itemText, SystemFonts.MenuFont!, textRect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | prefixFlags);
            m.Result = 0;
        }
        catch (ArgumentException) { }
    }

    private static string GetMenuItemText(nint hMenu, int position)
    {
        if (hMenu == 0) return "";

        var info = new MenuItemInfoW();
        info.cbSize = (uint)Marshal.SizeOf<MenuItemInfoW>();
        info.fMask = MiiString;
        info.dwTypeData = nint.Zero;
        info.cch = 0;

        if (!GetMenuItemInfoW(hMenu, (uint)position, true, ref info))
            return "";

        info.cch += 1;
        info.dwTypeData = Marshal.AllocHGlobal((int)(info.cch * 2));

        try
        {
            if (!GetMenuItemInfoW(hMenu, (uint)position, true, ref info))
                return "";

            return Marshal.PtrToStringUni(info.dwTypeData, (int)info.cch) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(info.dwTypeData);
        }
    }

    private const uint MiiString = 0x00000040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfoW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nint dwItemData;
        public nint dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMenuItemInfoW(nint hMenu, uint uItem, bool fByPosition, ref MenuItemInfoW lpmii);
}
