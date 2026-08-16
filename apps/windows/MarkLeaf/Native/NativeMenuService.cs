using System.ComponentModel;
using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;

namespace MarkLeaf.Native;

internal sealed class NativeMenuService : IDisposable
{
    internal static readonly AppCommand[] BlockHandleCommands =
    [
        AppCommand.SetParagraph,
        AppCommand.InsertLineBefore,
        AppCommand.InsertLineAfter,
        AppCommand.SetHeading1,
        AppCommand.SetHeading2,
        AppCommand.SetHeading3,
        AppCommand.SetHeading4,
        AppCommand.SetHeading5,
        AppCommand.SetHeading6,
        AppCommand.ToggleQuote,
        AppCommand.ToggleCodeBlock,
        AppCommand.ToggleBulletList,
        AppCommand.ToggleOrderedList,
        AppCommand.ToggleTaskList,
        AppCommand.InsertHorizontalRule,
        AppCommand.InsertTable,
        AppCommand.ClearFormat,
    ];

    private readonly CommandRouter _router;
    private readonly Func<IReadOnlyList<string>> _recentWorkspaceProvider;
    private readonly Func<IReadOnlyList<string>> _recentFileProvider;
    private readonly Func<string> _currentStyleProvider;
    private readonly Func<int> _currentZoomProvider;
    private readonly Func<string> _currentColorThemeProvider;
    private readonly Func<bool> _followSystemProvider;
    private readonly Dictionary<uint, string> _styleCommandIds = new();
    private readonly Dictionary<uint, int> _zoomCommandIds = new();
    private readonly Dictionary<uint, string> _colorCommandIds = new();
    private nint _menu;
    private nint _window;
    private nint _recentWorkspaceMenu;
    private nint _styleMenu;
    private nint _zoomMenu;
    private nint _colorMenu;

