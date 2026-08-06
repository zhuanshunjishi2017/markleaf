using System.Runtime.InteropServices;

namespace MarkLeaf.Native;

internal static class NativeMethods
{
    internal const uint ShgfiIcon = 0x000000100;
    internal const uint ShgfiSmallIcon = 0x000000001;
    internal const uint ShgfiLargeIcon = 0x000000000;
    internal const uint ShgfiUseFileAttributes = 0x000000010;
    internal const uint MfString = 0x0000;
    internal const uint MfEnabled = 0x0000;
    internal const uint MfGrayed = 0x0001;
    internal const uint MfUnchecked = 0x0000;
    internal const uint MfChecked = 0x0008;
    internal const uint MfPopup = 0x0010;
    internal const uint MfSeparator = 0x0800;
    internal const uint MfByCommand = 0x0000;
    internal const uint MfByPosition = 0x0400;
    internal const uint TpmRightButton = 0x0002;
    internal const uint TpmReturnCommand = 0x0100;
    internal const uint WmNull = 0x0000;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreateMenu();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(nint menu, uint flags, nuint item, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMenu(nint window, nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawMenuBar(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint EnableMenuItem(nint menu, uint item, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint CheckMenuItem(nint menu, uint item, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetMenuItemCount(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteMenu(nint menu, uint position, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint TrackPopupMenuEx(
        nint menu,
        uint flags,
        int x,
        int y,
        nint window,
        nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    // ---- 深色标题栏 ----
    public const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll", SetLastError = true)]
    public static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    // ---- 窗口框架刷新 ----
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

}
