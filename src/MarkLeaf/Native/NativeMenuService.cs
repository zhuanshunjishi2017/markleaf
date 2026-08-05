using System.ComponentModel;
using MarkLeaf.Commands;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;

namespace MarkLeaf.Native;

internal sealed class NativeMenuService : IDisposable
{
    internal static readonly AppCommand[] EditorContextCommands =
    [
        AppCommand.ToggleBold,
        AppCommand.ToggleItalic,
        AppCommand.SetParagraph,
        AppCommand.SetHeading1,
        AppCommand.SetHeading2,
        AppCommand.SetHeading3,
        AppCommand.SetHeading4,
        AppCommand.SetHeading5,
        AppCommand.SetHeading6,
        AppCommand.ToggleBulletList,
        AppCommand.ToggleOrderedList,
        AppCommand.ToggleTaskList,
        AppCommand.Cut,
        AppCommand.Copy,
        AppCommand.CopyMarkdown,
        AppCommand.CopyPlainText,
        AppCommand.Paste,
    ];

    private readonly CommandRouter _router;
    private readonly Func<IReadOnlyList<string>> _recentWorkspaceProvider;
    private readonly Func<IReadOnlyList<string>> _recentFileProvider;
    private readonly Func<string> _currentStyleProvider;
    private readonly Func<int> _currentZoomProvider;
    private readonly Dictionary<uint, string> _styleCommandIds = new();
    private readonly Dictionary<uint, int> _zoomCommandIds = new();
    private nint _menu;
    private nint _window;
    private nint _recentWorkspaceMenu;
    private nint _styleMenu;
    private nint _zoomMenu;

    public NativeMenuService(
        CommandRouter router,
        Func<IReadOnlyList<string>> recentWorkspaceProvider,
        Func<IReadOnlyList<string>> recentFileProvider,
        Func<string> currentStyleProvider,
        Func<int> currentZoomProvider)
    {
        _router = router;
        _recentWorkspaceProvider = recentWorkspaceProvider;
        _recentFileProvider = recentFileProvider;
        _currentStyleProvider = currentStyleProvider;
        _currentZoomProvider = currentZoomProvider;
    }

    public void Attach(nint window)
    {
        Detach();

        var menu = BuildMainMenu();
        if (!NativeMethods.SetMenu(window, menu))
        {
            NativeMethods.DestroyMenu(menu);
            throw new Win32Exception();
        }

        _menu = menu;
        _window = window;
        RefreshStates();
        NativeMethods.DrawMenuBar(window);
    }

    public void RefreshStates()
    {
        if (_menu == 0)
        {
            return;
        }

        foreach (var command in Enum.GetValues<AppCommand>())
        {
            var state = _router.GetState(command);
            NativeMethods.EnableMenuItem(
                _menu,
                (uint)command,
                NativeMethods.MfByCommand | (state.IsEnabled ? NativeMethods.MfEnabled : NativeMethods.MfGrayed));
            NativeMethods.CheckMenuItem(
                _menu,
                (uint)command,
                NativeMethods.MfByCommand | (state.IsChecked ? NativeMethods.MfChecked : NativeMethods.MfUnchecked));
        }

        RefreshRecentWorkspaces();
        RefreshStyleMenu();
        RefreshZoomMenu();

        if (_window != 0)
        {
            NativeMethods.DrawMenuBar(_window);
        }
    }

    public bool TryGetStyleByCommandId(uint commandId, out string styleId)
    {
        return _styleCommandIds.TryGetValue(commandId, out styleId!);
    }

    public bool TryGetZoomByCommandId(uint commandId, out int zoomPercent)
    {
        return _zoomCommandIds.TryGetValue(commandId, out zoomPercent);
    }

    public void Detach()
    {
        if (_menu == 0)
        {
            return;
        }

        if (_window != 0)
        {
            NativeMethods.SetMenu(_window, 0);
            NativeMethods.DrawMenuBar(_window);
        }

        NativeMethods.DestroyMenu(_menu);
        _menu = 0;
        _window = 0;
        _recentWorkspaceMenu = 0;
        _styleMenu = 0;
        _zoomMenu = 0;
    }

