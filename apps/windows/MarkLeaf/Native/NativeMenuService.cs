using System.ComponentModel;
using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native.ModernMenus;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;

namespace MarkLeaf.Native;

internal sealed class NativeMenuService : IDisposable
{
    internal sealed record DocumentTabMenuEntry(
        string Title,
        string? FullPath = null,
        bool IsDirty = false);

    private EditorMenuSession? _editorMenuSession;
    private CancellationTokenSource? _editorMenuCancellation;
    private long _editorMenuId;
    private Task _editorMenuDisplay = Task.CompletedTask;
    private EditorContextMenu? _editorContextMenu;
    private readonly CommandRouter _router;
    private readonly ShortcutManager _shortcutManager;
    private readonly Func<IReadOnlyList<string>> _recentWorkspaceProvider;
    private readonly Func<IReadOnlyList<string>> _recentFileProvider;
    private readonly Func<string> _currentStyleProvider;
    private readonly Func<int> _currentZoomProvider;
    private readonly Func<string> _currentColorThemeProvider;
    private readonly Func<bool> _followSystemProvider;
    private readonly Func<bool> _showKeyboardShortcutsProvider;
    private readonly Func<bool> _showMnemonicsProvider;
    private readonly Func<string> _uiLanguageProvider;
    private readonly Func<IReadOnlyList<DocumentTabMenuEntry>> _openDocumentTabProvider;
    private readonly Func<int> _activeDocumentTabProvider;
    private readonly Dictionary<uint, string> _styleCommandIds = new();
    private readonly Dictionary<uint, int> _zoomCommandIds = new();
    private readonly Dictionary<uint, string> _colorCommandIds = new();
    private nint _menu;
    private nint _window;
    private nint _recentWorkspaceMenu;
    private nint _styleMenu;
    private nint _zoomMenu;
    private nint _colorMenu;
    private nint _documentTabMenu;
    private readonly Dictionary<uint, int> _documentTabCommandIds = new();
    private readonly Action<string>? _diagnosticSink;
    private NativeFormatMenuSession? _formatMenuSession;

    internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

    private void Diagnose(string line) => _diagnosticSink?.Invoke(line);

    public NativeMenuService(
        CommandRouter router,
        ShortcutManager shortcutManager,
        Func<IReadOnlyList<string>> recentWorkspaceProvider,
        Func<IReadOnlyList<string>> recentFileProvider,
        Func<string> currentStyleProvider,
        Func<int> currentZoomProvider,
        Func<string> currentColorThemeProvider,
        Func<bool> followSystemProvider,
        Func<bool> showKeyboardShortcutsProvider,
        Func<bool> showMnemonicsProvider,
        Func<string> uiLanguageProvider,
        Func<IReadOnlyList<string>> openDocumentTabProvider,
        Func<int> activeDocumentTabProvider,
        Action<string>? diagnosticSink = null)
        : this(
            router,
            shortcutManager,
            recentWorkspaceProvider,
            recentFileProvider,
            currentStyleProvider,
            currentZoomProvider,
            currentColorThemeProvider,
            followSystemProvider,
            showKeyboardShortcutsProvider,
            showMnemonicsProvider,
            uiLanguageProvider,
            () => openDocumentTabProvider()
                .Select(title => new DocumentTabMenuEntry(title))
                .ToArray(),
            activeDocumentTabProvider,
            diagnosticSink)
    {
    }

    public NativeMenuService(
        CommandRouter router,
        ShortcutManager shortcutManager,
        Func<IReadOnlyList<string>> recentWorkspaceProvider,
        Func<IReadOnlyList<string>> recentFileProvider,
        Func<string> currentStyleProvider,
        Func<int> currentZoomProvider,
        Func<string> currentColorThemeProvider,
        Func<bool> followSystemProvider,
        Func<bool> showKeyboardShortcutsProvider,
        Func<bool> showMnemonicsProvider,
        Func<string> uiLanguageProvider,
        Func<IReadOnlyList<DocumentTabMenuEntry>> openDocumentTabProvider,
        Func<int> activeDocumentTabProvider,
        Action<string>? diagnosticSink = null)
    {
        _router = router;
        _shortcutManager = shortcutManager;
        _recentWorkspaceProvider = recentWorkspaceProvider;
        _recentFileProvider = recentFileProvider;
        _currentStyleProvider = currentStyleProvider;
        _currentZoomProvider = currentZoomProvider;
        _currentColorThemeProvider = currentColorThemeProvider;
        _followSystemProvider = followSystemProvider;
        _showKeyboardShortcutsProvider = showKeyboardShortcutsProvider;
        _showMnemonicsProvider = showMnemonicsProvider;
        _uiLanguageProvider = uiLanguageProvider;
        _openDocumentTabProvider = openDocumentTabProvider;
        _activeDocumentTabProvider = activeDocumentTabProvider;
        _diagnosticSink = diagnosticSink;
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
        RefreshDocumentTabMenu();

        RefreshMenuBarPopupAvailability(_menu);
        EnableRecentItemsPopup(_menu);

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
        _documentTabMenu = 0;
        _documentTabCommandIds.Clear();
    }

    public void Dispose()
    {
        CancelEditorContextMenu();
        Detach();
    }

    public void CancelEditorContextMenu()
    {
        _editorMenuSession?.Cancel();
        _editorMenuCancellation?.Cancel();
        _editorContextMenu?.Close(ToolStripDropDownCloseReason.CloseCalled);
        _editorMenuId++;
    }

    internal EditorMenuModel BuildEditorMenuModel(EditorCommandStatus status, bool inlineCommonActions = false) =>
        new EditorMenuModelBuilder(_router.GetState, command => _shortcutManager.GetShortcutText(command) ?? "").Build(status, inlineCommonActions);

    internal EditorMenuModel BuildBlockMenuModel(EditorCommandStatus status) =>
        new EditorMenuModelBuilder(_router.GetState, command => _shortcutManager.GetShortcutText(command) ?? "").BuildBlock(status);

