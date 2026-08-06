using System.Runtime.InteropServices;

namespace MarkLeaf.Native;

internal static class DarkModeService
{
    private const int PreferredAppModeDefault = 0;
    private const int PreferredAppModeAllowDark = 1;
    private const int PreferredAppModeForceDark = 2;
    private const int PreferredAppModeForceLight = 3;

    private static bool _available;
    private static bool _initialized;

    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int preferredAppMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", EntryPoint = "#104", SetLastError = true)]
    private static extern void RefreshImmersiveColorPolicyState();

    /// <summary>
    /// 在创建任何窗口之前调用，设置进程级初始颜色模式。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        if (Environment.OSVersion.Version.Major < 10) return;

        try
        {
            // 初始设置为强制浅色（与默认白色主题一致），后续由 SetColorTheme 切换。
            SetPreferredAppMode(PreferredAppModeForceLight);
            FlushMenuThemes();
            _available = true;
        }
        catch (EntryPointNotFoundException)
        {
            // uxtheme.dll 缺少对应序数，Windows 版本过旧。
        }
    }

    public static void Apply(bool dark)
    {
        if (!_available) return;

        try
        {
            SetPreferredAppMode(dark ? PreferredAppModeForceDark : PreferredAppModeForceLight);
            FlushMenuThemes();
            RefreshImmersiveColorPolicyState();
        }
        catch (EntryPointNotFoundException)
        {
            _available = false;
        }
    }
}