    public void Dispose() => Detach();

    public void ShowEditorContextMenu(nint window, Point screenPoint)
    {
        var menu = BuildEditorContextMenu();
        try
        {
            foreach (var command in EditorContextCommands)
            {
                var state = _router.GetState(command);
                NativeMethods.EnableMenuItem(
                    menu,
                    (uint)command,
                    NativeMethods.MfByCommand | (state.IsEnabled ? NativeMethods.MfEnabled : NativeMethods.MfGrayed));
                NativeMethods.CheckMenuItem(
                    menu,
                    (uint)command,
                    NativeMethods.MfByCommand | (state.IsChecked ? NativeMethods.MfChecked : NativeMethods.MfUnchecked));
            }

            NativeMethods.SetForegroundWindow(window);
            var selectedCommand = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCommand,
                screenPoint.X,
                screenPoint.Y,
                window,
                0);
            NativeMethods.PostMessage(window, NativeMethods.WmNull, 0, 0);
            if (selectedCommand != 0)
            {
                _router.TryExecuteById((int)selectedCommand);
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private nint BuildMainMenu()
    {
        var root = CreateMenu(false);
        try
        {
            AppendPopup(root, "文件(&F)", BuildFileMenu());
            AppendPopup(root, "编辑(&E)", BuildEditMenu());
            AppendPopup(root, "段落(&P)", BuildParagraphMenu());
            AppendPopup(root, "格式(&F)", BuildFormatMenu());
            AppendPopup(root, "视图(&V)", BuildViewMenu());
            AppendPopup(root, "外观(&A)", BuildAppearanceMenu());
            AppendPopup(root, "帮助(&H)", BuildHelpMenu());
            return root;
        }
        catch
        {
            NativeMethods.DestroyMenu(root);
            throw;
        }
    }

    private nint BuildFileMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.NewDocument, "新建(&N)\tCtrl+N");
            AppendCommand(menu, AppCommand.NewWindow, "新建窗口(&W)");
            AppendCommand(menu, AppCommand.OpenDocument, "打开(&O)...\tCtrl+O");
            AppendCommand(menu, AppCommand.OpenDocumentInNewWindow, "在新窗口中打开...");
            AppendCommand(menu, AppCommand.OpenFolder, "打开文件夹(&F)...");

            _recentWorkspaceMenu = CreateMenu(true);
            AppendDisabledText(_recentWorkspaceMenu, "(暂无最近项目)");
            AppendPopup(menu, "最近项目(&R)", _recentWorkspaceMenu);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.SaveDocument, "保存(&S)\tCtrl+S");
            AppendCommand(menu, AppCommand.SaveDocumentAs, "另存为(&A)...\tCtrl+Shift+S");
            AppendCommand(menu, AppCommand.ExportDocument, "导出(&E)...");
            AppendCommand(menu, AppCommand.RecoverUnsavedFiles, "恢复未保存的文件(&U)");


            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.CloseFolder, "关闭文件夹(&C)");
            AppendCommand(menu, AppCommand.Exit, "退出(&X)");
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private static nint BuildEditMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.Undo, "撤销(&U)\tCtrl+Z");
            AppendCommand(menu, AppCommand.Redo, "重做(&R)\tCtrl+Y");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.Cut, "剪切(&T)\tCtrl+X");
            AppendCommand(menu, AppCommand.Copy, "复制(&C)\tCtrl+C");
            AppendCommand(menu, AppCommand.CopyMarkdown, "复制为 Markdown 源码(&M)");
            AppendCommand(menu, AppCommand.CopyPlainText, "复制为纯文本(&L)");
            AppendCommand(menu, AppCommand.Paste, "粘贴(&P)\tCtrl+V");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.Find, "查找(&F)\tCtrl+F");
            AppendCommand(menu, AppCommand.Replace, "替换(&H)\tCtrl+H");
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private static nint BuildEditorContextMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.ToggleBold, "粗体(&B)");
            AppendCommand(menu, AppCommand.ToggleItalic, "斜体(&I)");
            AppendSeparator(menu);

            AppendCommand(menu, AppCommand.SetParagraph, "正文(&P)");

            var headings = CreateMenu(true);
            AppendCommand(headings, AppCommand.SetHeading1, "一级标题(&1)");
            AppendCommand(headings, AppCommand.SetHeading2, "二级标题(&2)");
            AppendCommand(headings, AppCommand.SetHeading3, "三级标题(&3)");
            AppendCommand(headings, AppCommand.SetHeading4, "四级标题(&4)");
            AppendCommand(headings, AppCommand.SetHeading5, "五级标题(&5)");
            AppendCommand(headings, AppCommand.SetHeading6, "六级标题(&6)");
            AppendPopup(menu, "标题(&H)", headings);

            var lists = CreateMenu(true);
            AppendCommand(lists, AppCommand.ToggleBulletList, "无序列表(&B)");
            AppendCommand(lists, AppCommand.ToggleOrderedList, "有序列表(&O)");
            AppendCommand(lists, AppCommand.ToggleTaskList, "任务列表(&T)");
            AppendPopup(menu, "列表(&L)", lists);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.Cut, "剪切(&T)");
            AppendCommand(menu, AppCommand.Copy, "复制(&C)");
            AppendCommand(menu, AppCommand.CopyMarkdown, "复制为 Markdown 源码(&M)");
            AppendCommand(menu, AppCommand.CopyPlainText, "复制为纯文本(&L)");
            AppendCommand(menu, AppCommand.Paste, "粘贴(&P)");
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private static nint BuildParagraphMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.SetParagraph, "正文(&P)");

            var headings = CreateMenu(true);
            AppendCommand(headings, AppCommand.SetHeading1, "一级标题\tCtrl+1");
            AppendCommand(headings, AppCommand.SetHeading2, "二级标题\tCtrl+2");
            AppendCommand(headings, AppCommand.SetHeading3, "三级标题\tCtrl+3");
            AppendCommand(headings, AppCommand.SetHeading4, "四级标题\tCtrl+4");
            AppendCommand(headings, AppCommand.SetHeading5, "五级标题\tCtrl+5");
            AppendCommand(headings, AppCommand.SetHeading6, "六级标题\tCtrl+6");
            AppendPopup(menu, "标题(&H)", headings);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.PromoteHeading, "提升标题级别(&I)\tCtrl+.");
            AppendCommand(menu, AppCommand.DemoteHeading, "降低标题级别(&D)\tCtrl+,");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ToggleQuote, "引用(&Q)");
            AppendCommand(menu, AppCommand.ToggleCodeBlock, "代码块(&C)");
            AppendCommand(menu, AppCommand.InsertHorizontalRule, "水平线(&R)");

            var lists = CreateMenu(true);
            AppendCommand(lists, AppCommand.ToggleBulletList, "无序列表(&B)");
            AppendCommand(lists, AppCommand.ToggleOrderedList, "有序列表(&O)");
            AppendCommand(lists, AppCommand.ToggleTaskList, "任务列表(&T)");
            AppendPopup(menu, "列表(&L)", lists);

            var table = CreateMenu(true);
            AppendCommand(table, AppCommand.InsertTable, "插入表格(&I)");
            AppendSeparator(table);
            AppendCommand(table, AppCommand.AddTableRowBefore, "在上方添加行(&A)");
            AppendCommand(table, AppCommand.AddTableRowAfter, "在下方添加行(&B)");
            AppendCommand(table, AppCommand.DeleteTableRow, "删除当前行(&R)");
            AppendSeparator(table);
            AppendCommand(table, AppCommand.AddTableColumnBefore, "在左侧添加列(&L)");
            AppendCommand(table, AppCommand.AddTableColumnAfter, "在右侧添加列(&G)");
            AppendCommand(table, AppCommand.DeleteTableColumn, "删除当前列(&C)");
            AppendSeparator(table);
            AppendCommand(table, AppCommand.AlignTableLeft, "左对齐");
            AppendCommand(table, AppCommand.AlignTableCenter, "居中对齐");
            AppendCommand(table, AppCommand.AlignTableRight, "右对齐");
            AppendSeparator(table);
            AppendCommand(table, AppCommand.DeleteTable, "删除表格(&D)");
            AppendPopup(menu, "表格(&T)", table);
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private nint BuildFormatMenu()
    {
        var menu = CreateMenu(true);
        AppendCommand(menu, AppCommand.ToggleBold, "加粗(&B)\tCtrl+B");
        AppendCommand(menu, AppCommand.ToggleItalic, "斜体(&I)\tCtrl+I");
        AppendCommand(menu, AppCommand.ToggleUnderline, "下划线(&U)\tCtrl+U");
        AppendCommand(menu, AppCommand.ToggleStrike, "删除线(&S)");
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.ToggleInlineCode, "行内代码(&C)");
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.InsertLink, "插入超链接(&K)...\tCtrl+K");
        AppendCommand(menu, AppCommand.InsertImage, "插入图片(&M)...");
        AppendCommand(menu, AppCommand.RotateImageClockwise, "顺时针旋转图片(&R)");
        return menu;
    }

    private nint BuildAppearanceMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            _styleMenu = CreateMenu(true);
            AppendPopup(menu, "排版样式(&Y)", _styleMenu);
            RefreshStyleMenu();

            var themes = CreateMenu(true);
            // 颜色主题：功能暂留空，默认选中“白色”，供后续开发。
            Append(themes, NativeMethods.MfString | NativeMethods.MfChecked, 0, "白色(&W)");
            AppendPopup(menu, "颜色主题(&C)", themes);

            AppendSeparator(menu);
            _zoomMenu = CreateMenu(true);
            AppendPopup(menu, "设置缩放(&Z)", _zoomMenu);
            RefreshZoomMenu();
            AppendCommand(menu, AppCommand.ZoomIn, "放大(&I)");
            AppendCommand(menu, AppCommand.ZoomOut, "缩小(&S)");
            AppendCommand(menu, AppCommand.ZoomReset, "重置为100%(&R)");

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.OpenThemeFolder, "打开主题文件夹(&O)...");
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    /// <summary>
    /// 重建“设置缩放”子菜单：选项来自 AppearanceSettings.ZoomPercentOptions，
    /// 命令 ID 从 CommandCatalog.ZoomCommandBase 起按序分配，并在当前缩放上打勾。
    /// </summary>
    private void RefreshZoomMenu()
    {
        if (_zoomMenu == 0)
        {
            return;
        }

        var count = NativeMethods.GetMenuItemCount(_zoomMenu);
        for (var index = count - 1; index >= 0; index--)
        {
            NativeMethods.DeleteMenu(_zoomMenu, (uint)index, NativeMethods.MfByPosition);
        }

        _zoomCommandIds.Clear();
        var current = _currentZoomProvider();
        foreach (var percent in AppearanceSettings.ZoomPercentOptions)
        {
            if ((uint)(CommandCatalog.ZoomCommandBase + _zoomCommandIds.Count) > CommandCatalog.ZoomCommandMax)
            {
                break;
            }

            var commandId = (uint)(CommandCatalog.ZoomCommandBase + _zoomCommandIds.Count);
            _zoomCommandIds[commandId] = percent;
            Append(
                _zoomMenu,
                NativeMethods.MfString | (percent == current ? NativeMethods.MfChecked : NativeMethods.MfUnchecked),
                commandId,
                $"{percent}%");
        }
    }

    /// <summary>
    /// 重建排版样式子菜单：项目由 Resources/Styles 目录动态发现，
    /// 命令 ID 从 CommandCatalog.StyleCommandBase 起按序分配，并在当前样式上打勾。
    /// </summary>
    private void RefreshStyleMenu()
    {
        if (_styleMenu == 0)
        {
            return;
        }

        var count = NativeMethods.GetMenuItemCount(_styleMenu);
        for (var index = count - 1; index >= 0; index--)
        {
            NativeMethods.DeleteMenu(_styleMenu, (uint)index, NativeMethods.MfByPosition);
        }

        _styleCommandIds.Clear();
        var current = _currentStyleProvider();
        foreach (var (id, displayName) in StyleService.GetAllStyles())
        {
            if ((uint)(CommandCatalog.StyleCommandBase + _styleCommandIds.Count) > CommandCatalog.StyleCommandMax)
            {
                break;
            }

            var commandId = (uint)(CommandCatalog.StyleCommandBase + _styleCommandIds.Count);
            var isCurrent = string.Equals(id, current, StringComparison.Ordinal);
            _styleCommandIds[commandId] = id;
            Append(
                _styleMenu,
                NativeMethods.MfString | (isCurrent ? NativeMethods.MfChecked : NativeMethods.MfUnchecked),
                commandId,
                displayName);
        }
    }

    private static nint BuildViewMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.ToggleSidebar, "显示侧栏(&B)");
            AppendCommand(menu, AppCommand.ViewTree, "树结构(&T)");
            AppendCommand(menu, AppCommand.ViewList, "文档列表(&L)");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowStatusBar, "显示状态栏(&S)");
            AppendCommand(menu, AppCommand.ToggleSourceMode, "源码模式(&S)");
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private static nint BuildHelpMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.ShowShortcuts, "快捷键(&K)");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowPreferences, "首选项(&P)...");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowAbout, "关于 MarkLeaf(&A)");
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private static nint CreateMenu(bool popup)
    {
        var menu = popup ? NativeMethods.CreatePopupMenu() : NativeMethods.CreateMenu();
        return menu != 0 ? menu : throw new Win32Exception();
    }

    private void RefreshRecentWorkspaces()
    {
        if (_recentWorkspaceMenu == 0)
        {
            return;
        }

        var count = NativeMethods.GetMenuItemCount(_recentWorkspaceMenu);
        for (var index = count - 1; index >= 0; index--)
        {
            NativeMethods.DeleteMenu(_recentWorkspaceMenu, (uint)index, NativeMethods.MfByPosition);
        }

        var files = _recentFileProvider().Take(8).ToArray();
        var folders = _recentWorkspaceProvider().Take(8).ToArray();

        if (files.Length == 0 && folders.Length == 0)
        {
            AppendDisabledText(_recentWorkspaceMenu, "(暂无最近项目)");
            return;
        }

        if (files.Length > 0)
        {
            AppendDisabledText(_recentWorkspaceMenu, "最近文件");
            for (var index = 0; index < files.Length; index++)
            {
                var command = (AppCommand)((int)AppCommand.OpenRecentFile1 + index);
                AppendCommand(_recentWorkspaceMenu, command, $"{index + 1}  {files[index]}");
            }
        }

        if (folders.Length > 0)
        {
            if (files.Length > 0)
            {
                AppendSeparator(_recentWorkspaceMenu);
            }

            AppendDisabledText(_recentWorkspaceMenu, "最近文件夹");
            for (var index = 0; index < folders.Length; index++)
            {
                var command = (AppCommand)((int)AppCommand.OpenRecentWorkspace1 + index);
                AppendCommand(_recentWorkspaceMenu, command, $"{index + 1}  {folders[index]}");
            }
        }
    }

    private static void AppendCommand(nint menu, AppCommand command, string text)
    {
        Append(menu, NativeMethods.MfString, (nuint)command, text);
    }

    private static void AppendPopup(nint menu, string text, nint popup)
    {
        try
        {
            Append(menu, NativeMethods.MfPopup, (nuint)popup, text);
        }
        catch
        {
            NativeMethods.DestroyMenu(popup);
            throw;
        }
    }

    private static void AppendSeparator(nint menu)
    {
        Append(menu, NativeMethods.MfSeparator, 0, null);
    }

    private static void AppendDisabledText(nint menu, string text)
    {
        Append(menu, NativeMethods.MfString | NativeMethods.MfGrayed, 0, text);
    }

    private static void Append(nint menu, uint flags, nuint item, string? text)
    {
        if (!NativeMethods.AppendMenu(menu, flags, item, text))
        {
            throw new Win32Exception();
        }
    }
}
