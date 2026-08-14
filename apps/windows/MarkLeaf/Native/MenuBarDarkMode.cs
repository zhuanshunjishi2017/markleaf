using System.Runtime.InteropServices;

namespace MarkLeaf.Native;

internal static class MenuBarDarkMode
{
    public const int WmNcPaint = 0x0085;
    public const int WmUahDrawMenu = 0x0091;
    public const int WmUahDrawMenuItem = 0x0092;
    public const int WmUahMeasureMenuItem = 0x0094;

    // DRAWITEMSTRUCT itemState flags
    public const int OdsSelected = 0x0001;
    public const int OdsGrayed = 0x0002;
    public const int OdsDisabled = 0x0004;
    public const int OdsHotLight = 0x0040;
    public const int OdsInactive = 0x0080;
    public const int OdsNoAccel = 0x0100;
    public const int OdsNoFocusRect = 0x0200;

    private const int ObjidMenu = unchecked((int)0xFFFFFFFD);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DrawItemStruct
    {
        public int CtlType;
        public int CtlId;
        public int ItemId;
        public int ItemAction;
        public int ItemState;
        public nint HwndItem;
        public nint Hdc;
        public Rect RcItem;
        public nint ItemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UahMenu
    {
        public nint HMenu;
        public nint Hdc;
        public uint DwFlags; // padding / format flags
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UahMenuMetrics
    {
        public uint Cx;
        public uint Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UahMenuItem
    {
        public int IPosition;
        public UahMenuMetrics Umim;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UahDrawMenuItem
    {
        public DrawItemStruct Dis;
        public UahMenu Um;
        public UahMenuItem Umi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MeasureItemStruct
    {
        public int CtlType;
        public int CtlId;
        public int ItemId;
        public int ItemWidth;
        public int ItemHeight;
        public nint ItemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UahMeasureMenuItem
    {
        public MeasureItemStruct Mis;
        public UahMenu Um;
        public UahMenuItem Umi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MenuBarInfo
    {
        public int CbSize;
        public Rect RcBar;
        public nint HMenu;
        public nint HwndMenu;
        public bool BarFocused;
        public bool Focused;
    }

    [DllImport("user32.dll")]
    public static extern bool GetMenuBarInfo(nint hwnd, int idObject, int idItem, ref MenuBarInfo pmbi);

    [DllImport("user32.dll")]
    public static extern int DrawTextW(nint hdc, string lpchText, int cchText, ref Rect lprc, uint format);

    [DllImport("user32.dll")]
    public static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hdc);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(nint hWnd, ref System.Drawing.Point lpPoint);

    [DllImport("gdi32.dll")]
    public static extern int SetTextColor(nint hdc, int crColor);

    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(nint hdc, int iBkMode);

    [DllImport("gdi32.dll")]
    public static extern nint SelectObject(nint hdc, nint hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(nint hObject);

    [DllImport("user32.dll")]
    public static extern int GetWindowRect(nint hwnd, out Rect lpRect);

    public static int ObjidMenuId => ObjidMenu;

    public const uint DtCenter = 0x00000001;
    public const uint DtVcenter = 0x00000004;
    public const uint DtSingleLine = 0x00000020;
    public const uint DtHidePrefix = 0x00100000;
}
