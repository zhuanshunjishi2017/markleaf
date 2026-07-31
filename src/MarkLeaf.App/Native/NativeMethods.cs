using System.Runtime.InteropServices;

namespace MarkLeaf.Native;

internal static class NativeMethods
{
    internal const uint MfString = 0x0000;
    internal const uint MfEnabled = 0x0000;
    internal const uint MfGrayed = 0x0001;
    internal const uint MfUnchecked = 0x0000;
    internal const uint MfChecked = 0x0008;
    internal const uint MfPopup = 0x0010;
    internal const uint MfSeparator = 0x0800;
    internal const uint MfByCommand = 0x0000;
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);
}
