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

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
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

    private CommandState GetCommandState(AppCommand command)
    {
        if (command == AppCommand.UseIndependentOutlineSidebar
            && _outlineAnimationTargetDetached is not null)
        {
            return new CommandState(false, _outlineDetached);
        }

        if (command is >= AppCommand.OpenRecentWorkspace1 and <= AppCommand.OpenRecentWorkspace8)
        {
            var index = (int)command - (int)AppCommand.OpenRecentWorkspace1;
            return new CommandState(index < GetRecentWorkspaces().Count);
        }

        if (command is >= AppCommand.OpenRecentFile1 and <= AppCommand.OpenRecentFile8)
        {
            var index = (int)command - (int)AppCommand.OpenRecentFile1;
            return new CommandState(index < GetRecentFiles().Count);
        }

        if (command == AppCommand.CloseFolder)
        {
            return new CommandState(_workspaceRoot is not null);
        }

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
            StatusBarVisible: _statusStrip?.Visible != false,
            OutlineActive: _sidebarActiveOutline,
            ShowCodeHighlight: _settings.Appearance.ShowCodeHighlight,
            ListViewActive: _workspaceListViewActive,
            IndependentOutlineSidebar: _outlineDetached,
            EditorActions: _editorCommandStatus.Actions);
        var state = CommandStateResolver.Resolve(command, context);
        if (command is AppCommand.Paste or AppCommand.PastePlainText)
            return state with { IsEnabled = state.IsEnabled && HasClipboardContent() };

        return state;
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

    private void ExecuteCommand(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.NewDocument:
                _ = NewDocumentAsync(NewDocumentKind.Markdown);
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
            case AppCommand.ExportWithLastSettings:
                _ = ExportWithLastSettingsAsync();
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
                _ = ExecuteClipboardCopyAsync(ClipboardCopyMode.Formatted, cut: true);
                break;
            case AppCommand.Copy:
                _ = ExecuteClipboardCopyAsync(ClipboardCopyMode.Formatted, cut: false);
                break;
            case AppCommand.CopyMarkdown:
                _ = ExecuteClipboardCopyAsync(ClipboardCopyMode.Markdown, cut: false);
                break;
            case AppCommand.CopyPlainText:
                _ = ExecuteClipboardCopyAsync(ClipboardCopyMode.PlainText, cut: false);
                break;
            case AppCommand.CopyHtml:
                _ = CopySelectionHtmlAsync();
                break;
            case AppCommand.Paste:
                _ = PasteClipboardContentAsync();
                break;
            case AppCommand.PastePlainText:
                _ = PasteClipboardPlainTextAsync();
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
            case AppCommand.SwitchToWorkspace:
                ShowSidebarView(outline: false);
                SetStatus(Loc.Get("status.switchedToWorkspace"));
                break;
            case AppCommand.SwitchToOutline:
                ShowSidebarView(outline: true);
                SetStatus(Loc.Get("status.switchedToOutline"));
                break;
            case AppCommand.UseIndependentOutlineSidebar:
                if (_outlineDetached)
                    MergeOutlineSidebar();
                else
                    DetachOutlineSidebar();
                break;
            case AppCommand.ViewTree:
                if (_workspaceListViewActive) ToggleWorkspaceView();
                break;
            case AppCommand.ViewList:
                if (!_workspaceListViewActive) ToggleWorkspaceView();
                break;
            case AppCommand.ShowStatusBar:
                if (_statusStrip is not null) _statusStrip.Visible = !_statusStrip.Visible;
                break;
            case AppCommand.ShowShortcuts:
                ShowShortcutHelp();
                break;
            case AppCommand.InstallOptionalFonts:
                ShowOptionalFonts();
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
                ShowWelcome();
                break;
            case AppCommand.CheckForUpdates:
                _ = CheckForUpdatesAsync();
                break;
            case AppCommand.LearnMarkdown:
                ExternalLinkService.Open("https://www.runoob.com/markdown/md-tutorial.html");
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
                InsertFootnote();
                break;
            case AppCommand.ResetFootnoteLabel:
                ResetFootnoteLabel();
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
                _ = SelectAndInsertImagesAsync();
                break;
            case AppCommand.InsertImageFromUrl:
                _ = InsertImageFromUrlAsync();
                break;
            case AppCommand.InsertTable:
                InsertTable();
                break;
            case AppCommand.ChangeImage:
                _ = ChangeImageAsync();
                break;
            case AppCommand.SaveImageAs:
                _ = SaveImageAsAsync();
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

    private void ShowTopLevelMainMenu(int menuIndex, Point screenLocation)
    {
        var bounds = _documentTabBar.GetTopLevelMenuScreenBounds();
        var currentIndex = menuIndex;
        try
        {
            while (currentIndex >= 0 && currentIndex < bounds.Count)
            {
                _documentTabBar.SetOpenTopLevelMenuIndex(currentIndex);
                var commandId = _menuService.ShowTopLevelMainMenu(
                    Handle,
                    screenLocation,
                    currentIndex,
                    bounds,
                    out var nextMenuIndex);
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

    private void OnEditorContextMenuRequested(object? sender, EditorContextMenuRequest request)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorContextMenuRequested(sender, request));
            return;
        }

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
            _menuService.ShowEditorContextMenu(Handle, screenPoint, status);
        }
        finally
        {
            // 原生菜单关闭时，同步隐藏前端格式菜单，保证两者同现同隐。
            _editorHost.ExecuteCommand("hideFormatMenu");
        }
    }

    private void OnEditorBlockMenuRequested(object? sender, EditorBlockMenuRequest request)
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
        _menuService.ShowBlockHandleMenu(Handle, screenPoint, _editorCommandStatus);
        _editorHost.ClearBlockHighlight();
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

            var prompt = Loc.Format("update.available", release.VersionText, release.BuildNumber);
            if (ShowMessage(this, prompt, "MarkLeaf", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
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
