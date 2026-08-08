using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace MarkLeaf.Services;

/// <summary>
/// 在当前用户（HKCU）下维护 MarkLeaf 与文件扩展名的关联，
/// 使资源管理器显示 MarkLeaf 图标并可双击打开，无需管理员权限。
/// 同步模式：传入当前启用的扩展名集合，未启用的映射会被移除。
/// </summary>
internal static class FileAssociationService
{
    private const string ProgId = "MarkLeaf.MarkdownDoc";

    public static readonly IReadOnlyList<string> AllExtensions = [".md", ".markdown", ".txt"];

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);

    /// <summary>
    /// 将关联状态同步到注册表：仅把 MarkLeaf 写入各扩展的“打开方式”列表
    /// （OpenWithProgids），不设置扩展的默认程序；启用集合为空时删除 ProgID。
    /// </summary>
    public static void ApplyFileAssociations(string executablePath, IReadOnlySet<string> enabledExtensions)
    {
        var exePath = Path.GetFullPath(executablePath);

        foreach (var extension in AllExtensions)
        {
            // 清理旧版本曾把 MarkLeaf 设为默认程序时写入的扩展名默认值映射。
            using (var extKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{extension}", writable: true))
            {
                if (extKey?.GetValue("") as string == ProgId)
                {
                    extKey.DeleteValue("", throwOnMissingValue: false);
                }
            }

            // 扩展名键可能已存在并含其他程序的值，只增删我们自己的 OpenWithProgids 项。
            using var openWithKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            if (enabledExtensions.Contains(extension))
            {
                openWithKey.SetValue(ProgId, "");
            }
            else
            {
                openWithKey.DeleteValue(ProgId, throwOnMissingValue: false);
            }
        }

        if (enabledExtensions.Count == 0)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            NotifyShell();
            return;
        }

        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            progIdKey.SetValue("", Loc.Get("fileAssociation.description"));
        }

        using (var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
        {
            // exe 内嵌的应用程序图标（csproj ApplicationIcon），索引 0。
            iconKey.SetValue("", $"\"{exePath}\",0");
        }

        using (var openKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
        {
            openKey.SetValue("", $"\"{exePath}\" --open-document \"%1\"");
        }

        NotifyShell();
    }

    /// <summary>
    /// 是否允许在注册表操作失败时继续；用于调用方的异常过滤。
    /// </summary>
    public static bool IsExpectedRegistryException(Exception exception)
    {
        return exception is UnauthorizedAccessException or SecurityException or IOException;
    }

    private static void NotifyShell()
    {
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, 0, 0);
    }
}