    public NativeMenuService(
        CommandRouter router,
        Func<IReadOnlyList<string>> recentWorkspaceProvider,
        Func<IReadOnlyList<string>> recentFileProvider,
        Func<string> currentStyleProvider,
        Func<int> currentZoomProvider,
        Func<string> currentColorThemeProvider,
        Func<bool> followSystemProvider)
    {
        _router = router;
        _recentWorkspaceProvider = recentWorkspaceProvider;
        _recentFileProvider = recentFileProvider;
        _currentStyleProvider = currentStyleProvider;
        _currentZoomProvider = currentZoomProvider;
        _currentColorThemeProvider = currentColorThemeProvider;
        _followSystemProvider = followSystemProvider;
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
        RefreshColorMenu();

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

    public bool TryGetColorThemeByCommandId(uint commandId, out string colorThemeId)
    {
        return _colorCommandIds.TryGetValue(commandId, out colorThemeId!);
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
        _colorMenu = 0;
    }

    public void Dispose() => Detach();

    public void ShowEditorContextMenu(nint window, Point screenPoint, EditorCommandStatus status)
    {
        var (menu, commands) = BuildEditorContextMenu(status);
        try
        {
            foreach (var command in commands)
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

    public void ShowBlockHandleMenu(nint window, Point screenPoint)
    {
        var menu = BuildBlockHandleMenu();
        try
        {
            foreach (var command in BlockHandleCommands)
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
                NativeMethods.TpmLeftButton | NativeMethods.TpmReturnCommand,
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

    private static nint BuildBlockHandleMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.SetParagraph, Loc.Get("menu.paragraph.paragraph"));

            var headings = CreateMenu(true);
            AppendCommand(headings, AppCommand.SetHeading1, Loc.Get("menu.paragraph.heading1"));
            AppendCommand(headings, AppCommand.SetHeading2, Loc.Get("menu.paragraph.heading2"));
            AppendCommand(headings, AppCommand.SetHeading3, Loc.Get("menu.paragraph.heading3"));
            AppendCommand(headings, AppCommand.SetHeading4, Loc.Get("menu.paragraph.heading4"));
            AppendCommand(headings, AppCommand.SetHeading5, Loc.Get("menu.paragraph.heading5"));
            AppendCommand(headings, AppCommand.SetHeading6, Loc.Get("menu.paragraph.heading6"));
            AppendPopup(menu, Loc.Get("menu.paragraph.heading"), headings);

            AppendCommand(menu, AppCommand.ToggleQuote, Loc.Get("menu.paragraph.quote"));
            AppendCommand(menu, AppCommand.ToggleCodeBlock, Loc.Get("menu.paragraph.codeBlock"));
            AppendSeparator(menu);

            var lists = CreateMenu(true);
            AppendCommand(lists, AppCommand.ToggleBulletList, Loc.Get("menu.paragraph.bulletList"));
            AppendCommand(lists, AppCommand.ToggleOrderedList, Loc.Get("menu.paragraph.orderedList"));
            AppendCommand(lists, AppCommand.ToggleTaskList, Loc.Get("menu.paragraph.taskList"));
            AppendPopup(menu, Loc.Get("menu.paragraph.list"), lists);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InsertHorizontalRule, Loc.Get("menu.paragraph.horizontalRule"));
            AppendCommand(menu, AppCommand.InsertTable, Loc.Get("menu.paragraph.insertTable"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InsertLineBefore, Loc.Get("menu.paragraph.insertLineBefore"));
            AppendCommand(menu, AppCommand.InsertLineAfter, Loc.Get("menu.paragraph.insertLineAfter"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ClearFormat, Loc.Get("menu.paragraph.clearFormat"));
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private nint BuildMainMenu()
    {
        var root = CreateMenu(false);
        try
        {
            AppendPopup(root, Loc.Get("menu.file.label"), BuildFileMenu());
            AppendPopup(root, Loc.Get("menu.edit.label"), BuildEditMenu());
            AppendPopup(root, Loc.Get("menu.paragraph.label"), BuildParagraphMenu());
            AppendPopup(root, Loc.Get("menu.format.label"), BuildFormatMenu());
            AppendPopup(root, Loc.Get("menu.view.label"), BuildViewMenu());
            AppendPopup(root, Loc.Get("menu.appearance.label"), BuildAppearanceMenu());
            AppendPopup(root, Loc.Get("menu.help.label"), BuildHelpMenu());
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
            AppendCommand(menu, AppCommand.NewDocument, Loc.Get("menu.file.new"));
            AppendCommand(menu, AppCommand.NewWindow, Loc.Get("menu.file.newWindow"));
            AppendCommand(menu, AppCommand.OpenDocument, Loc.Get("menu.file.open"));
            AppendCommand(menu, AppCommand.OpenDocumentReadOnly, Loc.Get("menu.file.openReadOnly"));
            AppendCommand(menu, AppCommand.OpenDocumentInNewWindow, Loc.Get("menu.file.openInNewWindow"));
            AppendCommand(menu, AppCommand.OpenFolder, Loc.Get("menu.file.openFolder"));

            _recentWorkspaceMenu = CreateMenu(true);
            AppendDisabledText(_recentWorkspaceMenu, Loc.Get("menu.file.noRecentItems"));
            AppendPopup(menu, Loc.Get("menu.file.recentItems"), _recentWorkspaceMenu);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.SaveDocument, Loc.Get("menu.file.save"));
            AppendCommand(menu, AppCommand.SaveDocumentAs, Loc.Get("menu.file.saveAs"));
            var exportMenu = CreateMenu(true);
            AppendCommand(exportMenu, AppCommand.ExportPdf, Loc.Get("menu.file.exportPdf"));
            AppendCommand(exportMenu, AppCommand.ExportHtml, Loc.Get("menu.file.exportHtml"));
            AppendPopup(menu, Loc.Get("menu.file.export"), exportMenu);
            AppendCommand(menu, AppCommand.Print, Loc.Get("menu.file.print"));
            AppendCommand(menu, AppCommand.RecoverUnsavedFiles, Loc.Get("menu.file.recoverUnsaved"));


            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.CloseFolder, Loc.Get("menu.file.closeFolder"));
            AppendCommand(menu, AppCommand.Exit, Loc.Get("menu.file.exit"));
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
            AppendCommand(menu, AppCommand.Undo, Loc.Get("menu.edit.undo"));
            AppendCommand(menu, AppCommand.Redo, Loc.Get("menu.edit.redo"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.Cut, Loc.Get("menu.edit.cut"));
            AppendCommand(menu, AppCommand.Copy, Loc.Get("menu.edit.copy"));

            var copyAs = CreateMenu(true);
            AppendCommand(copyAs, AppCommand.CopyMarkdown, Loc.Get("menu.edit.copyMarkdown"));
            AppendCommand(copyAs, AppCommand.CopyPlainText, Loc.Get("menu.edit.copyPlainText"));
            AppendPopup(menu, Loc.Get("menu.edit.copyAs"), copyAs);

            AppendCommand(menu, AppCommand.Paste, Loc.Get("menu.edit.paste"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.Find, Loc.Get("menu.edit.find"));
            AppendCommand(menu, AppCommand.Replace, Loc.Get("menu.edit.replace"));
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private (nint Menu, AppCommand[] Commands) BuildEditorContextMenu(EditorCommandStatus status)
    {
        var menu = CreateMenu(true);
        var commands = new List<AppCommand>();
        try
        {
            if (status.SourceMode)
            {
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                commands.AddRange([AppCommand.Cut, AppCommand.Copy, AppCommand.Paste, AppCommand.SelectAll]);
            }
            else if (status.InTable)
            {
                // 行操作
                AppendCommand(menu, AppCommand.AddTableRowBefore, Loc.Get("menu.paragraph.addRowAbove"));
                AppendCommand(menu, AppCommand.AddTableRowAfter, Loc.Get("menu.paragraph.addRowBelow"));
                AppendCommand(menu, AppCommand.DeleteTableRow, Loc.Get("menu.paragraph.deleteRow"));
                AppendSeparator(menu);

                // 列操作
                AppendCommand(menu, AppCommand.AddTableColumnBefore, Loc.Get("menu.paragraph.addColumnLeft"));
                AppendCommand(menu, AppCommand.AddTableColumnAfter, Loc.Get("menu.paragraph.addColumnRight"));
                AppendCommand(menu, AppCommand.DeleteTableColumn, Loc.Get("menu.paragraph.deleteColumn"));
                AppendSeparator(menu);

                // 对齐
                var align = CreateMenu(true);
                AppendCommand(align, AppCommand.AlignTableLeft, Loc.Get("menu.paragraph.alignLeft"));
                AppendCommand(align, AppCommand.AlignTableCenter, Loc.Get("menu.paragraph.alignCenter"));
                AppendCommand(align, AppCommand.AlignTableRight, Loc.Get("menu.paragraph.alignRight"));
                AppendPopup(menu, Loc.Get("contextMenu.table.align"), align);
                AppendSeparator(menu);

                // 剪贴板
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);

                AppendCommand(menu, AppCommand.DeleteTable, Loc.Get("menu.paragraph.deleteTable"));
                commands.AddRange([
                    AppCommand.AddTableRowBefore, AppCommand.AddTableRowAfter, AppCommand.DeleteTableRow,
                    AppCommand.AddTableColumnBefore, AppCommand.AddTableColumnAfter, AppCommand.DeleteTableColumn,
                    AppCommand.AlignTableLeft, AppCommand.AlignTableCenter, AppCommand.AlignTableRight,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.Paste,
                    AppCommand.DeleteTable]);
            }
            else if (status.MathInline || status.MathBlock)
            {
                AppendCommand(menu, AppCommand.EditMath, Loc.Get("contextMenu.math.edit"));
                AppendCommand(menu, AppCommand.ConvertMath,
                    status.MathBlock ? Loc.Get("contextMenu.math.toInline") : Loc.Get("contextMenu.math.toBlock"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.DeleteMath, Loc.Get("contextMenu.math.delete"));
                commands.AddRange([AppCommand.EditMath, AppCommand.ConvertMath, AppCommand.DeleteMath]);
            }
            else if (status.ImageSelected)
            {
                AppendCommand(menu, AppCommand.ChangeImage, Loc.Get("contextMenu.image.change"));

                AppendPopup(menu, Loc.Get("contextMenu.image.resize"), BuildResizeImageSubmenu());

                AppendCommand(menu, AppCommand.RotateImageClockwise, Loc.Get("contextMenu.image.rotate"));
                AppendCommand(menu, AppCommand.SaveImageAs, Loc.Get("contextMenu.image.saveAs"));
                AppendSeparator(menu);

                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                commands.AddRange([
                    AppCommand.ChangeImage, AppCommand.ResizeImage100, AppCommand.ResizeImage50,
                    AppCommand.ResizeImage75, AppCommand.ResizeImage90, AppCommand.RotateImageClockwise,
                    AppCommand.SaveImageAs, AppCommand.Cut, AppCommand.Copy, AppCommand.Paste]);
            }
            else if (status.CodeBlock)
            {
                AppendCommand(menu, AppCommand.ExitCode, Loc.Get("contextMenu.exitCode"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                commands.AddRange([AppCommand.ExitCode, AppCommand.Cut, AppCommand.Copy, AppCommand.Paste]);
            }
            else
            {
                AppendCommand(menu, AppCommand.FormatPainter, Loc.Get("contextMenu.formatPainter"));
                AppendSeparator(menu);

                var paragraphMenu = BuildBlockHandleMenu();
                AppendPopup(menu, Loc.Get("contextMenu.paragraphGroup"), paragraphMenu);

                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));

                var copyAs = CreateMenu(true);
                AppendCommand(copyAs, AppCommand.CopyMarkdown, Loc.Get("contextMenu.copyMarkdown"));
                AppendCommand(copyAs, AppCommand.CopyPlainText, Loc.Get("contextMenu.copyPlainText"));
                AppendPopup(menu, Loc.Get("contextMenu.copyAs"), copyAs);

                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                commands.AddRange(BlockHandleCommands);
                commands.AddRange([
                    AppCommand.FormatPainter,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.CopyMarkdown, AppCommand.CopyPlainText,
                    AppCommand.Paste, AppCommand.SelectAll]);
            }

            return (menu, commands.ToArray());
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
            AppendCommand(menu, AppCommand.SetParagraph, Loc.Get("menu.paragraph.paragraph"));

            var headings = CreateMenu(true);
            AppendCommand(headings, AppCommand.SetHeading1, Loc.Get("menu.paragraph.heading1"));
            AppendCommand(headings, AppCommand.SetHeading2, Loc.Get("menu.paragraph.heading2"));
            AppendCommand(headings, AppCommand.SetHeading3, Loc.Get("menu.paragraph.heading3"));
            AppendCommand(headings, AppCommand.SetHeading4, Loc.Get("menu.paragraph.heading4"));
            AppendCommand(headings, AppCommand.SetHeading5, Loc.Get("menu.paragraph.heading5"));
            AppendCommand(headings, AppCommand.SetHeading6, Loc.Get("menu.paragraph.heading6"));
            AppendPopup(menu, Loc.Get("menu.paragraph.heading"), headings);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.PromoteHeading, Loc.Get("menu.paragraph.promoteHeading"));
            AppendCommand(menu, AppCommand.DemoteHeading, Loc.Get("menu.paragraph.demoteHeading"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ToggleQuote, Loc.Get("menu.paragraph.quote"));
            AppendCommand(menu, AppCommand.InsertMathBlock, Loc.Get("menu.paragraph.insertMathBlock"));
            AppendCommand(menu, AppCommand.ToggleCodeBlock, Loc.Get("menu.paragraph.codeBlock"));
            AppendCommand(menu, AppCommand.InsertHorizontalRule, Loc.Get("menu.paragraph.horizontalRule"));
            AppendSeparator(menu);

            var lists = CreateMenu(true);
            AppendCommand(lists, AppCommand.ToggleBulletList, Loc.Get("menu.paragraph.bulletList"));
            AppendCommand(lists, AppCommand.ToggleOrderedList, Loc.Get("menu.paragraph.orderedList"));
            AppendCommand(lists, AppCommand.ToggleTaskList, Loc.Get("menu.paragraph.taskList"));
            AppendPopup(menu, Loc.Get("menu.paragraph.list"), lists);

            var table = CreateMenu(true);
            AppendCommand(table, AppCommand.InsertTable, Loc.Get("menu.paragraph.insertTable"));
            AppendSeparator(table);
            AppendCommand(table, AppCommand.AddTableRowBefore, Loc.Get("menu.paragraph.addRowAbove"));
            AppendCommand(table, AppCommand.AddTableRowAfter, Loc.Get("menu.paragraph.addRowBelow"));
            AppendCommand(table, AppCommand.DeleteTableRow, Loc.Get("menu.paragraph.deleteRow"));
            AppendSeparator(table);
            AppendCommand(table, AppCommand.AddTableColumnBefore, Loc.Get("menu.paragraph.addColumnLeft"));
            AppendCommand(table, AppCommand.AddTableColumnAfter, Loc.Get("menu.paragraph.addColumnRight"));
            AppendCommand(table, AppCommand.DeleteTableColumn, Loc.Get("menu.paragraph.deleteColumn"));
            AppendSeparator(table);
            AppendCommand(table, AppCommand.AlignTableLeft, Loc.Get("menu.paragraph.alignLeft"));
            AppendCommand(table, AppCommand.AlignTableCenter, Loc.Get("menu.paragraph.alignCenter"));
            AppendCommand(table, AppCommand.AlignTableRight, Loc.Get("menu.paragraph.alignRight"));
            AppendSeparator(table);
            AppendCommand(table, AppCommand.DeleteTable, Loc.Get("menu.paragraph.deleteTable"));
            AppendPopup(menu, Loc.Get("menu.paragraph.table"), table);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InsertLineBefore, Loc.Get("menu.paragraph.insertLineBefore"));
            AppendCommand(menu, AppCommand.InsertLineAfter, Loc.Get("menu.paragraph.insertLineAfter"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ClearFormat, Loc.Get("menu.paragraph.clearFormat"));
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
        AppendCommand(menu, AppCommand.ToggleBold, Loc.Get("menu.format.bold"));
        AppendCommand(menu, AppCommand.ToggleItalic, Loc.Get("menu.format.italic"));
        AppendCommand(menu, AppCommand.ToggleUnderline, Loc.Get("menu.format.underline"));
        AppendCommand(menu, AppCommand.ToggleStrike, Loc.Get("menu.format.strikethrough"));
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.ToggleInlineCode, Loc.Get("menu.format.inlineCode"));
        AppendCommand(menu, AppCommand.InsertMathInline, Loc.Get("menu.format.insertMathInline"));
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.FormatPainter, Loc.Get("menu.format.formatPainter"));
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.InsertLink, Loc.Get("menu.format.insertLink"));
        AppendCommand(menu, AppCommand.InsertImage, Loc.Get("menu.format.insertImage"));
        AppendCommand(menu, AppCommand.InsertImageFromUrl, Loc.Get("menu.format.insertImageFromUrl"));
        AppendCommand(menu, AppCommand.RotateImageClockwise, Loc.Get("menu.format.rotateImageClockwise"));
        AppendPopup(menu, Loc.Get("menu.format.resizeImage"), BuildResizeImageSubmenu());
        AppendCommand(menu, AppCommand.SaveImageAs, Loc.Get("menu.format.saveImageAs"));
        return menu;
    }

    private static nint BuildResizeImageSubmenu()
    {
        var resize = CreateMenu(true);
        AppendCommand(resize, AppCommand.ResizeImage100, Loc.Get("contextMenu.image.resizeFull"));
        AppendCommand(resize, AppCommand.ResizeImage50, Loc.Get("contextMenu.image.resize50"));
        AppendCommand(resize, AppCommand.ResizeImage75, Loc.Get("contextMenu.image.resize75"));
        AppendCommand(resize, AppCommand.ResizeImage90, Loc.Get("contextMenu.image.resize90"));
        return resize;
    }

    private nint BuildAppearanceMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            _styleMenu = CreateMenu(true);
            AppendPopup(menu, Loc.Get("menu.appearance.style"), _styleMenu);
            RefreshStyleMenu();

            _colorMenu = CreateMenu(true);
            AppendPopup(menu, Loc.Get("menu.appearance.colorTheme"), _colorMenu);
            RefreshColorMenu();

            AppendSeparator(menu);
            _zoomMenu = CreateMenu(true);
            AppendPopup(menu, Loc.Get("menu.appearance.zoom"), _zoomMenu);
            RefreshZoomMenu();
            AppendCommand(menu, AppCommand.ZoomIn, Loc.Get("menu.appearance.zoomIn"));
            AppendCommand(menu, AppCommand.ZoomOut, Loc.Get("menu.appearance.zoomOut"));
            AppendCommand(menu, AppCommand.ZoomReset, Loc.Get("menu.appearance.zoomReset"));

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.AddTheme, Loc.Get("menu.appearance.addTheme"));
            AppendCommand(menu, AppCommand.OpenThemeFolder, Loc.Get("menu.appearance.openThemeFolder"));
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

    /// <summary>
    /// 重建颜色主题子菜单：项目由 ColorThemeService 扫描 Resources/Styles 目录中
    /// 标记为 @type: color-theme 的 CSS 文件动态发现，
    /// 命令 ID 从 CommandCatalog.ColorCommandBase 起按序分配，并在当前主题上打勾。
    /// </summary>
    private void RefreshColorMenu()
    {
        if (_colorMenu == 0)
        {
            return;
        }

        var count = NativeMethods.GetMenuItemCount(_colorMenu);
        for (var index = count - 1; index >= 0; index--)
        {
            NativeMethods.DeleteMenu(_colorMenu, (uint)index, NativeMethods.MfByPosition);
        }

        _colorCommandIds.Clear();
        var current = _currentColorThemeProvider();
        var followSystem = _followSystemProvider();
        var themeEnabled = !followSystem;
        var lightThemes = ColorThemeService.All.Where(t => !t.IsDark).ToArray();
        var darkThemes = ColorThemeService.All.Where(t => t.IsDark).ToArray();

        foreach (var theme in lightThemes)
        {
            if ((uint)(CommandCatalog.ColorCommandBase + _colorCommandIds.Count) > CommandCatalog.ColorCommandMax)
                break;
            AppendColorThemeItem(theme, current, themeEnabled);
        }

        if (lightThemes.Length > 0 && darkThemes.Length > 0)
            AppendSeparator(_colorMenu);

        foreach (var theme in darkThemes)
        {
            if ((uint)(CommandCatalog.ColorCommandBase + _colorCommandIds.Count) > CommandCatalog.ColorCommandMax)
                break;
            AppendColorThemeItem(theme, current, themeEnabled);
        }

        AppendSeparator(_colorMenu);
        Append(
            _colorMenu,
            NativeMethods.MfString | (followSystem ? NativeMethods.MfChecked : NativeMethods.MfUnchecked),
            (uint)AppCommand.FollowSystemColorMode,
            Loc.Get("prefs.appearance.followSystemColor"));
    }

    private void AppendColorThemeItem(ColorTheme theme, string current, bool enabled)
    {
        var commandId = (uint)(CommandCatalog.ColorCommandBase + _colorCommandIds.Count);
        var isCurrent = string.Equals(theme.Id, current, StringComparison.Ordinal);
        _colorCommandIds[commandId] = theme.Id;
        var flags = NativeMethods.MfString
            | (isCurrent ? NativeMethods.MfChecked : NativeMethods.MfUnchecked)
            | (enabled ? NativeMethods.MfEnabled : NativeMethods.MfGrayed);
        Append(_colorMenu, flags, commandId, theme.DisplayName);
    }

    private static nint BuildViewMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.ToggleSidebar, Loc.Get("menu.view.toggleSidebar"));
            AppendCommand(menu, AppCommand.ShowStatusBar, Loc.Get("menu.view.showStatusBar"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.SwitchToWorkspace, Loc.Get("menu.view.workspace"));
            AppendCommand(menu, AppCommand.SwitchToOutline, Loc.Get("menu.view.outline"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ViewTree, Loc.Get("menu.view.treeView"));
            AppendCommand(menu, AppCommand.ViewList, Loc.Get("menu.view.documentList"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ToggleSourceMode, Loc.Get("menu.view.sourceMode"));
            AppendCommand(menu, AppCommand.ToggleFocusMode, Loc.Get("menu.view.focusMode"));
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
            AppendCommand(menu, AppCommand.ShowChangelog, Loc.Get("menu.help.changelog"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowPreferences, Loc.Get("menu.help.preferences"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowAbout, Loc.Get("menu.help.about"));
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
            AppendDisabledText(_recentWorkspaceMenu, Loc.Get("menu.file.noRecentItems"));
            return;
        }

        if (files.Length > 0)
        {
            AppendDisabledText(_recentWorkspaceMenu, Loc.Get("menu.file.recentFiles"));
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

            AppendDisabledText(_recentWorkspaceMenu, Loc.Get("menu.file.recentFolders"));
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
