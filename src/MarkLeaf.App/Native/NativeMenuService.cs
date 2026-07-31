using System.ComponentModel;
using MarkLeaf.Commands;

namespace MarkLeaf.Native;

internal sealed class NativeMenuService : IDisposable
{
    private readonly CommandRouter _router;
    private nint _menu;
    private nint _window;

    public NativeMenuService(CommandRouter router)
    {
        _router = router;
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

        if (_window != 0)
        {
            NativeMethods.DrawMenuBar(_window);
        }
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
    }

    public void Dispose() => Detach();

    private static nint BuildMainMenu()
    {
        var root = CreateMenu(false);
        try
        {
            AppendPopup(root, "文件(&F)", BuildFileMenu());
            AppendPopup(root, "编辑(&E)", BuildEditMenu());
            AppendPopup(root, "段落(&P)", BuildParagraphMenu());
            AppendPopup(root, "视图(&V)", BuildViewMenu());
            AppendPopup(root, "帮助(&H)", BuildHelpMenu());
            return root;
        }
        catch
        {
            NativeMethods.DestroyMenu(root);
            throw;
        }
    }

    private static nint BuildFileMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.NewDocument, "新建(&N)\tCtrl+N");
            AppendCommand(menu, AppCommand.OpenDocument, "打开(&O)...\tCtrl+O");
            AppendCommand(menu, AppCommand.OpenFolder, "打开文件夹(&F)...");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.SaveDocument, "保存(&S)\tCtrl+S");
            AppendCommand(menu, AppCommand.SaveDocumentAs, "另存为(&A)...\tCtrl+Shift+S");
            AppendCommand(menu, AppCommand.ExportDocument, "导出(&E)...");
            AppendCommand(menu, AppCommand.CleanUnreferencedAssets, "清理未引用资源(&C)...");
            AppendSeparator(menu);

            var recent = CreateMenu(true);
            AppendDisabledText(recent, "(暂无最近文件)");
            AppendPopup(menu, "最近文件(&R)", recent);

            AppendSeparator(menu);
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
            AppendCommand(menu, AppCommand.ToggleBold, "粗体(&B)\tCtrl+B");
            AppendCommand(menu, AppCommand.ToggleItalic, "斜体(&I)\tCtrl+I");
            AppendCommand(menu, AppCommand.InsertLink, "插入链接(&K)...\tCtrl+K");
            AppendCommand(menu, AppCommand.InsertImage, "插入图片(&M)...");
            AppendCommand(menu, AppCommand.RotateImageClockwise, "顺时针旋转图片(&R)");
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

    private static nint BuildViewMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.ToggleWorkspace, "工作区侧栏(&W)");
            AppendCommand(menu, AppCommand.ToggleOutline, "文档大纲(&O)");
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ToggleFocusMode, "专注模式(&F)\tF11");
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