    public async Task<AppCommand?> ShowHostPopupMenuAsync(
        Form owner, Point screenPoint, int dpi, bool darkMode, EditorMenuModel model)
    {
        CancelEditorContextMenu();
        var previousDisplay = _editorMenuDisplay;
        var id = ++_editorMenuId;
        try { await previousDisplay; }
        catch (Exception error) { Diagnose($"Previous native menu failed: {error.Message}"); }
        if (id != _editorMenuId || owner.IsDisposed) return null;

        var states = Flatten(model.Secondary)
            .Where(x => x.Command.HasValue)
            .GroupBy(x => x.Command!.Value)
            .ToDictionary(x => x.Key, x => x.First().State);
        var selected = new TaskCompletionSource<AppCommand?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        EditorMenuSession? session = null;
        session = new EditorMenuSession(
            id,
            command => states.TryGetValue(command, out var state) ? state : new CommandState(false),
            command =>
            {
                selected.TrySetResult(command);
                return Task.CompletedTask;
            },
            _ => { },
            () => selected.TrySetResult(null));
        _editorMenuSession = session;
        _editorMenuCancellation = cancellation;
        var displayFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _editorMenuDisplay = displayFinished.Task;
        try
        {
            using var menu = new EditorContextMenu(model, dpi, darkMode, _showKeyboardShortcutsProvider(), session);
            _editorContextMenu = menu;
            menu.Closed += (_, _) => session.Closed(callback =>
            {
                if (!owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke(callback);
                else session.Cancel();
            });
            menu.Show(owner, owner.PointToClient(screenPoint));
            await session.Completion;
            if (menu.Visible) await menu.Dismissed;
            return await selected.Task;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        finally
        {
            selected.TrySetResult(null);
            session.Cancel();
            displayFinished.TrySetResult();
            if (ReferenceEquals(_editorMenuSession, session))
            {
                _editorMenuSession = null;
                _editorMenuCancellation = null;
                _editorContextMenu = null;
            }
        }
    }

    /// <summary>
    /// 用自绘菜单渲染一个顶级菜单（文件/编辑/…）。菜单内容仍取自现有构建器，
    /// 返回选中的命令 ID；鼠标越过其它顶级标签时返回切换目标。
    /// </summary>
    internal async Task<(int? CommandId, int HoveredMenuIndex)> ShowSelfDrawnTopLevelMenuAsync(
        Form owner, int menuIndex, Point screenPoint, int dpi, bool darkMode,
        IReadOnlyList<Rectangle> menuButtonBounds)
    {
        try
        {
            CancelEditorContextMenu();
            var id = ++_editorMenuId;
            var model = BuildSelfDrawnTopLevelMenu(menuIndex);
            if (model.Secondary.Count == 0) return (null, -1);

            var chosen = new TaskCompletionSource<uint?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var hovered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new EditorMenuSession(id, _ => new CommandState(true), _ => Task.CompletedTask,
                _ => { }, () => { });
            using var menu = new EditorContextMenu(model, dpi, darkMode, _showKeyboardShortcutsProvider(), session,
                rawCommandSelected: commandId => chosen.TrySetResult(commandId));
            _editorContextMenu = menu;
            menu.Closed += (_, _) =>
            {
                if (!owner.IsDisposed && owner.IsHandleCreated)
                    owner.BeginInvoke(() => chosen.TrySetResult(null));
                else
                    chosen.TrySetResult(null);
            };
            using var hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
            hoverTimer.Tick += (_, _) =>
            {
                var cursor = Cursor.Position;
                for (var index = 0; index < menuButtonBounds.Count; index++)
                {
                    if (index == menuIndex || !menuButtonBounds[index].Contains(cursor)) continue;
                    hovered.TrySetResult(index);
                    menu.Close();
                    return;
                }
            };
            hoverTimer.Start();
            menu.Show(owner, owner.PointToClient(screenPoint));
            var completed = await Task.WhenAny(chosen.Task, hovered.Task);
            hoverTimer.Stop();
            if (completed == hovered.Task)
            {
                if (menu.Visible) menu.Close();
                await menu.Dismissed;
                return (null, await hovered.Task);
            }

            var commandId = await chosen.Task;
            if (menu.Visible) await menu.Dismissed;
            return (commandId is null ? null : unchecked((int)commandId.Value), -1);
        }
        catch (Exception error)
        {
            Diagnose($"Self-drawn top-level menu failed: {error.Message}");
            return (null, -1);
        }
    }

    internal EditorMenuModel BuildSelfDrawnTopLevelMenu(int menuIndex)
    {
        var menu = menuIndex switch
        {
            0 => BuildFileMenu(),
            1 => BuildEditMenu(),
            2 => BuildInsertMenu(),
            3 => BuildFormatMenu(),
            4 => BuildViewMenu(),
            5 => BuildHelpMenu(),
            _ => 0,
        };
        if (menu == 0)
        {
            return new EditorMenuModel(Array.Empty<EditorMenuEntry>(), Array.Empty<EditorMenuEntry>());
        }

        try
        {
            ApplyCommandStates(menu);
            RefreshRecentWorkspaces();
            RefreshStyleMenu();
            RefreshZoomMenu();
            RefreshColorMenu();
            RefreshDocumentTabMenu();
            // 自绘模型读取的是 HMENU 的最终状态；子项全部不可用时，容器项也必须先置灰。
            // “最近项目”是全局入口，空态仍需展开显示“无最近项目”。
            RefreshPopupAvailability(menu);
            EnableRecentItemsPopup(menu);
            return MenuBarModelAdapter.Convert(menu);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
            _recentWorkspaceMenu = 0;
            _styleMenu = 0;
            _zoomMenu = 0;
            _colorMenu = 0;
            _documentTabMenu = 0;
        }
    }

    private void ApplyCommandStates(nint menu)
    {
        if (menu == 0) return;

        foreach (var command in Enum.GetValues<AppCommand>())
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

        var count = NativeMethods.GetMenuItemCount(menu);
        for (var index = 0; index < count; index++)
        {
            var submenu = NativeMethods.GetSubMenu(menu, index);
            if (submenu != 0) ApplyCommandStates(submenu);
        }
    }

    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> entries) =>
        entries.SelectMany(x => new[] { x }.Concat(Flatten(x.Children)));

    public async Task ShowEditorContextMenuAsync(Form owner, EditorContextMenuRequest request,
        Func<double, double, Point> editorPointToScreen, int dpi, bool darkMode,
        Action<string?> previewFormat, EditorCommandStatus status, Action end,
        Func<AppCommand, Func<bool>, Task> execute, bool inlineCommonActions = false)
    {
        CancelEditorContextMenu();
        var previousDisplay = _editorMenuDisplay;
        var id = ++_editorMenuId;
        try { await previousDisplay; }
        catch (Exception error) { Diagnose($"Previous native menu failed: {error.Message}"); }
        if (id != _editorMenuId || owner.IsDisposed) return;
        using var cancellation = new CancellationTokenSource();
        EditorMenuSession? session = null;
        session = new EditorMenuSession(id, _router.GetState,
            command => id == _editorMenuId ? execute(command, () => id == _editorMenuId && session!.IsCurrent) : Task.CompletedTask,
            command => { if (id == _editorMenuId) previewFormat(command); },
            () => { if (id == _editorMenuId) end(); });
        _editorMenuSession = session;
        _editorMenuCancellation = cancellation;
        var displayFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _editorMenuDisplay = displayFinished.Task;
        try
        {
            using var menu = new EditorContextMenu(BuildEditorMenuModel(status, inlineCommonActions), dpi, darkMode,
                _showKeyboardShortcutsProvider(), session);
            _editorContextMenu = menu;
            menu.Closed += (_, _) => session.Closed(callback =>
            {
                if (!owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke(callback);
                else session.Cancel();
            });
            menu.Show(owner, owner.PointToClient(editorPointToScreen(request.ClientX, request.ClientY)));
            await session.Completion;
            // A command can finish before the short closing fade. Keep the
            // popup alive until Closed, and serialize a subsequent right click.
            if (menu.Visible) await menu.Dismissed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            session.Cancel();
            displayFinished.TrySetResult();
            if (ReferenceEquals(_editorMenuSession, session))
            {
                _editorMenuSession = null;
                _editorMenuCancellation = null;
                _editorContextMenu = null;
            }
        }
    }

    public async Task ShowBlockHandleMenuAsync(Form owner, Point screenPoint, int dpi, bool darkMode,
        EditorCommandStatus status, Action end)
    {
        CancelEditorContextMenu();
        var previousDisplay = _editorMenuDisplay;
        var id = ++_editorMenuId;
        try { await previousDisplay; }
        catch (Exception error) { Diagnose($"Previous native menu failed: {error.Message}"); }
        if (id != _editorMenuId || owner.IsDisposed) return;

        using var cancellation = new CancellationTokenSource();
        EditorMenuSession? session = null;
        session = new EditorMenuSession(
            id,
            _router.GetState,
            command =>
            {
                if (id == _editorMenuId) _router.ExecuteIfEnabled(command);
                return Task.CompletedTask;
            },
            _ => { },
            () => { if (id == _editorMenuId) end(); });
        _editorMenuSession = session;
        _editorMenuCancellation = cancellation;
        var displayFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _editorMenuDisplay = displayFinished.Task;
        try
        {
            using var menu = new EditorContextMenu(BuildBlockMenuModel(status), dpi, darkMode,
                _showKeyboardShortcutsProvider(), session);
            _editorContextMenu = menu;
            menu.Closed += (_, _) => session.Closed(callback =>
            {
                if (!owner.IsDisposed && owner.IsHandleCreated) owner.BeginInvoke(callback);
                else session.Cancel();
            });
            menu.Show(owner, owner.PointToClient(screenPoint));
            await session.Completion;
            if (menu.Visible) await menu.Dismissed;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            session.Cancel();
            displayFinished.TrySetResult();
            if (ReferenceEquals(_editorMenuSession, session))
            {
                _editorMenuSession = null;
                _editorMenuCancellation = null;
                _editorContextMenu = null;
            }
        }
    }

    public int? ShowFullScreenMainMenu(nint window, Point screenPoint)
    {
        var menu = BuildFullScreenMainMenu();
        try
        {
            foreach (var command in Enum.GetValues<AppCommand>())
            {
                var state = _router.GetState(command);
                NativeMethods.EnableMenuItem(
                    menu,
                    (uint)command,
                    NativeMethods.MfByCommand
                        | (state.IsEnabled ? NativeMethods.MfEnabled : NativeMethods.MfGrayed));
                NativeMethods.CheckMenuItem(
                    menu,
                    (uint)command,
                    NativeMethods.MfByCommand
                        | (state.IsChecked ? NativeMethods.MfChecked : NativeMethods.MfUnchecked));
            }
            RefreshRecentWorkspaces();
            RefreshStyleMenu();
            RefreshZoomMenu();
            RefreshColorMenu();
            RefreshDocumentTabMenu();
            RefreshMenuBarPopupAvailability(menu);

            NativeMethods.SetForegroundWindow(window);
            var selected = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmLeftButton | NativeMethods.TpmReturnCommand,
                screenPoint.X,
                screenPoint.Y,
                window,
                0);
            NativeMethods.PostMessage(window, NativeMethods.WmNull, 0, 0);
            return selected == 0 ? null : unchecked((int)selected);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
            _recentWorkspaceMenu = 0;
            _styleMenu = 0;
            _zoomMenu = 0;
            _colorMenu = 0;
            _documentTabMenu = 0;
        }
    }

    public int? ShowTopLevelMainMenu(
        nint window,
        Point screenPoint,
        int menuIndex,
        IReadOnlyList<Rectangle> menuButtonBounds,
        out int nextMenuIndex)
    {
        nextMenuIndex = -1;
        var menu = menuIndex switch
        {
            0 => BuildFileMenu(),
            1 => BuildEditMenu(),
            2 => BuildInsertMenu(),
            3 => BuildFormatMenu(),
            4 => BuildViewMenu(),
            5 => BuildHelpMenu(),
            _ => 0,
        };
        if (menu == 0) return null;

        try
        {
            ApplyCommandStates(menu);
            RefreshRecentWorkspaces();
            RefreshStyleMenu();
            RefreshZoomMenu();
            RefreshColorMenu();
            RefreshDocumentTabMenu();
            RefreshPopupAvailability(menu);
            EnableRecentItemsPopup(menu);

            NativeMethods.SetForegroundWindow(window);
            var hoveredMenuIndex = -1;
            using var hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
            hoverTimer.Tick += (_, _) =>
            {
                var cursor = Cursor.Position;
                for (var index = 0; index < menuButtonBounds.Count; index++)
                {
                    if (index == menuIndex || !menuButtonBounds[index].Contains(cursor)) continue;
                    hoveredMenuIndex = index;
                    hoverTimer.Stop();
                    NativeMethods.EndMenu();
                    break;
                }
            };
            hoverTimer.Start();
            var selected = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmLeftButton | NativeMethods.TpmReturnCommand,
                screenPoint.X,
                screenPoint.Y,
                window,
                0);
            hoverTimer.Stop();
            nextMenuIndex = hoveredMenuIndex;
            NativeMethods.PostMessage(window, NativeMethods.WmNull, 0, 0);
            return selected == 0 ? null : unchecked((int)selected);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
            _recentWorkspaceMenu = 0;
            _styleMenu = 0;
            _zoomMenu = 0;
            _colorMenu = 0;
            _documentTabMenu = 0;
        }
    }

