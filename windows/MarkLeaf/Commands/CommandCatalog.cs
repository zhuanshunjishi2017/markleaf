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

    private static readonly IReadOnlyDictionary<Keys, AppCommand> Shortcuts =
        new Dictionary<Keys, AppCommand>
        {
            [Keys.Control | Keys.N] = AppCommand.NewDocument,
            [Keys.Control | Keys.O] = AppCommand.OpenDocument,
            [Keys.Control | Keys.S] = AppCommand.SaveDocument,
            [Keys.Control | Keys.Shift | Keys.S] = AppCommand.SaveDocumentAs,
            [Keys.Control | Keys.Z] = AppCommand.Undo,
            [Keys.Control | Keys.Y] = AppCommand.Redo,
            [Keys.Control | Keys.X] = AppCommand.Cut,
            [Keys.Control | Keys.C] = AppCommand.Copy,
            [Keys.Control | Keys.V] = AppCommand.Paste,
            [Keys.Control | Keys.F] = AppCommand.Find,
            [Keys.Control | Keys.H] = AppCommand.Replace,
            [Keys.Control | Keys.B] = AppCommand.ToggleBold,
            [Keys.Control | Keys.I] = AppCommand.ToggleItalic,
            [Keys.Control | Keys.Shift | Keys.C] = AppCommand.FormatPainter,
            [Keys.Control | Keys.K] = AppCommand.InsertLink,
            [Keys.Control | Keys.D1] = AppCommand.SetHeading1,
            [Keys.Control | Keys.D2] = AppCommand.SetHeading2,
            [Keys.Control | Keys.D3] = AppCommand.SetHeading3,
            [Keys.Control | Keys.D4] = AppCommand.SetHeading4,
            [Keys.Control | Keys.D5] = AppCommand.SetHeading5,
            [Keys.Control | Keys.D6] = AppCommand.SetHeading6,
            [Keys.F11] = AppCommand.ToggleFocusMode,
        };

    public static IReadOnlyDictionary<Keys, AppCommand> ShortcutMap => Shortcuts;

    public static bool TryGetById(int commandId, out AppCommand command)
    {
        command = (AppCommand)commandId;
        return Enum.IsDefined(command);
    }

    public static bool TryGetByShortcut(Keys keyData, out AppCommand command)
    {
        var normalized = keyData & (Keys.KeyCode | Keys.Modifiers);
        return Shortcuts.TryGetValue(normalized, out command);
    }
}
