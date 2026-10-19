using System.Runtime.InteropServices;

namespace MarkLeaf.Native;

internal static class DarkModeService
{
    private static readonly List<WeakReference<Form>> Windows = [];
    private static bool _dark;
    private static Color? _titleBarColor;
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
            Application.Idle += (_, _) => RegisterOpenWindows();
        }
        catch (EntryPointNotFoundException)
        {
            // uxtheme.dll 缺少对应序数，Windows 版本过旧。
        }
    }

    public static void Apply(bool dark)
    {
        _dark = dark;
        if (_available)
        {
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

        // .NET 9+ 实验性 API：为 WinForms 标准控件开启深色模式。
        Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        RefreshTitleBars();
    }

    /// <summary>
    /// 为对话框及其所有子控件显式设置深色配色，
    /// 补充 Application.SetColorMode 覆盖不全的控件（ComboBox 下拉、TabControl 边框等）。
    /// </summary>
    public static void ApplyDialogDarkMode(Control root, Color bg, Color fg)
    {
        void Apply(Control c)
        {
            if (c is Form or Panel or TabPage or GroupBox or TableLayoutPanel or FlowLayoutPanel)
            {
                c.BackColor = bg;
                c.ForeColor = fg;
                foreach (Control child in c.Controls)
                    Apply(child);
                return;
            }

            if (c is Label or CheckBox or RadioButton or LinkLabel)
            {
                c.BackColor = bg;
                c.ForeColor = fg;
                return;
            }

            if (c is Button btn && btn.FlatStyle == FlatStyle.System)
                return; // 保持系统绘制按钮

            if (c is ComboBox)
                return; // 由 Application.SetColorMode 统一控制，显式 BackColor 会破坏下拉渲染

            if (c is TextBox textBox)
            {
                textBox.BackColor = bg;
                textBox.ForeColor = fg;
                TextBoxThemeFrame.Apply(textBox);
                return;
            }

            if (c is NumericUpDown)
            {
                c.BackColor = bg;
                c.ForeColor = fg;
                return;
            }

            if (c is TabControl tab)
            {
                tab.BackColor = bg;
                foreach (TabPage page in tab.TabPages)
                {
                    page.BackColor = bg;
                    page.ForeColor = fg;
                    foreach (Control child in page.Controls)
                        Apply(child);
                }
                return;
            }

            // 其他容器类控件
            if (c.HasChildren)
            {
                foreach (Control child in c.Controls)
                    Apply(child);
            }
        }

        Apply(root);
    }

    /// <summary>
    /// 设置窗口标题栏颜色，并将窗口纳入主题变化时的统一刷新范围。
    /// </summary>
    public static void SetWindowTitleBarColor(Form form, Color? color)
    {
        _titleBarColor = color;
        RegisterWindow(form);
        RefreshTitleBars();
    }

    public static void RegisterWindow(Form form)
    {
        if (!Windows.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, form)))
            Windows.Add(new WeakReference<Form>(form));
        ApplyTitleBar(form);
    }

    private static void RefreshTitleBars()
    {
        RegisterOpenWindows();

        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            if (!Windows[i].TryGetTarget(out var form) || form.IsDisposed)
                Windows.RemoveAt(i);
            else
                ApplyTitleBar(form);
        }
    }

    private static void RegisterOpenWindows()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (Windows.Any(reference => reference.TryGetTarget(out var target) && ReferenceEquals(target, form)))
                continue;
            Windows.Add(new WeakReference<Form>(form));
            ApplyTitleBar(form);
        }
    }

    private static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        var colorValue = _titleBarColor is null
            ? -1
            : _titleBarColor.Value.R | (_titleBarColor.Value.G << 8) | (_titleBarColor.Value.B << 16);
        NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DwmwaCaptionColor, ref colorValue, sizeof(int));
        var darkValue = _dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int));
        NativeMethods.SetWindowPos(form.Handle, 0, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize
            | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);
    }

    public static void SetWindowDarkTitleBar(Form form)
    {
        RegisterWindow(form);
    }
}