    /// 弹出编辑器右键菜单。
    /// 快捷操作与菜单共享 HMENU，系统统一计算大小与屏幕边缘放置。
    public void ShowEditorContextMenu(
        nint window,
        EditorContextMenuRequest request,
        Func<double, double, Point> editorPointToScreen,
        int dpi,
        bool darkMode,
        Action<string?> previewFormat,
        EditorCommandStatus status,
        Action<AppCommand>? select = null)
    {
        var anchorPoint = editorPointToScreen(request.ClientX, request.ClientY);
        var (menu, commands, ownerDraw) = status.ExpandedSource
            ? BuildExpandedSourceContextMenu()
            : BuildEditorContextMenu(status);
        try
        {
            var quickActions = !status.ReadOnly && !status.SourceMode && !status.ExpandedSource
                && _router.GetState(AppCommand.ToggleBold).IsEnabled;
            _formatMenuSession = new NativeFormatMenuSession(window, menu, dpi, darkMode,
                _router.GetState, previewFormat, Diagnose, ownerDraw, quickActions);
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
                anchorPoint.X,
                anchorPoint.Y,
                window,
                0);
            NativeMethods.PostMessage(window, NativeMethods.WmNull, 0, 0);
            if (_formatMenuSession?.SelectedCommand is { } quickCommand)
            {
                if (select is not null) select(quickCommand); else _router.ExecuteIfEnabled(quickCommand);
            }
            else if (selectedCommand != 0 && selectedCommand != NativeFormatMenuSession.ItemId)
            {
                if (select is not null && CommandCatalog.TryGetById((int)selectedCommand, out var command)) select(command);
                else _router.TryExecuteById((int)selectedCommand);
            }
        }
        finally
        {
            _formatMenuSession?.Dispose();
            _formatMenuSession = null;
            previewFormat(null);
            ownerDraw.Dispose();
        }
    }

    public bool HandleContextMenuMessage(ref Message message) =>
        _formatMenuSession?.HandleMessage(ref message) == true;

    private nint BuildMainMenu()
    {
        var root = CreateMenu(false);
        try
        {
            AppendPopup(root, Loc.Get("menu.file.label"), BuildFileMenu());
            AppendPopup(root, Loc.Get("menu.edit.label"), BuildEditMenu());
            AppendPopup(root, Loc.Get("menu.insert.label"), BuildInsertMenu());
            AppendPopup(root, Loc.Get("menu.format.label"), BuildFormatMenu());
            AppendPopup(root, Loc.Get("menu.view.label"), BuildViewMenu());
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
            AppendMainMenuCommand(newMenu, AppCommand.NewDocument, "Markdown");
            AppendMainMenuCommand(newMenu, AppCommand.NewPlainText, Loc.Get("menu.file.newPlainText"));
            AppendPopup(menu, Loc.Get("menu.file.new"), newMenu);
            AppendMainMenuCommand(menu, AppCommand.NewWindow, Loc.Get("menu.file.newWindow"));
            AppendMainMenuCommand(menu, AppCommand.OpenDocument, Loc.Get("menu.file.open"));
            AppendMainMenuCommand(menu, AppCommand.OpenDocumentReadOnly, Loc.Get("menu.file.openReadOnly"));
            AppendMainMenuCommand(menu, AppCommand.OpenDocumentInNewWindow, Loc.Get("menu.file.openInNewWindow"));
            AppendMainMenuCommand(menu, AppCommand.OpenFolder, Loc.Get("menu.file.openFolder"));
            AppendSeparator(menu);

            _recentWorkspaceMenu = CreateMenu(true);
            AppendDisabledText(_recentWorkspaceMenu, Loc.Get("menu.file.noRecentItems"));
            AppendPopup(menu, Loc.Get("menu.file.recentItems"), _recentWorkspaceMenu);
            AppendMainMenuCommand(menu, AppCommand.RecoverUnsavedFiles, Loc.Get("menu.file.recoverUnsaved"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.SaveDocument, Loc.Get("menu.file.save"));
            AppendMainMenuCommand(menu, AppCommand.SaveAllDocuments, Loc.Get("menu.file.saveAll"));
            AppendMainMenuCommand(menu, AppCommand.SaveDocumentAs, Loc.Get("menu.file.saveAs"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.ExportDocument, Loc.Get("menu.file.export"));
            AppendMainMenuCommand(menu, AppCommand.Print, Loc.Get("menu.file.print"));

            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.LocateCurrentDocumentInWorkspace, Loc.Get("documentTabMenu.locate"));
            AppendMainMenuCommand(menu, AppCommand.CopyCurrentFilePath, Loc.Get("menu.file.copyPath"));
            AppendMainMenuCommand(menu, AppCommand.CopyCurrentFileContents, Loc.Get("menu.file.copyContents"));
            AppendMainMenuCommand(menu, AppCommand.ShowCurrentFileInFolder, Loc.Get("menu.file.showInFolder"));
            AppendMainMenuCommand(menu, AppCommand.RenameCurrentDocument, Loc.Get("menu.file.rename"));

            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowPreferences, Loc.Get("menu.help.preferences"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.CloseCurrentDocumentTab, Loc.Get("documentTabMenu.close"));
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
            AppendMainMenuCommand(copyPasteAs, AppCommand.CopyPlainText, Loc.Get("menu.edit.copyPlainText"));
            AppendMainMenuCommand(copyPasteAs, AppCommand.CopyMarkdown, Loc.Get("menu.edit.copyMarkdown"));
            AppendMainMenuCommand(copyPasteAs, AppCommand.CopyHtml, Loc.Get("menu.edit.copyHtml"));
            AppendPopup(menu, Loc.Get("menu.edit.copyPasteAs"), copyPasteAs);

            AppendMainMenuCommand(menu, AppCommand.Paste, Loc.Get("menu.edit.paste"));
            AppendMainMenuCommand(menu, AppCommand.PastePlainText, Loc.Get("menu.edit.pastePlainText"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.SelectAll, Loc.Get("menu.edit.selectAll"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.Find, Loc.Get("menu.edit.find"));
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private (nint Menu, AppCommand[] Commands, ModernMenuBuilder OwnerDraw) BuildEditorContextMenu(EditorCommandStatus status)
    {
        var model = BuildEditorMenuModel(status);
        var ownerDraw = new ModernMenuBuilder();
        var menu = ownerDraw.Build(model, _showKeyboardShortcutsProvider());
        return (menu, ownerDraw.Commands.ToArray(), ownerDraw);
    }

    private nint BuildFullScreenMainMenu()
    {
        var root = CreateMenu(true);
        try
        {
            AppendPopup(root, Loc.Get("menu.file.label"), BuildFileMenu());
            AppendPopup(root, Loc.Get("menu.edit.label"), BuildEditMenu());
            AppendPopup(root, Loc.Get("menu.insert.label"), BuildInsertMenu());
            AppendPopup(root, Loc.Get("menu.format.label"), BuildFormatMenu());
            AppendPopup(root, Loc.Get("menu.view.label"), BuildViewMenu());
            AppendPopup(root, Loc.Get("menu.help.label"), BuildHelpMenu());
            return root;
        }
        catch
        {
            NativeMethods.DestroyMenu(root);
            throw;
        }
    }

    public bool TryGetDocumentTabByCommandId(uint commandId, out int index)
    {
        return _documentTabCommandIds.TryGetValue(commandId, out index);
    }

    private (nint Menu, AppCommand[] Commands, ModernMenuBuilder OwnerDraw) BuildExpandedSourceContextMenu() =>
        BuildEditorContextMenu(EditorCommandStatus.Empty with { ExpandedSource = true });


    private void AppendFootnoteCommands(nint menu)
    {
        AppendCommand(menu, AppCommand.GoToFootnoteReference, Loc.Get("contextMenu.footnote.goToReference"));
        AppendCommand(menu, AppCommand.ResetFootnoteLabel, Loc.Get("contextMenu.footnote.resetLabel"));
        AppendCommand(menu, AppCommand.ClearFootnoteReferences, Loc.Get("contextMenu.footnote.clearReferences"));
        AppendCommand(menu, AppCommand.DeleteFootnote, Loc.Get("contextMenu.footnote.delete"));
    }

    private void AppendTableCommands(nint menu, bool includeClipboard)
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


    private nint BuildInsertMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            // 与 macOS NativeMenuBuilder.insertMenu 保持同一分组：
            // 图片 / 表格 / Mermaid / 链接 / 脚注 | 行内公式 / 块级公式 / 水平线 | YAML 元数据。
            AppendPopup(menu, Loc.Get("menu.format.image"), BuildInsertImageSubmenu());
            AppendMainMenuCommand(menu, AppCommand.InsertTable, Loc.Get("menu.paragraph.insertTable"));
            AppendMainMenuCommand(menu, AppCommand.InsertMermaid, Loc.Get("menu.paragraph.insertMermaid"));
            AppendMainMenuCommand(menu, AppCommand.InsertLink, Loc.Get("menu.format.insertLink"));
            AppendMainMenuCommand(menu, AppCommand.InsertFootnote, Loc.Get("menu.paragraph.insertFootnote"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.InsertMathInline, Loc.Get("menu.format.insertMathInline"));
            AppendMainMenuCommand(menu, AppCommand.InsertMathBlock, Loc.Get("menu.paragraph.insertMathBlock"));
            AppendMainMenuCommand(menu, AppCommand.InsertHorizontalRule, Loc.Get("menu.paragraph.horizontalRule"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.ShowFrontMatter, Loc.Get("menu.paragraph.frontMatter"));
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
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
        AppendMainMenuCommand(menu, AppCommand.ToggleHighlight, Loc.Get("menu.format.highlight"));
        AppendMainMenuCommand(menu, AppCommand.ToggleInlineCode, Loc.Get("menu.format.inlineCode"));
        AppendSeparator(menu);
        AppendPopup(menu, Loc.Get("menu.paragraph.label"), BuildParagraphMenu());
        var lists = CreateMenu(true);
        foreach (var (command, key) in new[] { (AppCommand.ToggleBulletList, "menu.paragraph.bulletList"), (AppCommand.ToggleOrderedList, "menu.paragraph.orderedList"), (AppCommand.ToggleTaskList, "menu.paragraph.taskList"), (AppCommand.IncreaseListIndent, "menu.paragraph.increaseIndent"), (AppCommand.DecreaseListIndent, "menu.paragraph.decreaseIndent") })
        {
            if (command == AppCommand.IncreaseListIndent) AppendSeparator(lists);
            AppendMainMenuCommand(lists, command, Loc.Get(key));
        }
        AppendPopup(menu, Loc.Get("menu.paragraph.list"), lists);
        AppendMainMenuCommand(menu, AppCommand.ToggleQuote, Loc.Get("menu.paragraph.quote"));
        AppendMainMenuCommand(menu, AppCommand.ToggleCodeBlock, Loc.Get("menu.paragraph.codeBlock"));
        AppendPopup(menu, Loc.Get("menu.paragraph.alert"), BuildAlertSubmenu(useMainMenuText: true));
        AppendSeparator(menu);
        var paragraphs = CreateMenu(true);
        foreach (var (command, key) in new[] { (AppCommand.InsertLineBefore, "menu.paragraph.insertLineBefore"), (AppCommand.InsertLineAfter, "menu.paragraph.insertLineAfter"), (AppCommand.DuplicateParagraph, "menu.paragraph.duplicateParagraph"), (AppCommand.DeleteParagraph, "menu.paragraph.deleteParagraph") })
        {
            if (command == AppCommand.DuplicateParagraph) AppendSeparator(paragraphs);
            AppendMainMenuCommand(paragraphs, command, Loc.Get(key));
        }
        AppendPopup(menu, Loc.Get("menu.format.paragraphOperations"), paragraphs);
        var table = CreateMenu(true);
        AppendTableCommands(table, includeClipboard: false);
        AppendPopup(menu, Loc.Get("menu.paragraph.table"), table);
        AppendPopup(menu, Loc.Get("menu.format.image"), BuildImageSubmenu());
        AppendSeparator(menu);
        AppendMainMenuCommand(menu, AppCommand.FormatPainter, Loc.Get("menu.format.formatPainter"));
        AppendMainMenuCommand(menu, AppCommand.FormatPainterApply, Loc.Get("contextMenu.formatPainterApply"));
        AppendMainMenuCommand(menu, AppCommand.FormatCodeBlock, Loc.Get("menu.format.formatCodeBlock"));
        AppendSeparator(menu);
        AppendMainMenuCommand(menu, AppCommand.ClearFormat, Loc.Get("menu.format.clearFormat"));
        return menu;
    }

    private nint BuildImageSubmenu()
    {
        var image = CreateMenu(true);
        AppendMainMenuCommand(image, AppCommand.ChangeImage, Loc.Get("contextMenu.image.change"));
        AppendMainMenuCommand(image, AppCommand.EditImageCaption, Loc.Get("contextMenu.image.caption"));
            AppendPopup(image, Loc.Get("menu.format.resizeImage"), BuildResizeImageSubmenu());
        AppendMainMenuCommand(image, AppCommand.RotateImageClockwise, Loc.Get("menu.format.rotateImageClockwise"));
        AppendMainMenuCommand(image, AppCommand.SaveImageAs, Loc.Get("menu.format.saveImageAs"));
        return image;
    }

    private nint BuildInsertImageSubmenu()
    {
        var image = CreateMenu(true);
        AppendCommand(image, AppCommand.InsertImage, Loc.Get("menu.format.insertImage"));
        AppendCommand(image, AppCommand.InsertImageFromUrl, Loc.Get("menu.format.insertImageFromUrl"));
        return image;
    }

    private nint BuildResizeImageSubmenu()
    {
        var resize = CreateMenu(true);
        AppendCommand(resize, AppCommand.ResizeImage50, Loc.Get("contextMenu.image.resize50"));
        AppendCommand(resize, AppCommand.ResizeImage75, Loc.Get("contextMenu.image.resize75"));
        AppendCommand(resize, AppCommand.ResizeImage90, Loc.Get("contextMenu.image.resize90"));
        AppendCommand(resize, AppCommand.ResizeImage100, Loc.Get("contextMenu.image.resizeFull"));
        return resize;
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
        var canZoom = _router.GetState(AppCommand.ZoomIn).IsEnabled;
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
                NativeMethods.MfString
                    | (canZoom ? NativeMethods.MfEnabled : NativeMethods.MfGrayed)
                    | (percent == current ? NativeMethods.MfChecked : NativeMethods.MfUnchecked),
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

    private nint BuildAlertSubmenu(bool useMainMenuText)
    {
        var alerts = CreateMenu(true);
        void AppendAlert(AppCommand command, string key)
        {
            if (useMainMenuText) AppendMainMenuCommand(alerts, command, Loc.Get(key));
            else AppendCommand(alerts, command, Loc.Get(key));
        }

        AppendAlert(AppCommand.InsertAlertNote, "menu.paragraph.alertNote");
        AppendAlert(AppCommand.InsertAlertTip, "menu.paragraph.alertTip");
        AppendAlert(AppCommand.InsertAlertImportant, "menu.paragraph.alertImportant");
        AppendAlert(AppCommand.InsertAlertWarning, "menu.paragraph.alertWarning");
        AppendAlert(AppCommand.InsertAlertCaution, "menu.paragraph.alertCaution");
        return alerts;
    }

    private void RefreshDocumentTabMenu()
    {
        if (_documentTabMenu == 0)
        {
            return;
        }

        var count = NativeMethods.GetMenuItemCount(_documentTabMenu);
        for (var index = count - 1; index >= 0; index--)
        {
            NativeMethods.DeleteMenu(_documentTabMenu, (uint)index, NativeMethods.MfByPosition);
        }

        _documentTabCommandIds.Clear();
        var documents = _openDocumentTabProvider();
        var activeIndex = _activeDocumentTabProvider();

        AppendMainMenuCommand(
            _documentTabMenu,
            AppCommand.SwitchToPreviousDocumentTab,
            Loc.Get("menu.view.previousTab"),
            _router.GetState(AppCommand.SwitchToPreviousDocumentTab).IsEnabled
                ? NativeMethods.MfString
                : NativeMethods.MfString | NativeMethods.MfGrayed);
        AppendMainMenuCommand(
            _documentTabMenu,
            AppCommand.SwitchToNextDocumentTab,
            Loc.Get("menu.view.nextTab"),
            _router.GetState(AppCommand.SwitchToNextDocumentTab).IsEnabled
                ? NativeMethods.MfString
                : NativeMethods.MfString | NativeMethods.MfGrayed);
        AppendMainMenuCommand(
            _documentTabMenu,
            AppCommand.CloseCurrentDocumentTab,
            Loc.Get("documentTabMenu.close"),
            _router.GetState(AppCommand.CloseCurrentDocumentTab).IsEnabled
                ? NativeMethods.MfString
                : NativeMethods.MfString | NativeMethods.MfGrayed);
        AppendMainMenuCommand(
            _documentTabMenu,
            AppCommand.CloseOtherDocumentTabs,
            Loc.Get("menu.view.closeOtherTabs"),
            _router.GetState(AppCommand.CloseOtherDocumentTabs).IsEnabled
                ? NativeMethods.MfString
                : NativeMethods.MfString | NativeMethods.MfGrayed);
        AppendMainMenuCommand(
            _documentTabMenu,
            AppCommand.ReopenClosedTab,
            Loc.Get("menu.view.reopenClosedTab"),
            _router.GetState(AppCommand.ReopenClosedTab).IsEnabled
                ? NativeMethods.MfString
                : NativeMethods.MfString | NativeMethods.MfGrayed);

        if (documents.Count > 0)
        {
            AppendSeparator(_documentTabMenu);
        }

        for (var index = 0; index < documents.Count; index++)
        {
            var label = DocumentTabMenuLabel(documents, index)
                .Replace("&", "&&", StringComparison.Ordinal);
            var flags = NativeMethods.MfString
                | (index == activeIndex ? NativeMethods.MfChecked : NativeMethods.MfUnchecked);
            if (index < 9)
            {
                var command = (AppCommand)((int)AppCommand.SwitchDocumentTab1 + index);
                AppendMainMenuCommand(_documentTabMenu, command, label, flags);
                continue;
            }

            if ((uint)(CommandCatalog.DocumentTabCommandBase + _documentTabCommandIds.Count)
                > CommandCatalog.DocumentTabCommandMax)
            {
                break;
            }

            var commandId = (uint)(CommandCatalog.DocumentTabCommandBase + _documentTabCommandIds.Count);
            _documentTabCommandIds[commandId] = index;
            Append(_documentTabMenu, flags, commandId, label);
        }
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
            _documentTabMenu = CreateMenu(true);
            AppendPopup(menu, Loc.Get("menu.view.documentTabs"), _documentTabMenu);
            RefreshDocumentTabMenu();
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.ToggleSourceMode, Loc.Get("menu.view.sourceMode"));
            AppendMainMenuCommand(menu, AppCommand.ToggleReadOnlyMode, Loc.Get("menu.view.readOnlyMode"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.ToggleEditorFocusMode, Loc.Get("menu.view.editorFocusMode"));
            AppendMainMenuCommand(menu, AppCommand.ToggleEditorTypewriterMode, Loc.Get("menu.view.editorTypewriterMode"));
            AppendMainMenuCommand(menu, AppCommand.ToggleFocusMode, Loc.Get("menu.view.focusMode"));
            AppendMainMenuCommand(menu, AppCommand.ToggleEditorFullScreen, Loc.Get("menu.view.editorFullScreen"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowCodeHighlight, Loc.Get("menu.view.showCodeHighlight"));
            AppendSeparator(menu);
            _zoomMenu = CreateMenu(true);
            AppendPopup(menu, Loc.Get("menu.view.zoom"), _zoomMenu);
            RefreshZoomMenu();
            AppendCommand(menu, AppCommand.ZoomIn, Loc.Get("menu.view.zoomIn"));
            AppendCommand(menu, AppCommand.ZoomOut, Loc.Get("menu.view.zoomOut"));
            AppendCommand(menu, AppCommand.ZoomReset, Loc.Get("menu.view.zoomReset"));
            AppendSeparator(menu);
            AppendMainMenuCommand(menu, AppCommand.ShowThemeSettings, Loc.Get("menu.view.theme"));
            AppendMainMenuCommand(menu, AppCommand.FollowSystemColorMode, Loc.Get("menu.view.followSystem"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.RerenderAllMermaid, Loc.Get("menu.paragraph.rerenderAllMermaid"));
            AppendMainMenuCommand(menu, AppCommand.RestartEditor, Loc.Get("menu.view.restartEditor"));
            return menu;
        }
        catch
        {
            NativeMethods.DestroyMenu(menu);
            throw;
        }
    }

    private nint BuildHelpMenu()
    {
        var menu = CreateMenu(true);
        try
        {
            AppendCommand(menu, AppCommand.ShowWelcome, Loc.Get("menu.help.welcome"));
            var samples = CreateMenu(true);
            AppendCommand(samples, AppCommand.OpenSampleAlert, Loc.Get("menu.help.sampleAlert"));
            AppendCommand(samples, AppCommand.OpenSampleYamlBasic, Loc.Get("menu.help.sampleYamlBasic"));
            AppendCommand(samples, AppCommand.OpenSampleYamlAdvanced, Loc.Get("menu.help.sampleYamlAdvanced"));
            AppendPopup(menu, Loc.Get("menu.help.samples"), samples);
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.ShowShortcuts, Loc.Get("menu.help.shortcuts"));
            AppendCommand(menu, AppCommand.ShowChangelog, Loc.Get("menu.help.changelog"));
            AppendCommand(menu, AppCommand.LearnMarkdown, Loc.Get("menu.help.learnMarkdown"));
            AppendCommand(menu, AppCommand.OpenOnlineHelp, Loc.Get("menu.help.onlineHelp"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.InstallOptionalFonts, Loc.Get("menu.help.installOptionalFonts"));
            AppendCommand(menu, AppCommand.ShowCodeFormatter, Loc.Get("prefs.editor.markdown.codeFormatter.manage"));
            AppendSeparator(menu);
            AppendCommand(menu, AppCommand.CheckForUpdates, Loc.Get("menu.help.checkForUpdates"));
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

    /// <summary>
    /// 菜单栏的顶级项始终可展开（与 macOS 菜单栏一致），但其中嵌套的子菜单在没有可用命令时需要置灰。
    /// </summary>
    internal static void RefreshMenuBarPopupAvailability(nint menuBar)
    {
        var count = NativeMethods.GetMenuItemCount(menuBar);
        for (var index = 0; index < count; index++)
        {
            var submenu = NativeMethods.GetSubMenu(menuBar, index);
            if (submenu != 0)
            {
                RefreshPopupAvailability(submenu);
            }
        }
    }

    /// <summary>
    /// 按 AppKit 的自动启用规则同步子菜单容器：子菜单里没有任何可用命令时，容器项本身也置灰；
    /// 只要存在一个可用命令，容器保持可展开。
    /// </summary>
    internal static void RefreshPopupAvailability(nint menu)
    {
        var count = NativeMethods.GetMenuItemCount(menu);
        for (var index = 0; index < count; index++)
        {
            var submenu = NativeMethods.GetSubMenu(menu, index);
            if (submenu == 0)
            {
                continue;
            }

            RefreshPopupAvailability(submenu);
            NativeMethods.EnableMenuItem(
                menu,
                (uint)index,
                NativeMethods.MfByPosition
                    | (ContainsEnabledItem(submenu) ? NativeMethods.MfEnabled : NativeMethods.MfGrayed));
            // Like AppKit, paragraph/heading/list containers reflect their
            // active descendant rather than borrowing one representative item.
            var firstCommand = (AppCommand)NativeMethods.GetMenuItemID(submenu, 0);
            if (firstCommand is AppCommand.SetParagraph or AppCommand.SetHeading1 or AppCommand.ToggleBulletList)
            {
                NativeMethods.CheckMenuItem(menu, (uint)index, NativeMethods.MfByPosition
                    | (ContainsCheckedItem(submenu) ? NativeMethods.MfChecked : NativeMethods.MfUnchecked));
            }
        }
    }

    private static bool ContainsCheckedItem(nint menu)
    {
        for (var index = 0; index < NativeMethods.GetMenuItemCount(menu); index++)
        {
            var state = NativeMethods.GetMenuState(menu, (uint)index, NativeMethods.MfByPosition);
            if (state != uint.MaxValue && (state & NativeMethods.MfChecked) != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// macOS 的“最近项目”父菜单没有命令动作，即使全部子项为空态也会展开。
    /// Win32 聚合规则会把全禁用子菜单的父项置灰；这里保留该全局入口的可展开性。
    /// </summary>
    private void EnableRecentItemsPopup(nint menu)
    {
        if (menu == 0 || _recentWorkspaceMenu == 0) return;

        var count = NativeMethods.GetMenuItemCount(menu);
        for (var index = 0; index < count; index++)
        {
            var submenu = NativeMethods.GetSubMenu(menu, index);
            if (submenu == 0) continue;
            if (submenu == _recentWorkspaceMenu)
            {
                NativeMethods.EnableMenuItem(
                    menu,
                    (uint)index,
                    NativeMethods.MfByPosition | NativeMethods.MfEnabled);
            }
            else
            {
                EnableRecentItemsPopup(submenu);
            }
        }
    }

    private static bool ContainsEnabledItem(nint menu)
    {
        var count = NativeMethods.GetMenuItemCount(menu);
        for (var index = 0; index < count; index++)
        {
            var submenu = NativeMethods.GetSubMenu(menu, index);
            if (submenu != 0)
            {
                if (ContainsEnabledItem(submenu))
                {
                    return true;
                }

                continue;
            }

            var state = NativeMethods.GetMenuState(menu, (uint)index, NativeMethods.MfByPosition);
            if (state == uint.MaxValue)
            {
                continue;
            }

            const uint unavailable = NativeMethods.MfSeparator | NativeMethods.MfGrayed | NativeMethods.MfDisabled;
            if ((state & unavailable) == 0)
            {
                return true;
            }
        }

        return false;
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

        var files = _recentFileProvider().Take(RecentCommandCatalog.Files.Length).ToArray();
        var folders = _recentWorkspaceProvider().Take(RecentCommandCatalog.Folders.Length).ToArray();

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
                var command = RecentCommandCatalog.Files[index];
                AppendCommand(_recentWorkspaceMenu, command,
                    $"{index + 1}  {RecentMenuLabel(files[index], files.Concat(folders))}");
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
                var command = RecentCommandCatalog.Folders[index];
                AppendCommand(_recentWorkspaceMenu, command,
                    $"{index + 1}  {RecentMenuLabel(folders[index], files.Concat(folders))}");
            }
        }
    }

    private void AppendCommand(nint menu, AppCommand command, string text)
    {
        Append(menu, NativeMethods.MfString, (nuint)command, text);
    }

    internal static string RecentMenuLabel(string path, IEnumerable<string> siblingPaths)
    {
        var fileName = Path.GetFileName(path);
        var duplicate = siblingPaths.Count(candidate =>
            string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase)) > 1;
        if (!duplicate) return fileName;
        var parent = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? ""));
        return string.IsNullOrEmpty(parent) ? fileName : $"{fileName} — {parent}";
    }

    internal static string DocumentTabMenuLabel(
        IReadOnlyList<DocumentTabMenuEntry> entries,
        int index)
    {
        var entry = entries[index];
        var label = entry.IsDirty ? $"● {entry.Title}" : entry.Title;
        var duplicateCount = entries.Count(candidate =>
            string.Equals(candidate.Title, entry.Title, StringComparison.OrdinalIgnoreCase));
        if (duplicateCount < 2 || string.IsNullOrWhiteSpace(entry.FullPath))
        {
            return label;
        }

        var parent = Path.GetFileName(
            Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(entry.FullPath) ?? string.Empty));
        return string.IsNullOrEmpty(parent) ? label : $"{label} — {parent}";
    }

    /// <summary>
    /// 主菜单专用：若命令可自定义快捷键，则把本地化文本按首个 \t 拆成标签，
    /// 再拼接当前生效的快捷键（已清除则只显示标签）。上下文菜单不使用此方法。
    /// </summary>
    private void AppendMainMenuCommand(
        nint menu,
        AppCommand command,
        string rawText,
        uint flags = NativeMethods.MfString)
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
            Append(menu, flags, (nuint)command, shortcut is null ? label : $"{label}\t{shortcut}");
        }
        else
        {
            Append(menu, flags, (nuint)command, rawText);
        }
    }

    private void AppendPopup(nint menu, string text, nint popup)
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

    private void AppendSeparator(nint menu)
    {
        Append(menu, NativeMethods.MfSeparator, 0, null);
    }

    private void AppendDisabledText(nint menu, string text)
    {
        Append(menu, NativeMethods.MfString | NativeMethods.MfGrayed, 0, text);
    }

    private void Append(nint menu, uint flags, nuint item, string? text)
    {
        var formattedText = text is null
            ? null
            : MenuTextFormatter.Format(
                text,
                _showKeyboardShortcutsProvider(),
                _showMnemonicsProvider(),
                _uiLanguageProvider());
        if (!NativeMethods.AppendMenu(menu, flags, item, formattedText))
        {
            throw new Win32Exception();
        }
    }
}
