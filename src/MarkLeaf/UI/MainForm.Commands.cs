using System.Runtime.InteropServices;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.ExternalLinks;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (_focusMode && keyData == Keys.Escape)
        {
            ToggleFocusMode();
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

        if (command is AppCommand.NewWindow or AppCommand.OpenDocumentInNewWindow)
        {
            return new CommandState(true);
        }

        if (command == AppCommand.Paste)
        {
            return new CommandState(_editorHost?.IsDocumentLoaded == true && HasClipboardContent());
        }

        var context = new CommandContext(
            DocumentAvailable: _document is not null,
            EditorReady: _editorHost?.IsDocumentLoaded == true,
            CanUndo: _editorCommandStatus.CanUndo,
            CanRedo: _editorCommandStatus.CanRedo,
            HasSelection: _editorCommandStatus.HasSelection,
            SidebarVisible: !_sidebarSplit.Panel1Collapsed,
            FocusMode: _focusMode,
            SourceMode: _editorCommandStatus.SourceMode,
            ParagraphActive: _editorCommandStatus.Paragraph,
            HeadingLevel: _editorCommandStatus.HeadingLevel,
            BoldActive: _editorCommandStatus.Bold,
            ItalicActive: _editorCommandStatus.Italic,
            UnderlineActive: _editorCommandStatus.Underline,
            StrikeActive: _editorCommandStatus.Strike,
            InlineCodeActive: _editorCommandStatus.InlineCode,
            LinkActive: _editorCommandStatus.Link,
            QuoteActive: _editorCommandStatus.Blockquote,
            CodeBlockActive: _editorCommandStatus.CodeBlock,
            BulletListActive: _editorCommandStatus.BulletList,
            OrderedListActive: _editorCommandStatus.OrderedList,
            TaskListActive: _editorCommandStatus.TaskList,
            InTable: _editorCommandStatus.InTable,
            TableAlign: _editorCommandStatus.TableAlign,
            ImageSelected: _editorCommandStatus.ImageSelected,
            DocumentSaved: _document?.FilePath is not null,
            StatusBarVisible: _statusStrip?.Visible != false,
            OutlineActive: _sidebarActiveOutline,
            FollowSystemColorMode: _settings.Appearance.FollowSystemColorMode);
        var state = CommandStateResolver.Resolve(command, context);
        if (state.IsEnabled
            && context.EditorReady
            && IsEditorCommand(command)
            && command != AppCommand.InsertImage
            && command != AppCommand.InsertImageFromUrl
            && command is not AppCommand.Cut
                and not AppCommand.Copy
                and not AppCommand.CopyMarkdown
                and not AppCommand.CopyPlainText
                and not AppCommand.Paste
            && command is not AppCommand.Find and not AppCommand.Replace and not AppCommand.ToggleSourceMode
            && !TryMapEditorCommand(command, out _))
        {
            return new CommandState(false, state.IsChecked);
        }

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
                _ = NewDocumentAsync();
                break;
            case AppCommand.NewWindow:
                StartNewWindow();
                break;
            case AppCommand.OpenDocument:
                _ = OpenDocumentAsync();
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
            case AppCommand.ExportDocument:
                _ = ExportDocumentAsync();
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
            case AppCommand.Paste:
                _ = PasteClipboardContentAsync();
                break;
            case AppCommand.Find:
                _editorHost?.ExecuteCommand("find");
                break;
            case AppCommand.Replace:
                _editorHost?.ExecuteCommand("replace");
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
            case AppCommand.SwitchToWorkspace:
                ShowSidebarView(outline: false);
                SetStatus(Loc.Get("status.switchedToWorkspace"));
                break;
            case AppCommand.SwitchToOutline:
                ShowSidebarView(outline: true);
                SetStatus(Loc.Get("status.switchedToOutline"));
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
            case AppCommand.ShowChangelog:
                ShowChangelog();
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
            case AppCommand.InsertImage:
                _ = SelectAndInsertImagesAsync();
                break;
            case AppCommand.InsertImageFromUrl:
                _ = InsertImageFromUrlAsync();
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
                    _ = OpenWorkspaceAsync(workspacePath);
                    break;
                }

                if (_editorHost?.IsDocumentLoaded == true && TryMapEditorCommand(command, out var editorCommand))
                {
                    _editorHost.ExecuteCommand(
                        editorCommand,
                        applyToCurrentTextBlockWhenEmpty: _editorContextMenuActive && IsInlineFormatCommand(command));
                    SetStatus(CommandStatusFormatter.FormatExecuted(command));
                    break;
                }

                _logger.Warning($"Command has no available handler: {command}.");
                return;
        }

        _logger.Info($"Command executed: {command}.");
        _menuService.RefreshStates();
    }

    private static bool TryMapEditorCommand(AppCommand command, out string editorCommand)
    {
        editorCommand = command switch
        {
            AppCommand.Undo => "undo",
            AppCommand.Redo => "redo",
            AppCommand.ToggleBold => "toggleBold",
            AppCommand.ToggleItalic => "toggleItalic",
            AppCommand.ToggleUnderline => "toggleUnderline",
            AppCommand.ToggleStrike => "toggleStrike",
            AppCommand.ToggleInlineCode => "toggleCode",
            AppCommand.PromoteHeading => "promoteHeading",
            AppCommand.DemoteHeading => "demoteHeading",
            AppCommand.SetParagraph => "setParagraph",
            AppCommand.SetHeading1 => "setHeading1",
            AppCommand.SetHeading2 => "setHeading2",
            AppCommand.SetHeading3 => "setHeading3",
            AppCommand.SetHeading4 => "setHeading4",
            AppCommand.SetHeading5 => "setHeading5",
            AppCommand.SetHeading6 => "setHeading6",
            AppCommand.InsertLink => "setLink",
            AppCommand.RotateImageClockwise => "rotateImageClockwise",
            AppCommand.ToggleQuote => "toggleBlockquote",
            AppCommand.ToggleCodeBlock => "toggleCodeBlock",
            AppCommand.InsertHorizontalRule => "insertHorizontalRule",
            AppCommand.ToggleBulletList => "toggleBulletList",
            AppCommand.ToggleOrderedList => "toggleOrderedList",
            AppCommand.ToggleTaskList => "toggleTaskList",
            AppCommand.InsertTable => "insertTable",
            AppCommand.AddTableRowBefore => "addRowBefore",
            AppCommand.AddTableRowAfter => "addRowAfter",
            AppCommand.DeleteTableRow => "deleteRow",
            AppCommand.AddTableColumnBefore => "addColumnBefore",
            AppCommand.AddTableColumnAfter => "addColumnAfter",
            AppCommand.DeleteTableColumn => "deleteColumn",
            AppCommand.AlignTableLeft => "alignTableLeft",
            AppCommand.AlignTableCenter => "alignTableCenter",
            AppCommand.AlignTableRight => "alignTableRight",
            AppCommand.DeleteTable => "deleteTable",
            AppCommand.InsertLineBefore => "insertLineBefore",
            AppCommand.InsertLineAfter => "insertLineAfter",
            _ => string.Empty,
        };
        return editorCommand.Length > 0;
    }

    private static bool IsEditorCommand(AppCommand command)
    {
        return command is >= AppCommand.Undo and <= AppCommand.Replace
            || command is >= AppCommand.SetParagraph and <= AppCommand.DeleteTable
            || command is AppCommand.ToggleUnderline or AppCommand.ToggleStrike or AppCommand.ToggleInlineCode
                or AppCommand.PromoteHeading or AppCommand.DemoteHeading
            || command is AppCommand.InsertLineBefore or AppCommand.InsertLineAfter
            || command == AppCommand.ToggleSourceMode;
    }

    private static bool IsInlineFormatCommand(AppCommand command) =>
        command is AppCommand.ToggleBold or AppCommand.ToggleItalic;

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
        try
        {
            _editorContextMenuActive = true;
            _menuService.ShowEditorContextMenu(Handle, screenPoint);
        }
        finally
        {
            _editorContextMenuActive = false;
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
        _menuService.ShowBlockHandleMenu(Handle, screenPoint);
        _editorHost.ClearBlockHighlight();
    }

    private void OnOpenLinkRequested(object? sender, string url)
    {
        try
        {
            ExternalLinkService.Open(url);
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

    private async Task PasteClipboardContentAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        try
        {
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
                && Clipboard.TryGetData<string>(DataFormats.Html, out var clipboardHtml))
            {
                if (!string.IsNullOrWhiteSpace(clipboardHtml))
                {
                    _editorHost.ExecuteCommand("pasteHtml", ClipboardHtmlFormatter.ExtractFragment(clipboardHtml));
                    SetStatus(Loc.Get("status.pastedFormatted"));
                    return;
                }
            }

            if (!Clipboard.ContainsText())
            {
                SetStatus(Loc.Get("status.noTextToPaste"));
                return;
            }

            _editorHost.ExecuteCommand("pasteText", Clipboard.GetText(TextDataFormat.UnicodeText));
            SetStatus(Loc.Get("status.pastedPlainText"));
        }
        catch (Exception exception)
        {
            _logger.Error("Clipboard paste command failed.", exception);
            SetStatus(Loc.Get("status.clipboardFailed"));
        }
    }
}
