using System.Text.Json;
using MarkLeaf.Editor;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private Control CreateEditorHost()
    {
        var colors = ColorThemeService.GetActiveColors();
        var bg = colors.TryGetValue("bg-primary", out var c) ? c : SystemColors.Window;
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = bg,
        };
        _editorPanel = panel;
        var webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
            TabStop = true,
            AllowExternalDrop = true,
        };
        _webView = webView;
        var loadingView = new EditorLoadingView { Visible = false };
        _editorLoadingView = loadingView;
        panel.Controls.Add(webView);
        panel.Controls.Add(loadingView);
        panel.Visible = false;
        _editorHost = new EditorHostController(
            webView,
            loadingView,
            _editorSession,
            _logger,
            _options.EditorWebRoot ?? Path.Combine(AppContext.BaseDirectory, "EditorWeb"),
            _paths.WebView2UserDataDirectory,
            OnEditorStateChanged);
        _editorHost.Ready += (_, _) =>
        {
            // 必须先于 loadDocument 应用样式，确保文档渲染时排版即已就绪。
            _editorHost?.SendFindBarLocalization();
            _editorHost?.ApplyStyles(StyleService.BaseCss, StyleService.Styles, _markdownStyle);
            var e = _settings.Editor;
            _editorHost?.ApplyCssVariables(e.VisualLineHeight, e.VisualFontSize, e.VisualMaxContentWidth, e.SourceFontSize, e.SourceFontFamily, e.SourceCjkFontFamily, e.CjkLanguageTag.ToBcp47());
            _editorHost?.ApplySourceSettings(e.SourceIndentWidth);
            ApplyBlockHandleVisibility();
            SetZoomPercent(_settings.Appearance.RestoreZoomOnOpen ? _zoomPercent : 100);
            _editorHost?.ApplyAutoHideScrollbar(_settings.Appearance.AutoHideScrollbars);
            ApplySidebarAutoHideScrollbar();
            ApplySidebarColors();
        };
        _editorHost.Ready += (_, _) => BeginEditorSmokeIfRequested();
        _editorHost.Ready += (_, _) => LoadInitialDocumentIfNeeded();
        _editorHost.Ready += async (_, _) => await BeginDocumentSmokeIfRequestedAsync();
        _editorHost.Ready += (_, _) => HandleSmokeCrashExit();
        _editorHost.DocumentLoaded += (_, _) => ContinueEditorSmokeAfterLoad();
        _editorHost.DocumentLoaded += (_, _) => BeginEditorCommandSmokeIfRequested();
        _editorHost.DocumentLoaded += async (_, _) => await ContinueDocumentSmokeAfterLoadAsync();
        _editorHost.DocumentLoaded += (_, _) => SetMarkdownStyle(_markdownStyle);
        _editorHost.SnapshotReceived += (_, message) => CompleteEditorSmoke(message);
        _editorHost.SnapshotReceived += (_, message) => CompleteEditorCommandSmoke(message);
        _editorHost.DirtyChanged += OnEditorDirtyChanged;
        _editorHost.FindResultReceived += (_, result) => _findReplaceDialog?.ApplyResult(result);
        _editorHost.CommandStateChanged += OnEditorCommandStateChanged;
        _editorHost.EditorStatusChanged += OnEditorStatusChanged;
        _editorHost.ContextMenuRequested += OnEditorContextMenuRequested;
        _editorHost.BlockMenuRequested += OnEditorBlockMenuRequested;
        _editorHost.MathEditRequested += OnMathEditRequested;
        _editorHost.OutlineChanged += OnEditorOutlineChanged;
        _editorHost.OutlineSelectionChanged += OnEditorOutlineSelectionChanged;
        _editorHost.OpenLinkRequested += OnOpenLinkRequested;
        _editorHost.FilesDropped += OnEditorFilesDropped;
        _editorHost.PasteImageRequested += OnEditorPasteImageRequested;
        _editorHost.UnsafeEmphasisRequested += OnUnsafeEmphasisRequested;
        _editorHost.FootnoteDefinitionMissing += OnFootnoteDefinitionMissing;
        _editorHost.ZoomWheelRequested += (_, deltaY) =>
        {
            if (!_settings.Appearance.CtrlWheelZoom)
            {
                return;
            }
            SetZoomPercent(NextZoom(_zoomPercent, deltaY < 0 ? 1 : -1));
        };

        _editorHost.Ready += async (_, _) => await RevealEditorPanelAsync();

        return panel;
    }

    private async Task RevealEditorPanelAsync()
    {
        if (_editorPanel is { IsDisposed: false })
        {
            _editorPanel.Visible = true;
        }
    }

    private void OnEditorStateChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnEditorStateChanged);
            return;
        }

        _statusLabel.Text = _editorSession.State switch
        {
            EditorLifecycleState.Initializing => Loc.Get("editor.initializing"),
            EditorLifecycleState.LoadingPage => Loc.Get("editor.loadingPage"),
            EditorLifecycleState.WaitingForEditorReady => Loc.Get("editor.waitingReady"),
            EditorLifecycleState.Ready => Loc.Get("editor.ready"),
            EditorLifecycleState.Failed => Loc.Get("editor.failed"),
            _ => Loc.Get("statusBar.preparing"),
        };
        if (_editorSession.State == EditorLifecycleState.Failed)
        {
            _editorPanel.Visible = true;
        }
        _menuService.RefreshStates();

        if (_editorSession.State == EditorLifecycleState.Failed
            && !string.IsNullOrWhiteSpace(_options.EditorStateReportPath))
        {
            WriteEditorStateReport();
            BeginInvoke(Close);
        }
    }

    private void WriteEditorStateReport()
    {
        var report = new
        {
            LifecycleState = _editorSession.State.ToString(),
            EditorWebRoot = _options.EditorWebRoot,
            Status = _statusLabel.Text,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_options.EditorStateReportPath!)!);
        File.WriteAllText(
            _options.EditorStateReportPath!,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void OnEditorStatusChanged(object? sender, EditorStatus status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorStatusChanged(sender, status));
            return;
        }

        _editorStatus = status;
        RefreshPersistentStatusBar();
    }

    private void OnUnsafeEmphasisRequested(object? sender, UnsafeEmphasisRequest request)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnUnsafeEmphasisRequested(sender, request));
            return;
        }

        var preference = _settings.Editor.UnsafeEmphasisPreference;
        if (preference is "literal" or "html")
        {
            _editorHost?.ResolveUnsafeEmphasis(request.RequestId, preference);
            return;
        }

        using var dialog = new UnsafeEmphasisDialog(request.Kind);
        var result = ShowModal(() => dialog.ShowDialog(this));
        var action = result == DialogResult.OK ? dialog.Action : "literal";
        if (dialog.RememberChoice)
        {
            _settings.Editor.UnsafeEmphasisPreference = action;
            SaveSettings();
        }
        _editorHost?.ResolveUnsafeEmphasis(request.RequestId, action);
    }

    private void RefreshPersistentStatusBar()
    {
        ApplyStatusBarItemVisibility();
        _characterCountButton.Text = StatusBarFormatter.FormatCharacterCount(_editorStatus);
        _characterCountButton.Enabled = !_editorCommandStatus.SourceMode;
        _blockTypeLabel.Text = StatusBarFormatter.FormatBlockType(_editorStatus.BlockType);
        _positionLabel.Text = StatusBarFormatter.FormatPosition(_editorStatus);
        _encodingLabel.Text = _document is null
            ? "UTF-8"
            : StatusBarFormatter.FormatEncoding(_document.Encoding, _document.HasBom);
        _newLineLabel.Text = _document is null
            ? StatusBarFormatter.FormatNewLine(Environment.NewLine)
            : StatusBarFormatter.FormatNewLine(_document.NewLine);
        _modeButton.Text = _editorCommandStatus.SourceMode
            ? Loc.Get("statusBar.mode.source")
            : Loc.Get("statusBar.mode.visual");
        _modeButton.Enabled = !IsPlainTextDocument;
    }

    private void HandleSmokeCrashExit()
    {
        if (!_options.SmokeCrashExit) return;
        _logger.Info("Simulating abnormal shutdown for crash recovery test.");
        SetStatus(Loc.Get("status.simulatedCrash"));

        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            _logger.Info("Simulating crash exit now.");
            Environment.FailFast("MarkLeaf smoke crash exit.");
        };
        timer.Start();
    }

    private void BeginEditorSmokeIfRequested()
    {
        if (_editorSmokeStarted || string.IsNullOrWhiteSpace(_options.EditorSmokeReportPath))
        {
            return;
        }

        _editorSmokeStarted = true;
        _editorHost?.LoadDocument("# 阶段 3 通信检查\n\n初始内容。\n");
    }

    private void LoadInitialDocumentIfNeeded()
    {
        if (_editorSmokeStarted
            || !string.IsNullOrWhiteSpace(_options.DocumentSmokeInputPath)
            || _initialDocumentOpened
            || _editorHost?.IsDocumentLoaded != false)
        {
            return;
        }

        _document ??= _documentFileService.CreateNew(DefaultNewLine);
        if (!string.IsNullOrWhiteSpace(_options.EditorCommandSmoke))
        {
            _document.Markdown = "段落命令";
        }
        LoadDocumentIntoEditor(_document);
    }

    private void BeginEditorCommandSmokeIfRequested()
    {
        if (_editorCommandSmokeStarted
            || string.IsNullOrWhiteSpace(_options.EditorCommandSmoke)
            || string.IsNullOrWhiteSpace(_options.EditorCommandReportPath))
        {
            return;
        }

        _editorCommandSmokeStarted = true;
        _editorHost?.ExecuteCommand(_options.EditorCommandSmoke);
        _editorHost?.RequestSnapshot();
    }

    private void ContinueEditorSmokeAfterLoad()
    {
        if (!_editorSmokeStarted)
        {
            return;
        }

        _editorHost?.ExecuteCommand("appendText", " 通信桥已确认。");
        _editorHost?.RequestSnapshot();
    }

    private void CompleteEditorSmoke(EditorMessage message)
    {
        if (!_editorSmokeStarted || string.IsNullOrWhiteSpace(_options.EditorSmokeReportPath))
        {
            return;
        }

        var markdown = message.Payload.TryGetProperty("markdown", out var markdownElement)
            ? markdownElement.GetString() ?? string.Empty
            : string.Empty;
        var report = new
        {
            LifecycleState = _editorSession.State.ToString(),
            DocumentId = _editorSession.DocumentId,
            Revision = _editorSession.ConfirmedRevision,
            SnapshotRequestMatched = true,
            ContainsInitialText = markdown.Contains("初始内容。", StringComparison.Ordinal),
            ContainsCommandText = markdown.Contains("通信桥已确认。", StringComparison.Ordinal),
            SnapshotLength = markdown.Length,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_options.EditorSmokeReportPath)!);
        File.WriteAllText(
            _options.EditorSmokeReportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        _closeApproved = true;
        BeginInvoke(Close);
    }

    private void CompleteEditorCommandSmoke(EditorMessage message)
    {
        if (!_editorCommandSmokeStarted || string.IsNullOrWhiteSpace(_options.EditorCommandReportPath))
        {
            return;
        }

        var markdown = message.Payload.TryGetProperty("markdown", out var markdownElement)
            ? markdownElement.GetString() ?? string.Empty
            : string.Empty;
        var report = new
        {
            Command = _options.EditorCommandSmoke,
            DocumentLoaded = _editorHost?.IsDocumentLoaded == true,
            Revision = _editorSession.ConfirmedRevision,
            Markdown = markdown,
            _editorCommandStatus.CanUndo,
            _editorCommandStatus.CanRedo,
            _editorCommandStatus.HasSelection,
            _editorCommandStatus.Bold,
            _editorCommandStatus.Paragraph,
            _editorCommandStatus.HeadingLevel,
            _editorCommandStatus.TaskList,
            _editorCommandStatus.InTable,
            _editorCommandStatus.TableAlign,
            ContainsVisibleLink = markdown.Contains(
                "[https://example.com/regression](https://example.com/regression)",
                StringComparison.Ordinal),
            ContainsTemporaryText = markdown.Contains("临时修改", StringComparison.Ordinal),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_options.EditorCommandReportPath)!);
        File.WriteAllText(
            _options.EditorCommandReportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        _closeApproved = true;
        BeginInvoke(Close);
    }
}
