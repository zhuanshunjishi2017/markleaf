namespace MarkLeaf.Commands;

public static class CommandCatalog
{
    /// <summary>
    /// 排版样式菜单项的命令 ID 起始值。样式随 Resources/Styles 目录动态发现，
    /// 因此不使用枚举值，而是从该基数开始按序分配，再由 NativeMenuService 映射回样式 ID。
    /// </summary>
    public const int StyleCommandBase = 0x1230;

    public const int StyleCommandMax = 0x12FF;

    /// <summary>
    /// “设置缩放”菜单项的命令 ID 起始值。缩放百分比选项来自
    /// AppearanceSettings.ZoomPercentOptions，同样在运行时按序分配。
    /// </summary>
    public const int ZoomCommandBase = 0x1600;

    public const int ZoomCommandMax = 0x16FF;

    /// <summary>
    /// “颜色主题”菜单项的命令 ID 起始值。颜色主题根据 Resources/Styles 目录下
    /// 标记为 @type: color-theme 的 CSS 文件动态发现，在运行时按序分配。
    /// </summary>
    public const int ColorCommandBase = 0x1700;

    public const int ColorCommandMax = 0x17FF;

    public const int DocumentTabCommandBase = 0x1800;

    public const int DocumentTabCommandMax = 0x18FF;

    public static bool TryGetById(int commandId, out AppCommand command)
    {
        command = (AppCommand)commandId;
        return Enum.IsDefined(command);
    }
}
