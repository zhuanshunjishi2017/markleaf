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
        AppCommand.IncreaseListIndent,
        AppCommand.DecreaseListIndent,
        AppCommand.InsertHorizontalRule,
        AppCommand.InsertFootnote,
        AppCommand.InsertTable,
    ];

    private static readonly AppCommand[] FootnoteBlockHandleCommands =
    [
        AppCommand.GoToFootnoteReference,
        AppCommand.ResetFootnoteLabel,
        AppCommand.ClearFootnoteReferences,
        AppCommand.DeleteFootnote,
    ];

    private static readonly AppCommand[] TableBlockHandleCommands =
    [
        AppCommand.EditTableCaption,
        AppCommand.AddTableRowBefore,
        AppCommand.AddTableRowAfter,
        AppCommand.DeleteTableRow,
        AppCommand.AddTableColumnBefore,
        AppCommand.AddTableColumnAfter,
        AppCommand.DeleteTableColumn,
        AppCommand.AlignTableLeft,
        AppCommand.AlignTableCenter,
        AppCommand.AlignTableRight,
        AppCommand.DeleteTable,
    ];

    private readonly CommandRouter _router;
    private readonly ShortcutManager _shortcutManager;
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
        ShortcutManager shortcutManager,
        Func<IReadOnlyList<string>> recentWorkspaceProvider,
        Func<IReadOnlyList<string>> recentFileProvider,
        Func<string> currentStyleProvider,
        Func<int> currentZoomProvider,
        Func<string> currentColorThemeProvider,
        Func<bool> followSystemProvider)
    {
        _router = router;
        _shortcutManager = shortcutManager;
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

    /// <summary>
    /// 快捷键变更后重建整个菜单，使菜单项右侧显示的快捷键与当前映射一致。
    /// </summary>
    public void RebuildMenu()
    {
        if (_window != 0)
        {
            Attach(_window);
        }
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

    public void ShowBlockHandleMenu(nint window, Point screenPoint, EditorCommandStatus status)
    {
        var (menu, commands) = BuildBlockHandleMenu(status);
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

    internal static AppCommand[] GetBlockHandleCommands(EditorCommandStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.FootnoteDefinitionLabel))
        {
            return FootnoteBlockHandleCommands;
        }

        return status.InTable ? TableBlockHandleCommands : BlockHandleCommands;
    }

    private static (nint Menu, AppCommand[] Commands) BuildBlockHandleMenu(EditorCommandStatus status)
    {
        if (!string.IsNullOrWhiteSpace(status.FootnoteDefinitionLabel))
        {
            var menu = CreateMenu(true);
            try
            {
                AppendFootnoteCommands(menu);
                return (menu, FootnoteBlockHandleCommands);
            }
            catch
            {
                NativeMethods.DestroyMenu(menu);
                throw;
            }
        }

        if (status.InTable)
        {
            var menu = CreateMenu(true);
            try
            {
                AppendTableCommands(menu, includeClipboard: false);
                return (menu, TableBlockHandleCommands);
            }
            catch
            {
                NativeMethods.DestroyMenu(menu);
                throw;
            }
        }

        return (BuildBlockHandleMenu(), BlockHandleCommands);
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
            AppendSeparator(lists);
            AppendCommand(lists, AppCommand.IncreaseListIndent, Loc.Get("menu.paragraph.increaseIndent"));
            AppendCommand(lists, AppCommand.DecreaseListIndent, Loc.Get("menu.paragraph.decreaseIndent"));
            AppendPopup(menu, Loc.Get("menu.paragraph.list"), lists);

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InsertHorizontalRule, Loc.Get("menu.paragraph.horizontalRule"));
            AppendCommand(menu, AppCommand.InsertFootnote, Loc.Get("menu.paragraph.insertFootnote"));
            AppendCommand(menu, AppCommand.InsertTable, Loc.Get("menu.paragraph.insertTable"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InsertLineBefore, Loc.Get("menu.paragraph.insertLineBefore"));
            AppendCommand(menu, AppCommand.InsertLineAfter, Loc.Get("menu.paragraph.insertLineAfter"));
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
            var newMenu = CreateMenu(true);
            AppendMainMenuCommand(newMenu, AppCommand.NewDocument, Loc.Get("menu.file.newMarkdown"));
            AppendMainMenuCommand(newMenu, AppCommand.NewPlainTextDocument, Loc.Get("menu.file.newText"));
            AppendPopup(menu, Loc.Get("menu.file.new"), newMenu);
            AppendMainMenuCommand(menu, AppCommand.NewWindow, Loc.Get("menu.file.newWindow"));
            AppendMainMenuCommand(menu, AppCommand.OpenDocument, Loc.Get("menu.file.open"));
            AppendMainMenuCommand(menu, AppCommand.OpenDocumentReadOnly, Loc.Get("menu.file.openReadOnly"));
            AppendMainMenuCommand(menu, AppCommand.OpenDocumentInNewWindow, Loc.Get("menu.file.openInNewWindow"));
            AppendMainMenuCommand(menu, AppCommand.OpenFolder, Loc.Get("menu.file.openFolder"));

            _recentWorkspaceMenu = CreateMenu(true);
            AppendDisabledText(_recentWorkspaceMenu, Loc.Get("menu.file.noRecentItems"));
            AppendPopup(menu, Loc.Get("menu.file.recentItems"), _recentWorkspaceMenu);

            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.SaveDocument, Loc.Get("menu.file.save"));
            AppendMainMenuCommand(menu, AppCommand.SaveDocumentAs, Loc.Get("menu.file.saveAs"));
            var exportMenu = CreateMenu(true);

            AppendMainMenuCommand(exportMenu, AppCommand.ExportPdf, Loc.Get("menu.file.exportPdf"));
            AppendMainMenuCommand(exportMenu, AppCommand.ExportHtml, Loc.Get("menu.file.exportHtml"));
            AppendSeparator(exportMenu);
            AppendMainMenuCommand(exportMenu, AppCommand.ExportWithLastSettings, Loc.Get("menu.file.exportLast"));
            AppendPopup(menu, Loc.Get("menu.file.export"), exportMenu);
            AppendMainMenuCommand(menu, AppCommand.Print, Loc.Get("menu.file.print"));
            AppendMainMenuCommand(menu, AppCommand.RecoverUnsavedFiles, Loc.Get("menu.file.recoverUnsaved"));


            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.CloseFolder, Loc.Get("menu.file.closeFolder"));
            AppendMainMenuCommand(menu, AppCommand.Exit, Loc.Get("menu.file.exit"));
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private nint BuildEditMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendMainMenuCommand(menu, AppCommand.Undo, Loc.Get("menu.edit.undo"));
            AppendMainMenuCommand(menu, AppCommand.Redo, Loc.Get("menu.edit.redo"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.Cut, Loc.Get("menu.edit.cut"));
            AppendMainMenuCommand(menu, AppCommand.Copy, Loc.Get("menu.edit.copy"));

            var copyPasteAs = CreateMenu(true);
            AppendMainMenuCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("menu.edit.copyMarkdown"));
            AppendMainMenuCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("menu.edit.copyPlainText"));
            AppendSeparator(copyPasteAs);
            AppendMainMenuCommand(copyPasteAs, AppCommand.PastePlainText, Loc.Get("menu.edit.pastePlainText"));
            AppendPopup(menu, Loc.Get("menu.edit.copyPasteAs"), copyPasteAs);

            AppendMainMenuCommand(menu, AppCommand.Paste, Loc.Get("menu.edit.paste"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.Find, Loc.Get("menu.edit.find"));
            AppendMainMenuCommand(menu, AppCommand.Replace, Loc.Get("menu.edit.replace"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.SelectAll, Loc.Get("menu.edit.selectAll"));
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
            if (status.ReadOnly)
            {
                AppendReadOnlyContextMenu(menu);
                commands.AddRange([
                    AppCommand.Cut, AppCommand.Copy, AppCommand.CopyMarkdown, AppCommand.CopyPlainText,
                    AppCommand.PastePlainText, AppCommand.Paste, AppCommand.SelectAll]);
                return (menu, commands.ToArray());
            }

            if (status.SourceMode)
            {
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                commands.AddRange([AppCommand.Cut, AppCommand.Copy, AppCommand.Paste, AppCommand.SelectAll]);
            }
            else if (!string.IsNullOrWhiteSpace(status.FootnoteDefinitionLabel))
            {
                AppendFootnoteCommands(menu);
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));

                var copyPasteAs = CreateMenu(true);
                AppendCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("contextMenu.copyMarkdown"));
                AppendCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("contextMenu.copyPlainText"));
                AppendSeparator(copyPasteAs);
                AppendCommand(copyPasteAs, AppCommand.PastePlainText, Loc.Get("contextMenu.pastePlainText"));
                AppendPopup(menu, Loc.Get("contextMenu.copyPasteAs"), copyPasteAs);

                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                commands.AddRange([
                    AppCommand.GoToFootnoteReference, AppCommand.ResetFootnoteLabel,
                    AppCommand.ClearFootnoteReferences, AppCommand.DeleteFootnote,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.CopyMarkdown, AppCommand.CopyPlainText,
                    AppCommand.Paste, AppCommand.PastePlainText, AppCommand.SelectAll]);
            }
            else if (status.InTable)
            {
                AppendTableCommands(menu, includeClipboard: true);
                commands.AddRange([
                    AppCommand.EditTableCaption,
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
            else if (status.MermaidSelected)
            {
                AppendCommand(menu, AppCommand.EditMermaid, Loc.Get("contextMenu.mermaid.edit"));
                AppendCommand(menu, AppCommand.RerenderMermaid, Loc.Get("contextMenu.mermaid.rerender"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.DeleteMermaid, Loc.Get("contextMenu.mermaid.delete"));
                commands.AddRange([AppCommand.EditMermaid, AppCommand.RerenderMermaid, AppCommand.DeleteMermaid]);
            }
            else if (status.ImageSelected)
            {
                AppendCommand(menu, AppCommand.ChangeImage, Loc.Get("contextMenu.image.change"));
                AppendCommand(menu, AppCommand.EditImageCaption, Loc.Get("contextMenu.image.caption"));

                AppendPopup(menu, Loc.Get("contextMenu.image.resize"), BuildResizeImageSubmenu());

                AppendCommand(menu, AppCommand.RotateImageClockwise, Loc.Get("contextMenu.image.rotate"));
                AppendCommand(menu, AppCommand.SaveImageAs, Loc.Get("contextMenu.image.saveAs"));
                AppendSeparator(menu);

                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                commands.AddRange([
                    AppCommand.ChangeImage, AppCommand.EditImageCaption, AppCommand.ResizeImage100, AppCommand.ResizeImage50,
                    AppCommand.ResizeImage75, AppCommand.ResizeImage90, AppCommand.RotateImageClockwise,
                    AppCommand.SaveImageAs, AppCommand.Cut, AppCommand.Copy, AppCommand.Paste]);
            }
            else if (status.CodeBlock)
            {
                AppendCommand(menu, AppCommand.DeclareCodeLanguage, Loc.Get("contextMenu.code.declareLanguage"));
                AppendCommand(menu, AppCommand.CopyCodeBlock, Loc.Get("contextMenu.code.copyBlock"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.ExitCode, Loc.Get("contextMenu.exitCode"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
                AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                commands.AddRange([
                    AppCommand.DeclareCodeLanguage, AppCommand.CopyCodeBlock, AppCommand.ExitCode,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.Paste]);
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

                var copyPasteAs = CreateMenu(true);
                AppendCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("contextMenu.copyMarkdown"));
                AppendCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("contextMenu.copyPlainText"));
                AppendSeparator(copyPasteAs);
                AppendCommand(copyPasteAs, AppCommand.PastePlainText, Loc.Get("contextMenu.pastePlainText"));
                AppendPopup(menu, Loc.Get("contextMenu.copyPasteAs"), copyPasteAs);

                AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
                AppendSeparator(menu);
                AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
                commands.AddRange(BlockHandleCommands);
                commands.AddRange([
                    AppCommand.FormatPainter,
                    AppCommand.Cut, AppCommand.Copy, AppCommand.CopyMarkdown, AppCommand.CopyPlainText,
                    AppCommand.Paste, AppCommand.PastePlainText, AppCommand.SelectAll]);
            }

            return (menu, commands.ToArray());
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private static void AppendFootnoteCommands(nint menu)
    {
        AppendCommand(menu, AppCommand.GoToFootnoteReference, Loc.Get("contextMenu.footnote.goToReference"));
        AppendCommand(menu, AppCommand.ResetFootnoteLabel, Loc.Get("contextMenu.footnote.resetLabel"));
        AppendCommand(menu, AppCommand.ClearFootnoteReferences, Loc.Get("contextMenu.footnote.clearReferences"));
        AppendCommand(menu, AppCommand.DeleteFootnote, Loc.Get("contextMenu.footnote.delete"));
    }

    private static void AppendTableCommands(nint menu, bool includeClipboard)
    {
        AppendCommand(menu, AppCommand.EditTableCaption, Loc.Get("contextMenu.table.caption"));
        AppendSeparator(menu);

        AppendCommand(menu, AppCommand.AddTableRowBefore, Loc.Get("menu.paragraph.addRowAbove"));
        AppendCommand(menu, AppCommand.AddTableRowAfter, Loc.Get("menu.paragraph.addRowBelow"));
        AppendCommand(menu, AppCommand.DeleteTableRow, Loc.Get("menu.paragraph.deleteRow"));
        AppendSeparator(menu);

        AppendCommand(menu, AppCommand.AddTableColumnBefore, Loc.Get("menu.paragraph.addColumnLeft"));
        AppendCommand(menu, AppCommand.AddTableColumnAfter, Loc.Get("menu.paragraph.addColumnRight"));
        AppendCommand(menu, AppCommand.DeleteTableColumn, Loc.Get("menu.paragraph.deleteColumn"));
        AppendSeparator(menu);

        var align = CreateMenu(true);
        AppendCommand(align, AppCommand.AlignTableLeft, Loc.Get("menu.paragraph.alignLeft"));
        AppendCommand(align, AppCommand.AlignTableCenter, Loc.Get("menu.paragraph.alignCenter"));
        AppendCommand(align, AppCommand.AlignTableRight, Loc.Get("menu.paragraph.alignRight"));
        AppendPopup(menu, Loc.Get("contextMenu.table.align"), align);
        AppendSeparator(menu);

        if (includeClipboard)
        {
            AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
            AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));
            AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
            AppendSeparator(menu);
        }

        AppendCommand(menu, AppCommand.DeleteTable, Loc.Get("menu.paragraph.deleteTable"));
    }

    private static void AppendReadOnlyContextMenu(nint menu)
    {
        AppendCommand(menu, AppCommand.Cut, Loc.Get("contextMenu.cut"));
        AppendCommand(menu, AppCommand.Copy, Loc.Get("contextMenu.copy"));

        var copyPasteAs = CreateMenu(true);
        AppendCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("contextMenu.copyMarkdown"));
        AppendCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("contextMenu.copyPlainText"));
        AppendSeparator(copyPasteAs);
        AppendCommand(copyPasteAs, AppCommand.PastePlainText, Loc.Get("contextMenu.pastePlainText"));
        AppendPopup(menu, Loc.Get("contextMenu.copyPasteAs"), copyPasteAs);

        AppendCommand(menu, AppCommand.Paste, Loc.Get("contextMenu.paste"));
        AppendSeparator(menu);
        AppendCommand(menu, AppCommand.SelectAll, Loc.Get("contextMenu.selectAll"));
    }

    private nint BuildParagraphMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendMainMenuCommand(menu, AppCommand.SetParagraph, Loc.Get("menu.paragraph.paragraph"));

            var headings = CreateMenu(true);
            AppendMainMenuCommand(headings, AppCommand.SetHeading1, Loc.Get("menu.paragraph.heading1"));
            AppendMainMenuCommand(headings, AppCommand.SetHeading2, Loc.Get("menu.paragraph.heading2"));
            AppendMainMenuCommand(headings, AppCommand.SetHeading3, Loc.Get("menu.paragraph.heading3"));
            AppendMainMenuCommand(headings, AppCommand.SetHeading4, Loc.Get("menu.paragraph.heading4"));
            AppendMainMenuCommand(headings, AppCommand.SetHeading5, Loc.Get("menu.paragraph.heading5"));
            AppendMainMenuCommand(headings, AppCommand.SetHeading6, Loc.Get("menu.paragraph.heading6"));
            AppendPopup(menu, Loc.Get("menu.paragraph.heading"), headings);

            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.PromoteHeading, Loc.Get("menu.paragraph.promoteHeading"));
            AppendMainMenuCommand(menu, AppCommand.DemoteHeading, Loc.Get("menu.paragraph.demoteHeading"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.ToggleQuote, Loc.Get("menu.paragraph.quote"));
            AppendMainMenuCommand(menu, AppCommand.InsertMathBlock, Loc.Get("menu.paragraph.insertMathBlock"));
            AppendMainMenuCommand(menu, AppCommand.ToggleCodeBlock, Loc.Get("menu.paragraph.codeBlock"));
            AppendMainMenuCommand(menu, AppCommand.InsertHorizontalRule, Loc.Get("menu.paragraph.horizontalRule"));
            AppendSeparator(menu);

            var lists = CreateMenu(true);
            AppendMainMenuCommand(lists, AppCommand.ToggleBulletList, Loc.Get("menu.paragraph.bulletList"));
            AppendMainMenuCommand(lists, AppCommand.ToggleOrderedList, Loc.Get("menu.paragraph.orderedList"));
            AppendMainMenuCommand(lists, AppCommand.ToggleTaskList, Loc.Get("menu.paragraph.taskList"));
            AppendSeparator(lists);
            AppendMainMenuCommand(lists, AppCommand.IncreaseListIndent, Loc.Get("menu.paragraph.increaseIndent"));
            AppendMainMenuCommand(lists, AppCommand.DecreaseListIndent, Loc.Get("menu.paragraph.decreaseIndent"));
            AppendPopup(menu, Loc.Get("menu.paragraph.list"), lists);

            var table = CreateMenu(true);
            AppendMainMenuCommand(table, AppCommand.InsertTable, Loc.Get("menu.paragraph.insertTable"));
            AppendSeparator(table);
            AppendMainMenuCommand(table, AppCommand.AddTableRowBefore, Loc.Get("menu.paragraph.addRowAbove"));
            AppendMainMenuCommand(table, AppCommand.AddTableRowAfter, Loc.Get("menu.paragraph.addRowBelow"));
            AppendMainMenuCommand(table, AppCommand.DeleteTableRow, Loc.Get("menu.paragraph.deleteRow"));
            AppendSeparator(table);
            AppendMainMenuCommand(table, AppCommand.AddTableColumnBefore, Loc.Get("menu.paragraph.addColumnLeft"));
            AppendMainMenuCommand(table, AppCommand.AddTableColumnAfter, Loc.Get("menu.paragraph.addColumnRight"));
            AppendMainMenuCommand(table, AppCommand.DeleteTableColumn, Loc.Get("menu.paragraph.deleteColumn"));
            AppendSeparator(table);
            AppendMainMenuCommand(table, AppCommand.AlignTableLeft, Loc.Get("menu.paragraph.alignLeft"));
            AppendMainMenuCommand(table, AppCommand.AlignTableCenter, Loc.Get("menu.paragraph.alignCenter"));
            AppendMainMenuCommand(table, AppCommand.AlignTableRight, Loc.Get("menu.paragraph.alignRight"));
            AppendSeparator(table);
            AppendMainMenuCommand(table, AppCommand.DeleteTable, Loc.Get("menu.paragraph.deleteTable"));
            AppendPopup(menu, Loc.Get("menu.paragraph.table"), table);

            var diagram = CreateMenu(true);
            AppendMainMenuCommand(diagram, AppCommand.InsertMermaid, Loc.Get("menu.paragraph.insertMermaid"));
            AppendSeparator(diagram);
            AppendMainMenuCommand(diagram, AppCommand.RerenderAllMermaid, Loc.Get("menu.paragraph.rerenderAllMermaid"));
            AppendPopup(menu, Loc.Get("menu.paragraph.diagram"), diagram);

            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.InsertFootnote, Loc.Get("menu.paragraph.insertFootnote"));
            AppendSeparator(menu);

            AppendMainMenuCommand(menu, AppCommand.InsertLineBefore, Loc.Get("menu.paragraph.insertLineBefore"));
            AppendMainMenuCommand(menu, AppCommand.InsertLineAfter, Loc.Get("menu.paragraph.insertLineAfter"));
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
        AppendMainMenuCommand(menu, AppCommand.ToggleBold, Loc.Get("menu.format.bold"));
        AppendMainMenuCommand(menu, AppCommand.ToggleItalic, Loc.Get("menu.format.italic"));
        AppendMainMenuCommand(menu, AppCommand.ToggleUnderline, Loc.Get("menu.format.underline"));
        AppendMainMenuCommand(menu, AppCommand.ToggleStrike, Loc.Get("menu.format.strikethrough"));
        AppendSeparator(menu);
        AppendMainMenuCommand(menu, AppCommand.ToggleInlineCode, Loc.Get("menu.format.inlineCode"));
        AppendMainMenuCommand(menu, AppCommand.InsertMathInline, Loc.Get("menu.format.insertMathInline"));
        AppendSeparator(menu);
        AppendMainMenuCommand(menu, AppCommand.InsertLink, Loc.Get("menu.format.insertLink"));
        AppendPopup(menu, Loc.Get("menu.format.image"), BuildImageSubmenu());
        AppendSeparator(menu);
        AppendMainMenuCommand(menu, AppCommand.FormatPainter, Loc.Get("menu.format.formatPainter"));
        AppendSeparator(menu);
        AppendMainMenuCommand(menu, AppCommand.ClearFormat, Loc.Get("menu.format.clearFormat"));
        return menu;
    }

    private nint BuildImageSubmenu()
    {
        var image = CreateMenu(true);
        AppendMainMenuCommand(image, AppCommand.InsertImage, Loc.Get("menu.format.insertImage"));
        AppendMainMenuCommand(image, AppCommand.InsertImageFromUrl, Loc.Get("menu.format.insertImageFromUrl"));
        AppendSeparator(image);
        AppendMainMenuCommand(image, AppCommand.RotateImageClockwise, Loc.Get("menu.format.rotateImageClockwise"));
        AppendPopup(image, Loc.Get("menu.format.resizeImage"), BuildResizeImageSubmenu());
        AppendMainMenuCommand(image, AppCommand.SaveImageAs, Loc.Get("menu.format.saveImageAs"));
        return image;
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

    private nint BuildViewMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendMainMenuCommand(menu, AppCommand.ToggleSidebar, Loc.Get("menu.view.toggleSidebar"));
            var sidebarSettings = CreateMenu(true);
            AppendMainMenuCommand(sidebarSettings, AppCommand.SwitchToWorkspace, Loc.Get("menu.view.workspace"));
            AppendMainMenuCommand(sidebarSettings, AppCommand.SwitchToOutline, Loc.Get("menu.view.outline"));
            AppendSeparator(sidebarSettings);
            AppendMainMenuCommand(
                sidebarSettings,
                AppCommand.UseIndependentOutlineSidebar,
                Loc.Get("menu.view.independentOutlineSidebar"));
            AppendSeparator(sidebarSettings);
            AppendMainMenuCommand(sidebarSettings, AppCommand.ViewTree, Loc.Get("menu.view.treeView"));
            AppendMainMenuCommand(sidebarSettings, AppCommand.ViewList, Loc.Get("menu.view.documentList"));
            AppendPopup(menu, Loc.Get("menu.view.sidebarSettings"), sidebarSettings);
            AppendMainMenuCommand(menu, AppCommand.ShowStatusBar, Loc.Get("menu.view.showStatusBar"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowCodeHighlight, Loc.Get("menu.view.showCodeHighlight"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.ToggleSourceMode, Loc.Get("menu.view.sourceMode"));
            AppendMainMenuCommand(menu, AppCommand.ToggleFocusMode, Loc.Get("menu.view.focusMode"));
            AppendSeparator(menu);
            _zoomMenu = CreateMenu(true);
            AppendPopup(menu, Loc.Get("menu.appearance.zoom"), _zoomMenu);
            RefreshZoomMenu();
            AppendCommand(menu, AppCommand.ZoomIn, Loc.Get("menu.appearance.zoomIn"));
            AppendCommand(menu, AppCommand.ZoomOut, Loc.Get("menu.appearance.zoomOut"));
            AppendCommand(menu, AppCommand.ZoomReset, Loc.Get("menu.appearance.zoomReset"));
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
            AppendCommand(menu, AppCommand.CheckForUpdates, Loc.Get("menu.help.checkForUpdates"));
            AppendSeparator(menu);
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

    /// <summary>
    /// 主菜单专用：若命令可自定义快捷键，则把本地化文本按首个 \t 拆成标签，
    /// 再拼接当前生效的快捷键（已清除则只显示标签）。上下文菜单不使用此方法。
    /// </summary>
    private void AppendMainMenuCommand(nint menu, AppCommand command, string rawText)
    {
        if (_shortcutManager.IsRemappable(command))
        {
            var label = rawText;
            var tabIndex = rawText.IndexOf('\t');
            if (tabIndex >= 0)
            {
                label = rawText[..tabIndex];
            }

            var shortcut = _shortcutManager.GetShortcutText(command);
            Append(menu, NativeMethods.MfString, (nuint)command, shortcut is null ? label : $"{label}\t{shortcut}");
        }
        else
        {
            Append(menu, NativeMethods.MfString, (nuint)command, rawText);
        }
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
