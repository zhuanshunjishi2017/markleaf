using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.ExternalLinks;
using MarkLeaf.Services.Updates;
using MarkLeaf.UI.Dialogs;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (TryHandleNativeTextShortcut(keyData))
        {
            return true;
        }

        var keyCode = keyData & Keys.KeyCode;
        if ((keyData & (Keys.Control | Keys.Alt | Keys.Shift)) == Keys.None
            && keyCode is Keys.Left or Keys.Right or Keys.Enter or Keys.Escape
            && _documentTabBar.HandleKeyboardMenuKey(keyCode))
        {
            return true;
        }

        if (_editorCommandStatus.ExpandedSource
            && (keyData & (Keys.Control | Keys.Alt)) == Keys.Control
            && (keyData & Keys.KeyCode) is Keys.C or Keys.V or Keys.X or Keys.A)
        {
            ExecuteCommand((keyData & Keys.KeyCode) switch
            {
                Keys.C => AppCommand.Copy,
                Keys.V => AppCommand.PastePlainText,
                Keys.X => AppCommand.Cut,
                _ => AppCommand.SelectAll,
            });
            return true;
        }

        if (_focusMode && keyData == Keys.Escape)
        {
            ToggleFocusMode();
            return true;
        }

        if (_editorFullScreen && keyData == Keys.Escape)
        {
            ToggleEditorFullScreen();
            return true;
        }

        // Tab / Shift+Tab（不含 Ctrl/Alt）：焦点在 WebView2 内时转发到
        // 编辑器执行缩进，WebView2 Runtime 120+ 的 AreBrowserAcceleratorKeysEnabled
        // 存在已知 bug（设置 false 仍会消费加速键），统一在此拦截最可靠。
        if ((keyData & Keys.KeyCode) == Keys.Tab
            && (keyData & Keys.Control) == Keys.None
            && (keyData & Keys.Alt) == Keys.None)
        {
            if (_webView is not null
                && _webView.IsHandleCreated
                && _webView.ContainsFocus
                && _editorHost is not null)
            {
                var shift = (keyData & Keys.Shift) != Keys.None;
                _editorHost.ForwardTab(shift);
                return true;
            }
        }

        return _commandRouter.TryExecuteShortcut(keyData)
            || base.ProcessCmdKey(ref message, keyData);
    }

    internal bool TryHandleNativeTextShortcut(Keys keyData, Control? focused = null)
    {
        var editor = NativeTextEditingPolicy.FindFocusedText(this, focused);
        if (editor is null)
        {
            return false;
        }

        var command = NativeTextEditingPolicy.MapShortcut(keyData);
        return command is not null && NativeTextEditingPolicy.Execute(editor, command.Value);
    }

    private CommandState GetCommandState(AppCommand command)
    {
        if (command == AppCommand.UseIndependentOutlineSidebar
            && _outlineAnimationTargetDetached is not null)
        {
            return new CommandState(false, _outlineDetached);
        }

        if (Array.IndexOf(RecentCommandCatalog.Folders, command) is var folderIndex && folderIndex >= 0)
        {
            return new CommandState(folderIndex < GetRecentWorkspaces().Count);
        }

        if (Array.IndexOf(RecentCommandCatalog.Files, command) is var fileIndex && fileIndex >= 0)
        {
            return new CommandState(fileIndex < GetRecentFiles().Count);
        }

        if (command is AppCommand.CopyCurrentFilePath or AppCommand.ShowCurrentFileInFolder)
            return new(_document?.FilePath is not null);
        if (command == AppCommand.RenameCurrentDocument)
            return new(_document is { FilePath: not null, IsReadOnly: false });
        if (command is >= AppCommand.SwitchDocumentTab1 and <= AppCommand.SwitchDocumentTab9)
        {
            var index = (int)command - (int)AppCommand.SwitchDocumentTab1;
            return new CommandState(index < _openDocuments.Count, index == _activeDocumentIndex);
        }

        if (command == AppCommand.CloseCurrentDocumentTab)
        {
            return new CommandState(_activeDocumentIndex >= 0);
        }

        if (command == AppCommand.CloseOtherDocumentTabs)
        {
            return new CommandState(_activeDocumentIndex >= 0 && _openDocuments.Count > 1);
        }

        if (command == AppCommand.SwitchToNextDocumentTab)
        {
            return new CommandState(_activeDocumentIndex >= 0 && _openDocuments.Count > 1);
        }

        if (command == AppCommand.SwitchToPreviousDocumentTab)
        {
            return new CommandState(_activeDocumentIndex >= 0 && _openDocuments.Count > 1);
        }

        if (command == AppCommand.LocateCurrentDocumentInWorkspace)
        {
            return new CommandState(
                _activeDocumentIndex >= 0
                && CanLocateDocumentInWorkspace(_openDocuments[_activeDocumentIndex]));
        }

        if (command is AppCommand.NewWindow or AppCommand.OpenDocumentInNewWindow)
        {
            return new CommandState(true);
        }

        var context = new CommandContext(
            DocumentAvailable: _document is not null,
            EditorReady: _editorHost?.IsDocumentLoaded == true,
            SidebarVisible: !_sidebarSplit.Panel1Collapsed,
            FocusMode: _focusMode,
            EditorFullScreen: _editorFullScreen,
            EditorFocusMode: _editorFocusMode,
            EditorTypewriterMode: _editorTypewriterMode,
            StatusBarVisible: _statusStrip?.Visible != false,
            OutlineActive: _sidebarActiveOutline,
            ShowCodeHighlight: _settings.Appearance.ShowCodeHighlight,
            FollowSystemColorMode: _settings.Appearance.FollowSystemColorMode,
            ListViewActive: _workspaceListViewActive,
            IndependentOutlineSidebar: _outlineDetached,
            // 有效只读 = 文件级只读（权限/以只读打开）或用户手动开启的只读模式；
            // 文件级只读无法通过菜单切回可写，因此不可切换。
            ReadOnlyMode: _document is { } readOnlyDocument
                && (readOnlyDocument.IsReadOnly || readOnlyDocument.IsUserReadOnly),
            ReadOnlyCanBeToggled: _document is not null && !_document.IsReadOnly,
            ClosedTabsAvailable: _closedTabHistory.Count > 0,
            EditorActions: _editorCommandStatus.Actions,
            SelectionScope: _editorCommandStatus.SelectionScope,
            DocumentDirty: _document?.IsDirty == true,
            DocumentHasPath: _document?.FilePath is not null,
            SavableDocumentsAvailable: DocumentMenuPolicy.SaveAllTargets(_openDocuments).Count > 0,
            WorkspaceAvailable: _workspaceRoot is not null,
            DocumentHasContent: _editorStatus.CharacterCount > 0);
        var state = CommandStateResolver.Resolve(command, context);
        if (command is AppCommand.Paste or AppCommand.PastePlainText)
            return state with { IsEnabled = state.IsEnabled && HasClipboardContent() };

        return state;
    }

    private void SwitchWorkspaceView(bool treeViewDesired)
    {
        var plan = WorkspaceViewNavigationPolicy.Switch(
            treeViewDesired,
            _workspaceListViewActive,
            _focusMode,
            _sidebarSplit.Panel1Collapsed);

        foreach (var step in plan.Navigation.Steps)
        {
            switch (step)
            {
                case ViewNavigationStep.ExitMinimalMode:
                    ToggleFocusMode();
                    break;
                case ViewNavigationStep.ExpandSidebar:
                    ExpandSidebar();
                    break;
                case ViewNavigationStep.ShowTarget:
                    ShowSidebarView(outline: false);
                    break;
                case ViewNavigationStep.FocusTarget:
                    var focusTarget = SidebarFocusTargetPolicy.Resolve(
                        SidebarTarget.Workspace, _workspaceListViewActive);
                    var target = focusTarget switch
                    {
                        SidebarFocusTarget.WorkspaceList => (Control)_workspaceDocumentList,
                        _ => _workspaceTree,
                    };
                    if (target.Visible) target.Focus();
                    break;
            }
        }

        if (plan.SwitchPresentation) ToggleWorkspaceView();
    }

    private static bool HasClipboardContent()
    {
        try
        {
            return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                || Clipboard.ContainsImage()
                || Clipboard.ContainsFileDropList()
                || Clipboard.ContainsData(DataFormats.Html);
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private async void ExecuteCommand(AppCommand command)
    {
        try { await ExecuteCommandCoreAsync(command); }
        catch (Exception error) { _logger.Error($"Command failed: {command}.", error); }
    }

    private async Task ExecuteContextMenuCommandAsync(AppCommand command, long? menuSessionId, Func<bool> isCurrent)
    {
        if (_editorHost is null || !isCurrent() || !GetCommandState(command).IsEnabled) return;
        using var scope = _editorHost.BeginMenuCommandScope(menuSessionId, isCurrent);
        await ExecuteCommandCoreAsync(command);
    }

    private async Task ExecuteCommandCoreAsync(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.NewDocument:
                _ = NewDocumentAsync(NewDocumentKind.Markdown);
                break;
            case AppCommand.NewPlainText:
                await NewDocumentAsync(NewDocumentKind.PlainText);
                break;
            case AppCommand.CopyCurrentFilePath:
                if (_document?.FilePath is { } copyPath) Clipboard.SetText(copyPath);
                break;
            case AppCommand.CopyCurrentFileContents:
                var copyDocument = _document;
                await SyncActiveDocumentAsync();
                if (copyDocument is not null)
                {
                    if (copyDocument.Markdown.Length == 0) Clipboard.Clear();
                    else Clipboard.SetText(copyDocument.Markdown);
                }
                break;
            case AppCommand.ShowCurrentFileInFolder:
                if (_document?.FilePath is { } shownPath) ShowDocumentInExplorer(shownPath);
                break;
            case AppCommand.RenameCurrentDocument:
                if (_document is { FilePath: { } renamePath, IsReadOnly: false })
                {
                    using var renameDialog = new TextInputDialog(Loc.Get("workspace.rename"), Loc.Get("workspace.rename"), Path.GetFileName(renamePath));
                    if (ShowModal(() => renameDialog.ShowDialog(this)) == DialogResult.OK)
                        await RenameWorkspaceEntryAsync(new WorkspaceEntry(Path.GetFileName(renamePath), renamePath, false), renameDialog.InputText);
                }
                break;
            case AppCommand.NewWindow:
                StartNewWindow();
                break;
            case AppCommand.OpenDocument:
                _ = OpenDocumentAsync();
                break;
            case AppCommand.OpenDocumentReadOnly:
                _ = OpenDocumentReadOnlyAsync();
                break;
            case AppCommand.OpenDocumentInNewWindow:
                OpenDocumentInNewWindow();
                break;
            case AppCommand.OpenFolder:
                _ = SelectWorkspaceFolderAsync();
                break;
            case AppCommand.CloseFolder:
                CloseWorkspace();
                break;
            case AppCommand.SaveDocument:
                _ = SaveDocumentAsync(saveAs: false);
                break;
            case AppCommand.SaveDocumentAs:
                _ = SaveDocumentAsync(saveAs: true);
                break;
            case AppCommand.SaveAllDocuments:
                _ = SaveAllDocumentsAsync();
                break;
            case AppCommand.ExportWithLastSettings:
                _ = ExportWithLastSettingsAsync();
                break;
            case AppCommand.ExportDocument:
                _ = ExportDocumentAsync();
                break;
            case AppCommand.ExportPdf:
                _ = ExportPdfAsync();
                break;
            case AppCommand.ExportHtml:
                _ = ExportHtmlAsync();
                break;
            case AppCommand.ExportImage:
                _ = ExportImageAsync();
                break;
            case AppCommand.Print:
                PrintDocument();
                break;
            case AppCommand.Cut:
                await ExecuteClipboardCopyAsync(ClipboardCopyMode.Formatted, cut: true);
                break;
            case AppCommand.Copy:
                await ExecuteClipboardCopyAsync(ClipboardCopyMode.Formatted, cut: false);
                break;
            case AppCommand.CopyMarkdown:
                await ExecuteClipboardCopyAsync(ClipboardCopyMode.Markdown, cut: false);
                break;
            case AppCommand.CopyPlainText:
                await ExecuteClipboardCopyAsync(ClipboardCopyMode.PlainText, cut: false);
                break;
            case AppCommand.CopyHtml:
                await CopySelectionHtmlAsync();
                break;
            case AppCommand.Paste:
                await PasteClipboardContentAsync();
                break;
            case AppCommand.PastePlainText:
                await PasteClipboardPlainTextAsync();
                break;
            case AppCommand.Find:
                OpenFindReplaceDialog(replace: false);
                break;
            case AppCommand.Replace:
                OpenFindReplaceDialog(replace: true);
                break;
            case AppCommand.ToggleSourceMode:
                _editorHost?.ExecuteCommand("toggleSourceMode");
                break;
            case AppCommand.ToggleSidebar:
                ToggleSidebarWithWindowResize();
                break;
            case AppCommand.ToggleFocusMode:
                ToggleFocusMode();
                break;
            case AppCommand.ToggleEditorFullScreen:
                ToggleEditorFullScreen();
                break;
            case AppCommand.ToggleEditorFocusMode:
                ToggleEditorFocusMode();
                break;
            case AppCommand.ToggleEditorTypewriterMode:
                ToggleEditorTypewriterMode();
                break;
            case AppCommand.ToggleReadOnlyMode:
                ToggleReadOnlyMode();
                break;
            case AppCommand.ReopenClosedTab:
                await ReopenLastClosedTabAsync();
                break;
            case AppCommand.SwitchToWorkspace:
                RevealSidebarView(outline: false);
                SetStatus(Loc.Get("status.switchedToWorkspace"));
                break;
            case AppCommand.SwitchToOutline:
                RevealSidebarView(outline: true);
                SetStatus(Loc.Get("status.switchedToOutline"));
                break;
            case AppCommand.UseIndependentOutlineSidebar:
                if (_outlineDetached)
                    MergeOutlineSidebar();
                else
                    DetachOutlineSidebar();
                break;
            case AppCommand.ViewTree:
                SwitchWorkspaceView(treeViewDesired: true);
                break;
            case AppCommand.ViewList:
                SwitchWorkspaceView(treeViewDesired: false);
                break;
            case AppCommand.ShowStatusBar:
                if (_focusMode)
                {
                    _statusBarVisibleBeforeFocus = true;
                    ToggleFocusMode();
                }
                else if (_statusStrip is not null)
                {
                    _statusStrip.Visible = !_statusStrip.Visible;
                }
                break;
            case AppCommand.ShowShortcuts:
                ShowShortcutHelp();
                break;
            case AppCommand.InstallOptionalFonts:
                ShowOptionalFonts();
                break;
            case AppCommand.ShowCodeFormatter:
                ShowCodeFormatterManager();
                break;
            case AppCommand.ShowColorThemes:
                ShowColorThemes();
                break;
            case AppCommand.ShowTypographyStyles:
                ShowTypographyStyles();
                break;
            case AppCommand.ShowThemeSettings:
                ShowColorThemes();
                break;
            case AppCommand.ShowChangelog:
                ShowChangelog();
                break;
            case AppCommand.ShowWelcome:
                _ = ShowWelcomeAsync();
                break;
            case AppCommand.CheckForUpdates:
                _ = CheckForUpdatesAsync();
                break;
            case AppCommand.LearnMarkdown:
                ExternalLinkService.Open("https://markdown.com.cn/basic-syntax/index.html");
                break;
            case AppCommand.OpenOnlineHelp:
                ExternalLinkService.Open("https://github.com/zhuanshunjishi2017/markleaf/blob/main/README.md");
                break;
            case AppCommand.OpenSampleAlert:
                _ = OpenBundledSampleAsync("alert-examples.md");
                break;
            case AppCommand.OpenSampleYamlBasic:
                _ = OpenBundledSampleAsync("yaml-front-matter-basic.md");
                break;
            case AppCommand.OpenSampleYamlAdvanced:
                _ = OpenBundledSampleAsync("yaml-front-matter-advanced.md");
                break;
            case AppCommand.ShowPreferences:
                ShowPreferences();
                break;
            case AppCommand.ShowAbout:
                ShowAbout();
                break;
            case AppCommand.RecoverUnsavedFiles:
                RecoverUnsavedFiles();
                break;
            case AppCommand.InsertLink:
                InsertLink();
                break;
            case AppCommand.InsertMathInline:
                InsertMath(isBlock: false);
                break;
            case AppCommand.InsertMathBlock:
                InsertMath(isBlock: true);
                break;
            case AppCommand.InsertMermaid:
                InsertMermaid();
                break;
            case AppCommand.InsertFootnote:
                await InsertFootnote();
                break;
            case AppCommand.ResetFootnoteLabel:
                await ResetFootnoteLabel();
                break;
            case AppCommand.GoToFootnoteReference:
                await GoToFootnoteReferenceAsync();
                break;
            case AppCommand.EditMath:
                EditMath();
                break;
            case AppCommand.SetMathNumber:
                SetMathNumber();
                break;
            case AppCommand.EditMermaid:
                EditMermaid();
                break;
            case AppCommand.DeclareCodeLanguage:
                DeclareCodeLanguage();
                break;
            case AppCommand.CopyCodeBlock:
                CopyCodeBlock();
                break;
            case AppCommand.EditImageCaption:
                EditImageCaption();
                break;
            case AppCommand.EditTableCaption:
                EditTableCaption();
                break;
            case AppCommand.InsertImage:
                await SelectAndInsertImagesAsync();
                break;
            case AppCommand.InsertImageFromUrl:
                await InsertImageFromUrlAsync();
                break;
            case AppCommand.InsertTable:
                InsertTable();
                break;
            case AppCommand.ChangeImage:
                await ChangeImageAsync();
                break;
            case AppCommand.SaveImageAs:
                await SaveImageAsAsync();
                break;
            case AppCommand.SaveMermaidAs:
                await SaveMermaidAsAsync();
                break;
            case AppCommand.OpenLink:
                if (_editorCommandStatus.LinkHref is { } openHref) _editorHost?.ExecuteCommand("openLink", openHref);
                break;
            case AppCommand.CopyLinkAddress:
                if (_editorCommandStatus.LinkHref is { } copyHref) Clipboard.SetText(copyHref);
                break;
            case AppCommand.EditLink:
                InsertLink(_editorCommandStatus.LinkHref);
                break;
            case AppCommand.ResizeImage100:
                _editorHost?.ExecuteCommand("resizeImage", "100");
                break;
            case AppCommand.ResizeImage50:
                _editorHost?.ExecuteCommand("resizeImage", "50");
                break;
            case AppCommand.ResizeImage75:
                _editorHost?.ExecuteCommand("resizeImage", "75");
                break;
            case AppCommand.ResizeImage90:
                _editorHost?.ExecuteCommand("resizeImage", "90");
                break;
            case AppCommand.OpenThemeFolder:
                OpenThemeFolder();
                break;
            case AppCommand.AddTheme:
                AddThemeFromFile();
                break;
            case AppCommand.ZoomIn:
                SetZoomPercent(NextZoom(_zoomPercent, 1));
                break;
            case AppCommand.ZoomOut:
                SetZoomPercent(NextZoom(_zoomPercent, -1));
                break;
            case AppCommand.ZoomReset:
                SetZoomPercent(100);
                break;
            case AppCommand.FollowSystemColorMode:
                ToggleFollowSystemColorMode();
                break;
            case AppCommand.ShowCodeHighlight:
                ToggleCodeHighlight();
                break;
            case AppCommand.RestartEditor:
                _ = RestartEditorAsync();
                break;
            case >= AppCommand.SwitchDocumentTab1 and <= AppCommand.SwitchDocumentTab9:
                _ = SwitchDocumentTabAsync((int)command - (int)AppCommand.SwitchDocumentTab1);
                break;
            case AppCommand.CloseCurrentDocumentTab:
                if (_activeDocumentIndex >= 0)
                    _ = CloseDocumentTabAsync(_activeDocumentIndex);
                break;
            case AppCommand.CloseOtherDocumentTabs:
                if (_activeDocumentIndex >= 0)
                    _ = CloseOtherDocumentTabsAsync(_activeDocumentIndex);
                break;
            case AppCommand.SwitchToNextDocumentTab:
                if (_activeDocumentIndex >= 0 && _openDocuments.Count > 1)
                    _ = SwitchDocumentTabAsync((_activeDocumentIndex + 1) % _openDocuments.Count);
                break;
            case AppCommand.SwitchToPreviousDocumentTab:
                if (_activeDocumentIndex >= 0 && _openDocuments.Count > 1)
                    _ = SwitchDocumentTabAsync((_activeDocumentIndex - 1 + _openDocuments.Count) % _openDocuments.Count);
                break;
            case AppCommand.LocateCurrentDocumentInWorkspace:
                if (_activeDocumentIndex >= 0)
                    _ = LocateDocumentInWorkspaceAsync(_activeDocumentIndex);
                break;
            case AppCommand.Exit:
                Close();
                break;
            default:
                if (TryGetRecentFile(command, out var recentFilePath))
                {
                    _ = OpenRecentFileAsync(recentFilePath);
                    break;
                }

                if (TryGetRecentWorkspace(command, out var workspacePath))
                {
                    _ = OpenWorkspaceAsync(workspacePath, revealSidebar: true);
                    break;
                }

                if (_editorHost?.IsDocumentLoaded == true && EditorCommandBindings.TryGetCommand(command, out var editorCommand))
                {
                    _editorHost.ExecuteCommand(editorCommand);
                    SetStatus(CommandStatusFormatter.FormatExecuted(command));
                    break;
                }

                _logger.Warning($"Command has no available handler: {command}.");
                return;
        }

        _logger.Info($"Command executed: {command}.");
        _menuService.RefreshStates();
    }

    private void ShowFullScreenMainMenu(Point screenLocation)
    {
        var commandId = _menuService.ShowFullScreenMainMenu(Handle, screenLocation);
        HandlePopupMenuCommand(commandId);
    }

    private async void ShowTopLevelMainMenu(int menuIndex, Point screenLocation)
    {
        var bounds = _documentTabBar.GetTopLevelMenuScreenBounds();
        var currentIndex = menuIndex;
        try
        {
            while (currentIndex >= 0 && currentIndex < bounds.Count)
            {
                _documentTabBar.SetOpenTopLevelMenuIndex(currentIndex);
                var (commandId, nextMenuIndex) = await _menuService.ShowSelfDrawnTopLevelMenuAsync(
                    this, currentIndex, screenLocation, DeviceDpi, _menuDarkMode, bounds);
                if (commandId is not null)
                {
                    HandlePopupMenuCommand(commandId);
                    return;
                }
                if (nextMenuIndex < 0 || nextMenuIndex == currentIndex) return;
                currentIndex = nextMenuIndex;
                screenLocation = _documentTabBar.GetTopLevelMenuScreenLocation(currentIndex);
            }
        }
        finally
        {
            _documentTabBar.SetOpenTopLevelMenuIndex(-1);
        }
    }

    private void HandlePopupMenuCommand(int? commandId)
    {
        if (commandId is null)
        {
            return;
        }

        if (_menuService.TryGetStyleByCommandId((uint)commandId.Value, out var styleId))
        {
            SetMarkdownStyle(styleId);
            return;
        }
        if (_menuService.TryGetZoomByCommandId((uint)commandId.Value, out var zoomPercent))
        {
            SetZoomPercent(zoomPercent);
            return;
        }
        if (_menuService.TryGetColorThemeByCommandId((uint)commandId.Value, out var colorThemeId))
        {
            if (!_settings.Appearance.FollowSystemColorMode)
                SetColorTheme(colorThemeId);
            return;
        }
        if (_menuService.TryGetDocumentTabByCommandId((uint)commandId.Value, out var documentTabIndex))
        {
            _ = SwitchDocumentTabAsync(documentTabIndex);
            return;
        }
        _commandRouter.TryExecuteById(commandId.Value);
    }

    private void OpenFindReplaceDialog(bool replace, string? query = null)
    {
        if (_editorHost is null)
        {
            return;
        }

        _findReplaceDialog ??= new FindReplaceDialog((command, text) => _editorHost?.ExecuteCommand(command, text));
        _findReplaceDialog.Open(this, replace, query);
    }

    private static bool IsEditorCommand(AppCommand command)
    {
        return command is >= AppCommand.Undo and <= AppCommand.Replace
            || command is >= AppCommand.SetParagraph and <= AppCommand.DeleteTable
            || command is AppCommand.ToggleUnderline or AppCommand.ToggleStrike or AppCommand.ToggleHighlight or AppCommand.ToggleInlineCode
                or AppCommand.PromoteHeading or AppCommand.DemoteHeading
            || command is AppCommand.InsertLineBefore or AppCommand.InsertLineAfter
                or AppCommand.DuplicateParagraph or AppCommand.DeleteParagraph
            || command is AppCommand.InsertMathInline or AppCommand.InsertMathBlock or AppCommand.InsertMermaid or AppCommand.InsertFootnote
                or AppCommand.ShowFrontMatter
                or AppCommand.InsertAlertNote or AppCommand.InsertAlertTip or AppCommand.InsertAlertImportant
                or AppCommand.InsertAlertWarning or AppCommand.InsertAlertCaution
                or AppCommand.ResetFootnoteLabel or AppCommand.GoToFootnoteReference
                or AppCommand.ClearFootnoteReferences or AppCommand.DeleteFootnote
                or AppCommand.IncreaseListIndent or AppCommand.DecreaseListIndent
            || command is AppCommand.SelectAll or AppCommand.ExitCode or AppCommand.ConvertMath
                 or AppCommand.DeleteMath or AppCommand.EditMath or AppCommand.SetMathNumber
                or AppCommand.DeclareCodeLanguage or AppCommand.CopyCodeBlock
                or AppCommand.EditMermaid or AppCommand.RerenderMermaid or AppCommand.DeleteMermaid or AppCommand.RerenderAllMermaid
            || command is AppCommand.ChangeImage or AppCommand.SaveImageAs
                or AppCommand.ResizeImage100 or AppCommand.ResizeImage50
                or AppCommand.ResizeImage75 or AppCommand.ResizeImage90
            || command is AppCommand.ClearFormat
            || command is AppCommand.FormatPainter
            || command == AppCommand.ToggleSourceMode;
    }

    private void OnEditorCommandStateChanged(object? sender, EditorCommandStatus status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorCommandStateChanged(sender, status));
            return;
        }

        _editorCommandStatus = status;
        RefreshPersistentStatusBar();
        _menuService.RefreshStates();
    }

    private async void OnEditorContextMenuRequested(object? sender, EditorContextMenuRequest request)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorContextMenuRequested(sender, request));
            return;
        }

        _logger.Info(
            $"context-menu requested: menuHeight={request.MenuHeight} outside={request.OutsideDocument} " +
            $"source={request.SourceMode} expanded={request.ExpandedSource} client=({request.ClientX},{request.ClientY})");

        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        var screenPoint = _editorHost.EditorPointToScreen(request);
        if (request.OutsideDocument)
        {
            if (!_editorFullScreen)
            {
                return;
            }
            ShowExitFullScreenMenu(screenPoint);
            return;
        }
        try
        {
            var status = request.ExpandedSource
                ? _editorCommandStatus with
                {
                    SourceMode = false,
                    ExpandedSource = true,
                    HasSelection = true,
                }
                : request.SourceMode
                ? _editorCommandStatus with { SourceMode = true, ExpandedSource = false }
                : _editorCommandStatus;
            await _menuService.ShowEditorContextMenuAsync(
                this,
                request,
                (clientX, clientY) => _editorHost.EditorPointToScreen(clientX, clientY),
                DeviceDpi,
                _menuDarkMode,
                command => _editorHost.ExecuteCommand("previewFormat", command, menuSessionId: request.MenuSessionId),
                status,
                () => { if (!IsDisposed && !Disposing) _editorHost.ExecuteCommand("hideFormatMenu", menuSessionId: request.MenuSessionId); },
                (command, isCurrent) => ExecuteContextMenuCommandAsync(command, request.MenuSessionId, isCurrent),
                inlineCommonActions: _settings.Editor.InlineCommonActions);
        }
        catch (Exception exception)
        {
            _logger.Error("Editor context menu failed.", exception);
        }
    }

    private async void OnEditorBlockMenuRequested(object? sender, EditorBlockMenuRequest request)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorBlockMenuRequested(sender, request));
            return;
        }

        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        var screenPoint = _editorHost.EditorPointToScreen(request);
        await _menuService.ShowBlockHandleMenuAsync(
            this,
            screenPoint,
            DeviceDpi,
            _menuDarkMode,
            _editorCommandStatus,
            () => { if (!IsDisposed && !Disposing) _editorHost?.ClearBlockHighlight(); });
    }

    private async Task CheckForUpdatesAsync(bool silent = false)
    {
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var currentVersion = typeof(MainForm).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var updateService = new GitHubUpdateService();
            var release = await updateService.FindUpdateAsync(currentVersion, cancellation.Token);
            if (release is null)
            {
                if (!silent)
                {
                    ShowMessage(this, Loc.Get("update.latest"), "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            // 自动检查尊重"跳过此版本"：仅当远端版本与被跳过版本一致时静默；
            // 更高版本（或手动检查）仍会提示。跳过键取版本号文本，重新发布
            // 的同版本构建号变化不视为新版本。
            if (silent
                && string.Equals(_settings.General.SkippedUpdateVersion, release.VersionText, StringComparison.Ordinal))
            {
                return;
            }

            var installButton = new System.Windows.Forms.TaskDialogButton(Loc.Get("update.installNow"));
            var skipButton = new System.Windows.Forms.TaskDialogButton(Loc.Get("update.skipVersion"));
            var laterButton = new System.Windows.Forms.TaskDialogButton(Loc.Get("update.remindLater"));
            var page = new System.Windows.Forms.TaskDialogPage
            {
                Caption = "MarkLeaf",
                Heading = Loc.Format("update.available", release.VersionText, release.BuildNumber),
                Buttons = { installButton, skipButton, laterButton },
                AllowCancel = false,
            };
            var choice = System.Windows.Forms.TaskDialog.ShowDialog(Handle, page);
            if (choice == skipButton)
            {
                _settings.General.SkippedUpdateVersion = release.VersionText;
                SaveSettings();
                SetStatus(Loc.Format("update.skippedStatus", release.VersionText));
                return;
            }

            if (choice != installButton)
            {
                return;
            }

            SetStatus(Loc.Get("update.downloading"));
            var installerPath = await updateService.DownloadInstallerAsync(release, cancellation.Token);
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            _closeApproved = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (!silent)
            {
                ShowMessage(this, Loc.Get("update.failed"), "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Could not check for application updates.", exception);
            if (!silent)
            {
                ShowMessage(this, Loc.Get("update.failed") + "\r\n\r\n" + exception.Message,
                    "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void OnMermaidEditRequested(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnMermaidEditRequested(sender, e));
            return;
        }

        EditMermaid();
    }

    private async void OnOpenLinkRequested(object? sender, string url)
    {
        try
        {
            if (!ExternalLinkService.IsAllowed(url))
            {
                var localPath = ExternalLinkService.TryResolveLocalPath(url, _document?.FilePath);
                if (localPath is not null && File.Exists(localPath))
                {
                    await OpenDocumentPathAsync(localPath, forceNewTab: true);
                    RecordRecentFile(localPath);
                    return;
                }
            }

            ExternalLinkService.Open(url, _document?.FilePath);
            SetStatus(Loc.Get("status.linkOpened"));
        }
        catch (Exception exception)
        {
            _logger.Error("External link could not be opened.", exception);
            ShowMessage(
                this,
                Loc.Get("dialog.cannotOpenLink"),
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnFootnoteDefinitionMissing(object? sender, EventArgs e)
    {
        ShowMessage(
            this,
            Loc.Get("dialog.footnoteDefinitionMissing"),
            "MarkLeaf",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void DeclareCodeLanguage()
    {
        if (_editorHost?.IsDocumentLoaded != true || !_editorCommandStatus.CodeBlock || _document?.IsReadOnly == true)
        {
            return;
        }

        using var dialog = new TextInputDialog(
            Loc.Get("dialog.codeLanguageTitle"),
            Loc.Get("dialog.codeLanguagePrompt"),
            _editorCommandStatus.CodeBlockLanguage ?? string.Empty);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand("setCodeBlockLanguage", dialog.InputText);
        SetStatus(Loc.Get("status.codeLanguageUpdated"));
    }

    private void OnCodeBlockLanguageRequested(object? sender, EditorCodeBlockLanguageRequest request)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnCodeBlockLanguageRequested(sender, request));
            return;
        }
        if (_editorHost?.IsDocumentLoaded != true || _document?.IsReadOnly == true)
        {
            return;
        }

        using var dialog = new TextInputDialog(
            Loc.Get("dialog.codeLanguageTitle"),
            Loc.Get("dialog.codeLanguagePrompt"),
            request.Language);
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand(
            "setCodeBlockLanguageAt",
            JsonSerializer.Serialize(new { position = request.Position, language = dialog.InputText }));
        SetStatus(Loc.Get("status.codeLanguageUpdated"));
    }

    private void OnCopyCodeBlockRequested(object? sender, string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnCopyCodeBlockRequested(sender, text));
            return;
        }
        CopyCodeBlockText(text);
    }

    private void CopyCodeBlock()
    {
        CopyCodeBlockText(
            (_editorCommandStatus.CodeBlock || _editorCommandStatus.FrontMatter)
                ? _editorCommandStatus.CodeBlockText ?? string.Empty
                : string.Empty);
    }

    private void CopyCodeBlockText(string text)
    {
        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            SetStatus(Loc.Get("status.copied"));
        }
        catch (Exception exception)
        {
            _logger.Error("Copy code block command failed.", exception);
            SetStatus(Loc.Get("status.clipboardFailed"));
        }
    }

    private async Task ExecuteClipboardCopyAsync(ClipboardCopyMode mode, bool cut)
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        try
        {
            var selection = await _editorHost.RequestSelectionExportAsync();
            if (string.IsNullOrEmpty(selection.Text)
                && string.IsNullOrEmpty(selection.Markdown)
                && string.IsNullOrEmpty(selection.Html))
            {
                SetStatus(Loc.Get("status.noTextToCopy"));
                return;
            }

            var text = mode switch
            {
                ClipboardCopyMode.Markdown => selection.Markdown,
                ClipboardCopyMode.PlainText => selection.Text,
                _ => selection.Text,
            };
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData(DataFormats.Text, text);
            if (mode == ClipboardCopyMode.Formatted && !string.IsNullOrEmpty(selection.Html))
            {
                data.SetData(DataFormats.Html, ClipboardHtmlFormatter.Create(selection.Html));
            }
            Clipboard.SetDataObject(data, true);
            if (cut)
            {
                if (_document?.IsReadOnly == true)
                {
                    SetStatus(Loc.Get("status.copied"));
                    return;
                }
                _editorHost.ExecuteCommand("deleteSelection");
            }
            SetStatus(cut ? Loc.Get("status.cut") : Loc.Get("status.copied"));
        }
        catch (Exception exception)
        {
            _logger.Error("Clipboard copy command failed.", exception);
            SetStatus(Loc.Get("status.clipboardFailed"));
        }
    }

    private async Task CopySelectionHtmlAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _editorCommandStatus.SourceMode)
        {
            return;
        }

        try
        {
            var selection = await _editorHost.RequestSelectionExportAsync();
            if (string.IsNullOrEmpty(selection.Html))
            {
                SetStatus(Loc.Get("status.noTextToCopy"));
                return;
            }

            Clipboard.SetText(selection.Html, TextDataFormat.UnicodeText);
            SetStatus(Loc.Get("status.copied"));
        }
        catch (Exception exception)
        {
            _logger.Error("Copy HTML source command failed.", exception);
            SetStatus(Loc.Get("status.clipboardFailed"));
        }
    }

    private async Task PasteClipboardContentAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document?.IsReadOnly == true)
        {
            return;
        }

        try
        {
            var clipboardData = Clipboard.GetDataObject();
            if ((_editorCommandStatus.SourceMode || _editorCommandStatus.ExpandedSource)
                && TryGetClipboardPlainTextForSourceMode(clipboardData, out var sourcePlainText))
            {
                var result = await _editorHost.ExecuteCommandResultAsync("pasteText", sourcePlainText);
                SetPasteStatus(result, formattedRequested: false);
                return;
            }

            if (Clipboard.ContainsFileDropList())
            {
                await ImportImageFilesAsync(Clipboard.GetFileDropList().Cast<string>());
                return;
            }

            if (Clipboard.ContainsImage())
            {
                await ImportClipboardBitmapAsync();
                return;
            }

            if (!_editorCommandStatus.SourceMode
                && TryGetClipboardPlainTextForSourceMode(clipboardData, out var visualPlainText))
            {
                if (Clipboard.TryGetData<string>(DataFormats.Html, out var clipboardHtml)
                    && !string.IsNullOrWhiteSpace(clipboardHtml))
                {
                    var result = await _editorHost.ExecuteCommandResultAsync(
                        "pasteClipboard",
                        visualPlainText,
                        html: ClipboardHtmlFormatter.ExtractFragment(clipboardHtml));
                    SetPasteStatus(result, formattedRequested: true);
                    return;
                }

                var markdownResult = await _editorHost.ExecuteCommandResultAsync("pasteMarkdown", visualPlainText);
                SetPasteStatus(markdownResult, formattedRequested: false);
                return;
            }

            if (!Clipboard.ContainsText())
            {
                SetStatus(Loc.Get("status.noTextToPaste"));
                return;
            }

            var fallbackResult = await _editorHost.ExecuteCommandResultAsync(
                _editorCommandStatus.SourceMode ? "pasteText" : "pasteMarkdown",
                Clipboard.GetText(TextDataFormat.UnicodeText));
            SetPasteStatus(fallbackResult, formattedRequested: false);
        }
        catch (Exception exception)
        {
            _logger.Error("Clipboard paste command failed.", exception);
            SetStatus(Loc.Get("status.clipboardFailed"));
        }
    }

    private async Task PasteClipboardPlainTextAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document?.IsReadOnly == true)
        {
            return;
        }

        try
        {
            if (!TryGetClipboardPlainTextForSourceMode(Clipboard.GetDataObject(), out var plainText))
            {
                SetStatus(Loc.Get("status.noTextToPaste"));
                return;
            }

            var result = await _editorHost.ExecuteCommandResultAsync(
                _editorCommandStatus.SourceMode ? "pasteText" : "pasteMarkdown",
                plainText);
            SetPasteStatus(result, formattedRequested: false);
        }
        catch (Exception exception)
        {
            _logger.Error("Clipboard plain-text paste command failed.", exception);
            SetStatus(Loc.Get("status.clipboardFailed"));
        }

    }

    private void SetPasteStatus(EditorCommandResult result, bool formattedRequested)
    {
        if (!result.Success)
        {
            SetStatus(Loc.Get("status.pasteFailed"));
            return;
        }

        SetStatus(result.Outcome switch
        {
            "markdown" => Loc.Get("status.pastedMarkdown"),
            "normalized" => Loc.Get("status.pastedMarkdownNormalized"),
            "plainText" when !_editorCommandStatus.SourceMode && !_editorCommandStatus.ExpandedSource && !string.IsNullOrWhiteSpace(result.Error) =>
                Loc.Format("status.pastedPlainTextFallbackReason", result.Error),
            "plainText" when !_editorCommandStatus.SourceMode && !_editorCommandStatus.ExpandedSource => Loc.Get("status.pastedPlainTextFallback"),
            "formatted" => Loc.Get("status.pastedFormatted"),
            _ when formattedRequested => Loc.Get("status.pastedFormatted"),
            _ => Loc.Get("status.pastedPlainText"),
        });
    }

    private static bool TryGetClipboardPlainTextForSourceMode(IDataObject? clipboardData, out string text)
    {
        text = string.Empty;
        if (clipboardData is null)
        {
            return false;
        }

        if (TryGetClipboardString(clipboardData, DataFormats.UnicodeText, out text)
            || TryGetClipboardString(clipboardData, DataFormats.Text, out text)
            || TryGetClipboardHtmlPlainText(clipboardData, out text)
            || TryGetClipboardRtfPlainText(clipboardData, out text))
        {
            return text.Length > 0;
        }

        return false;
    }

    private static bool TryGetClipboardString(IDataObject clipboardData, string format, out string text)
    {
        text = string.Empty;
        if (!clipboardData.GetDataPresent(format, autoConvert: true))
        {
            return false;
        }

        text = clipboardData.GetData(format, autoConvert: true) as string ?? string.Empty;
        return text.Length > 0;
    }

    private static bool TryGetClipboardHtmlPlainText(IDataObject clipboardData, out string text)
    {
        text = string.Empty;
        if (!TryGetClipboardString(clipboardData, DataFormats.Html, out var html))
        {
            return false;
        }

        text = ClipboardHtmlFormatter.ExtractPlainText(html);
        return text.Length > 0;
    }

    private static bool TryGetClipboardRtfPlainText(IDataObject clipboardData, out string text)
    {
        text = string.Empty;
        if (!TryGetClipboardString(clipboardData, DataFormats.Rtf, out var rtf))
        {
            return false;
        }

        try
        {
            using var box = new RichTextBox { Rtf = rtf };
            text = box.Text;
            return text.Length > 0;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
