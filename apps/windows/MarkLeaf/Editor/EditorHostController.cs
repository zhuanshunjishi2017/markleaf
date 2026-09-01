using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MarkLeaf.Documents;
using MarkLeaf.Services;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.Editor;

internal sealed class EditorHostController : IDisposable
{
    private readonly WebView2 _webView;
    private readonly EditorLoadingView _loadingView;
    private readonly EditorSession _session;
    private readonly IAppLogger _logger;
    private readonly string _editorWebPath;
    private readonly string _webView2UserDataDirectory;
    private readonly Uri _editorUri;
    private readonly Action _stateChanged;
    private readonly Queue<Action> _readyActions = new();
    private readonly Dictionary<string, TaskCompletionSource<EditorSnapshot>> _snapshotRequests =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _commandRequests =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<EditorSelectionExport>> _selectionExportRequests =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<string>> _exportRequests =
        new(StringComparer.Ordinal);
    private readonly Stopwatch _initializationTimer = new();
    private CancellationTokenSource? _initializationCancellation;
    private CancellationTokenSource? _readyTimeoutCancellation;
    private bool _eventsAttached;
    private bool _disposed;
    private bool _failureShown;
    private bool _documentLoaded;
    private Guid? _lastDocumentId;
    private long _lastDocumentRevision;
    private string _lastDocumentMarkdown = string.Empty;
    private bool _lastDocumentReadOnly;
    private string? _lastDocumentType;
    private bool _replayDocumentAfterNavigation;

    public event EventHandler? Ready;

    public event EventHandler<EditorMessage>? DocumentLoaded;

    public event EventHandler<EditorMessage>? SnapshotReceived;

    public event EventHandler<EditorMessage>? DirtyChanged;

    public event EventHandler<EditorCommandStatus>? CommandStateChanged;

    public event EventHandler<EditorStatus>? EditorStatusChanged;

    public event EventHandler<EditorContextMenuRequest>? ContextMenuRequested;

    public event EventHandler<EditorBlockMenuRequest>? BlockMenuRequested;

    public event EventHandler? MermaidEditRequested;

    public event EventHandler<EditorFindResult>? FindResultReceived;

    public event EventHandler<EditorOutline>? OutlineChanged;

    public event EventHandler<int?>? OutlineSelectionChanged;

    public event EventHandler<string>? OpenLinkRequested;

    public event EventHandler<DroppedFiles>? FilesDropped;

    public event EventHandler? PasteImageRequested;

    public event EventHandler<double>? ZoomWheelRequested;

    public event EventHandler<UnsafeEmphasisRequest>? UnsafeEmphasisRequested;

    public event EventHandler? FootnoteDefinitionMissing;
    public event EventHandler? FootnoteReferenceMissing;

    public EditorHostController(
        WebView2 webView,
        EditorLoadingView loadingView,
        EditorSession session,
        IAppLogger logger,
        string editorWebPath,
        string webView2UserDataDirectory,
        Action stateChanged)
    {
        _webView = webView;
        _loadingView = loadingView;
        _session = session;
        _logger = logger;
        _editorWebPath = editorWebPath;
        _webView2UserDataDirectory = webView2UserDataDirectory;
        _editorUri = CreateVersionedEditorUri(editorWebPath);
        _stateChanged = stateChanged;
        _loadingView.RetryRequested += (_, _) => _ = RetryAsync();
    }

    public EditorLifecycleState State => _session.State;

    public bool IsReady => State == EditorLifecycleState.Ready;

    public bool IsDocumentLoaded => IsReady && _documentLoaded;

    public Point EditorPointToScreen(EditorContextMenuRequest request)
    {
        // 前端格式菜单占据右键位置上方一段高度，原生菜单需在其下方偏移 menuHeight + 10px 间距。
        const double contextMenuGap = 10;
        return EditorPointToScreen(request.ClientX, request.ClientY + request.MenuHeight + contextMenuGap);
    }

    public Point EditorPointToScreen(EditorBlockMenuRequest request)
    {
        return EditorPointToScreen(request.ClientX, request.ClientY);
    }

    private Point EditorPointToScreen(double clientX, double clientY)
    {
        var devicePoint = EditorCoordinateConverter.CssToDevicePoint(
            clientX,
            clientY,
            _webView.DeviceDpi,
            _webView.ZoomFactor);
        return _webView.PointToScreen(devicePoint);
    }

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State != EditorLifecycleState.NotStarted)
        {
            return;
        }

        if (!File.Exists(Path.Combine(_editorWebPath, "index.html")))
        {
            Fail(Loc.Get("editor.resourceMissing"), null);
            return;
        }

        _initializationTimer.Restart();
        _session.TransitionTo(EditorLifecycleState.Initializing);
        ShowLoading(Loc.Get("editor.initMessage"), Loc.Get("editor.initDetail"));
        NotifyStateChanged();

        try
        {
            _initializationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Directory.CreateDirectory(_webView2UserDataDirectory);
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _webView2UserDataDirectory).WaitAsync(_initializationCancellation.Token);
            var controllerOptions = environment.CreateCoreWebView2ControllerOptions();
            controllerOptions.AllowHostInputProcessing = true;
            var themeColors = ColorThemeService.GetActiveColors();
            if (themeColors.TryGetValue("bg-primary", out var bgColor))
            {
                controllerOptions.DefaultBackgroundColor = bgColor;
                _loadingView.SetThemeBackground(bgColor);
            }
            await _webView.EnsureCoreWebView2Async(environment, controllerOptions).WaitAsync(_initializationCancellation.Token);
            ConfigureCoreWebView2();

            _session.TransitionTo(EditorLifecycleState.LoadingPage);
            ShowLoading(Loc.Get("editor.loadingMessage"), Loc.Get("editor.loadingDetail"));
            NotifyStateChanged();
            _webView.Source = _editorUri;
            BeginReadyTimeout();
        }
        catch (OperationCanceledException exception)
        {
            Fail(Loc.Get("editor.timeout"), exception);
        }
        catch (Exception exception)
        {
            Fail(Loc.Get("editor.runtimeFailed"), exception);
        }
    }

    public async Task RetryAsync()
    {
        if (State != EditorLifecycleState.Failed)
        {
            return;
        }

        _initializationCancellation?.Cancel();
        _readyTimeoutCancellation?.Cancel();
        _session.ResetForRetry();
        _failureShown = false;
        _webView.Visible = false;
        _loadingView.ShowLoading(Loc.Get("editor.retrying"), Loc.Get("editor.retryingDetail"));
        NotifyStateChanged();
        await InitializeAsync();
    }

    public void LoadDocument(string markdown)
    {
        LoadDocument(Guid.NewGuid(), 0, markdown);
    }

    public void LoadDocument(Guid documentId, long revision, string markdown, bool readOnly = false, string? documentType = null)
    {
        _lastDocumentId = documentId;
        _lastDocumentRevision = revision;
        _lastDocumentMarkdown = markdown;
        _lastDocumentReadOnly = readOnly;
        _lastDocumentType = documentType;
        EnqueueOrRun(() =>
        {
            _session.StartDocument(documentId, revision);
            _documentLoaded = false;
            Post("loadDocument", new { markdown, readOnly, documentType });
        });
    }

    public void SetDocumentType(string documentType)
    {
        EnqueueOrRun(() => Post("setDocumentType", new { documentType }));
    }

    public string RequestSnapshot()
    {
        var requestId = Guid.NewGuid().ToString("N");
        EnqueueOrRun(() =>
        {
            var registeredId = _session.RegisterRequest("snapshot", requestId);
            Post("requestSnapshot", requestId: registeredId);
        });
        return requestId;
    }

    public async Task<EditorSnapshot> RequestSnapshotAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<EditorSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _snapshotRequests.Add(requestId, completion);
        EnqueueOrRun(() =>
        {
            var registeredId = _session.RegisterRequest("snapshot", requestId);
            Post("requestSnapshot", requestId: registeredId);
        });

        using var timeoutCancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCancellation.Token,
            cancellationToken);
        using var registration = linkedCancellation.Token.Register(
            () => completion.TrySetCanceled(linkedCancellation.Token));
        try
        {
            return await completion.Task;
        }
        finally
        {
            _snapshotRequests.Remove(requestId);
        }
    }

    public void ApplyCssVariables(float lineHeight, int fontSize, int maxWidth, int sourceFontSize, string sourceFontFamily = "", string sourceCjkFontFamily = "", string cjkLang = "", bool visualCjkAutoSpacing = true)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceFontFamily))
            parts.Add($"\"{sourceFontFamily.Replace("\"", "\\\"")}\"");
        if (!string.IsNullOrWhiteSpace(sourceCjkFontFamily))
            parts.Add($"\"{sourceCjkFontFamily.Replace("\"", "\\\"")}\"");
        parts.Add("monospace");
        var fontFamilyValue = string.Join(", ", parts);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            lineHeight = CultureInvariant($"{lineHeight:F2}"),
            fontSize = $"{fontSize}px",
            maxWidth = $"{maxWidth}px",
            sourceFontSize = $"{sourceFontSize}px",
            sourceFontFamily = fontFamilyValue,
            cjkLanguage = cjkLang,
            visualCjkAutoSpacing,
            usePointerAnchor = false,
            anchorX = (double?)null,
            anchorY = (double?)null,
        });
        var script = $$"""
            (() => {
              const payload = {{payload}};
              if (typeof window.__markleafApplyVisualVariables === 'function') {
                window.__markleafApplyVisualVariables(payload);
              } else {
                document.documentElement.style.setProperty('--ml-line-height', payload.lineHeight);
                document.documentElement.style.setProperty('--ml-font-size', payload.fontSize);
                document.documentElement.style.setProperty('--ml-max-width', payload.maxWidth);
                document.documentElement.style.setProperty('--ml-source-font-size', payload.sourceFontSize);
                document.documentElement.style.setProperty('--ml-source-font-family', payload.sourceFontFamily);
                document.documentElement.setAttribute('lang', payload.cjkLanguage);
                document.documentElement.style.setProperty('--ml-cjk-lang', payload.cjkLanguage);
                document.documentElement.classList.toggle('markleaf-cjk-autospace', payload.visualCjkAutoSpacing);
              }
            })();
            """;
        EnqueueOrRun(() =>
        {
            if (_webView.CoreWebView2 is not null)
                _webView.CoreWebView2.ExecuteScriptAsync(script);
        });
    }

    public void ApplySourceSettings(int indentWidth)
    {
        EnqueueOrRun(() => Post("command", new { command = "setSourceIndent", text = indentWidth.ToString() }));
    }

    public void ApplyAutoConvertUnsafeEmphasis(bool enabled)
    {
        EnqueueOrRun(() => Post("command", new { command = "setAutoConvertUnsafeEmphasis", text = enabled ? "1" : "0" }));
    }

    /// <summary>
    /// WebView2 AreBrowserAcceleratorKeysEnabled=false 会屏蔽 Tab，
    /// 因此由宿主在 WinForms 层拦截并手动注入 Tab 键事件。
    /// </summary>
    public void ForwardTab(bool shift = false)
    {
        var shiftArg = shift ? "true" : "";
        var script = $"window.__markleaf_tab__?.({shiftArg})";
        EnqueueOrRun(() =>
        {
            if (_webView.CoreWebView2 is not null)
                _webView.CoreWebView2.ExecuteScriptAsync(script);
        });
    }

    public void ApplyAutoHideScrollbar(bool enabled)
    {
        EnqueueOrRun(() => Post("command", new { command = "setAutoHideScrollbar", text = enabled ? "1" : "0" }));
    }

    public void ApplyBlockHandleVisibility(bool enabled)
    {
        EnqueueOrRun(() => Post("command", new { command = "setBlockHandleVisible", text = enabled ? "1" : "0" }));
    }

    public void SetZoomPercent(int percent)
    {
        var zoomFactor = Math.Clamp(percent, 50, 200) / 100.0;
        EnqueueOrRun(() => _webView.ZoomFactor = zoomFactor);
    }

    public void SetWindowActive(bool active)
    {
        EnqueueOrRun(() =>
        {
            if (_webView.CoreWebView2 is null)
                return;

            _webView.CoreWebView2.ExecuteScriptAsync(
                $"window.__markleafSetWindowActive?.({(active ? "true" : "false")});");
        });
    }

    public void ExecuteExpandedSourceCommand(string command, string? text = null)
    {
        if (command is "undo" or "redo")
        {
            EnqueueOrRun(() => Post("command", new { command }));
            return;
        }

        var script = command switch
        {
            "copy" => "(() => { const target = document.querySelector('.markleaf-expanded-source-editor'); target?.focus(); return document.execCommand('copy'); })()",
            "cut" => "(() => { const target = document.querySelector('.markleaf-expanded-source-editor'); target?.focus(); return document.execCommand('cut'); })()",
            "paste" when text is not null => $"(() => {{ const target = document.querySelector('.markleaf-expanded-source-editor'); if (!target) return false; target.focus(); return document.execCommand('insertText', false, {System.Text.Json.JsonSerializer.Serialize(text)}); }})()",
            "paste" => "(() => { const target = document.querySelector('.markleaf-expanded-source-editor'); target?.focus(); return document.execCommand('paste'); })()",
            "selectAll" => "(() => { const target = document.querySelector('.markleaf-expanded-source-editor'); if (!target) return false; target.focus(); const selection = window.getSelection(); const range = document.createRange(); range.selectNodeContents(target); selection?.removeAllRanges(); selection?.addRange(range); return true; })()",
            _ => "false",
        };
        EnqueueOrRun(() => _webView.CoreWebView2?.ExecuteScriptAsync($"(() => {{ return {script}; }})()"));
    }

    public async Task RestartEditorAsync()
    {
        if (!IsDocumentLoaded)
        {
            return;
        }

        try
        {
            var snapshot = await RequestSnapshotAsync(TimeSpan.FromSeconds(10));
            _lastDocumentMarkdown = snapshot.Markdown;
            _lastDocumentRevision = snapshot.Revision;
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            _logger.Warning($"Editor restart snapshot failed: {exception.Message}.");
        }

        EnqueueOrRun(() =>
        {
            if (_webView.CoreWebView2 is null)
            {
                return;
            }

            _replayDocumentAfterNavigation = _lastDocumentId is not null;
            _documentLoaded = false;
            _webView.CoreWebView2.Navigate(_editorUri.ToString());
        });
    }

    public void SetEditorFocusMode(bool enabled)
    {
        EnqueueOrRun(() => Post("command", new { command = "setEditorFocusMode", text = enabled ? "1" : "0" }));
    }

    public void SetEditorTypewriterMode(bool enabled)
    {
        EnqueueOrRun(() => Post("command", new { command = "setEditorTypewriterMode", text = enabled ? "1" : "0" }));
    }

    public void ApplyStyles(string baseCss, IReadOnlyList<StyleDefinition> styles, string activeStyle)
    {
        var payload = new
        {
            baseCss,
            colorThemeCss = ColorThemeService.GetActiveThemeCss(),
            styles = styles.Select(style => new
            {
                style.Id,
                style.DisplayName,
                style.Css,
                style.DependsOn,
            }),
            activeStyle,
            // 混合前端右键菜单仅 Windows 端启用。
            frontendFormatMenu = true,
        };
        EnqueueOrRun(() => Post("applyStyles", payload));
    }

    public void SendFindBarLocalization()
    {
        var payload = new
        {
            find = Loc.Get("findBar.find"),
            findLabel = Loc.Get("findBar.findLabel"),
            replaceWith = Loc.Get("findBar.replaceWith"),
            replaceLabel = Loc.Get("findBar.replaceLabel"),
            caseSensitive = Loc.Get("findBar.caseSensitive"),
            wholeWord = Loc.Get("findBar.wholeWord"),
            previous = Loc.Get("findBar.previous"),
            next = Loc.Get("findBar.next"),
            replace = Loc.Get("findBar.replace"),
            replaceAll = Loc.Get("findBar.replaceAll"),
            close = Loc.Get("findBar.close"),
            closeLabel = Loc.Get("findBar.closeLabel"),
            replaced = Loc.Get("findBar.replaced"),
            noResults = Loc.Get("findBar.noResults"),
            blockParagraph = Loc.Get("blockHandle.paragraph"),
            blockHeading1 = Loc.Get("blockHandle.heading1"),
            blockHeading2 = Loc.Get("blockHandle.heading2"),
            blockHeading3 = Loc.Get("blockHandle.heading3"),
            blockHeading4 = Loc.Get("blockHandle.heading4"),
            blockHeading5 = Loc.Get("blockHandle.heading5"),
            blockHeading6 = Loc.Get("blockHandle.heading6"),
            blockBulletList = Loc.Get("blockHandle.bulletList"),
            blockOrderedList = Loc.Get("blockHandle.orderedList"),
            blockTaskList = Loc.Get("blockHandle.taskList"),
            blockBlockquote = Loc.Get("blockHandle.blockquote"),
            blockCodeBlock = Loc.Get("blockHandle.codeBlock"),
            blockTable = Loc.Get("blockHandle.table"),
            blockFootnote = Loc.Get("blockHandle.footnote"),
            blockAlert = Loc.Get("blockHandle.alert"),
            alertNote = Loc.Get("menu.paragraph.alertNote"),
            alertTip = Loc.Get("menu.paragraph.alertTip"),
            alertImportant = Loc.Get("menu.paragraph.alertImportant"),
            alertWarning = Loc.Get("menu.paragraph.alertWarning"),
            alertCaution = Loc.Get("menu.paragraph.alertCaution"),
            formatPromoteHeading = Loc.Get("formatMenu.promoteHeading"),
            formatDemoteHeading = Loc.Get("formatMenu.demoteHeading"),
        };
        EnqueueOrRun(() => Post("localizeFindBar", payload));
    }

    public void ExecuteCommand(
        string command,
        string? text = null,
        bool applyToCurrentTextBlockWhenEmpty = false)
    {
        EnqueueOrRun(() => Post("command", new { command, text, applyToCurrentTextBlockWhenEmpty }));
    }

    public void ClearBlockHighlight()
    {
        ExecuteCommand("clearBlockHighlight");
    }

    public void ResolveUnsafeEmphasis(string requestId, string action)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        EnqueueOrRun(() => Post("unsafeEmphasisResponse", new { action }, requestId));
    }

    public async Task<bool> ExecuteCommandAsync(
        string command,
        string? text = null,
        double? clientX = null,
        double? clientY = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commandRequests.Add(requestId, completion);
        EnqueueOrRun(() =>
        {
            var registeredId = _session.RegisterRequest("commandResult", requestId);
            Post("command", new { command, text, clientX, clientY }, registeredId);
        });

        using var timeoutCancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCancellation.Token,
            cancellationToken);
        using var registration = linkedCancellation.Token.Register(
            () => completion.TrySetCanceled(linkedCancellation.Token));
        try
        {
            return await completion.Task;
        }
        finally
        {
            _commandRequests.Remove(requestId);
        }
    }

    public async Task<EditorSelectionExport> RequestSelectionExportAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<EditorSelectionExport>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _selectionExportRequests.Add(requestId, completion);
        EnqueueOrRun(() =>
        {
            var registeredId = _session.RegisterRequest("selectionExport", requestId);
            Post("command", new { command = "exportSelection" }, registeredId);
        });

        using var timeoutCancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCancellation.Token,
            cancellationToken);
        using var registration = linkedCancellation.Token.Register(
            () => completion.TrySetCanceled(linkedCancellation.Token));
        try
        {
            return await completion.Task;
        }
        finally
        {
            _selectionExportRequests.Remove(requestId);
        }
    }

    public async Task<string> RequestExportAsync(
        string format,
        string style,
        string header,
        string footer,
        int fontSize,
        float lineHeight,
        int maxWidth,
        bool visualCjkAutoSpacing,
        string? colorSchemeCss = null,
        string? title = null,
        bool keepTablesTogether = false,
        bool keepHeadingsWithNextBlock = false,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _exportRequests.Add(requestId, completion);
        var options = System.Text.Json.JsonSerializer.Serialize(new
        {
            format,
            style,
            header,
            footer,
            fontSize,
            lineHeight,
            maxWidth,
            visualCjkAutoSpacing,
            colorSchemeCss,
            title,
            keepTablesTogether,
            keepHeadingsWithNextBlock,
        });
        EnqueueOrRun(() =>
        {
            var registeredId = _session.RegisterRequest("exportContent", requestId);
            Post("command", new { command = "exportDocument", text = options }, registeredId);
        });

        using var timeoutCancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCancellation.Token,
            cancellationToken);
        using var registration = linkedCancellation.Token.Register(
            () => completion.TrySetCanceled(linkedCancellation.Token));
        try
        {
            return await completion.Task;
        }
        finally
        {
            _exportRequests.Remove(requestId);
        }
    }

    public async Task<byte[]> PrintExportToPdfAsync(
        string html,
        string paperSize,
        bool landscape,
        float marginTop,
        float marginBottom,
        float marginLeft,
        float marginRight,
        string headerText = "",
        string headerAlignment = "",
        string footerText = "",
        string footerAlignment = "",
        string headerFooterFontFamily = "",
        CancellationToken cancellationToken = default)
    {
        // Use CSS @page margins instead of print-setting margins so the html/body
        // background color fills the full page. @page margins apply per-page,
        // unlike body padding which only affects the first and last page.
        var pageRule = BuildPdfPageRule(
            marginTop,
            marginRight,
            marginBottom,
            marginLeft,
            headerText,
            headerAlignment,
            footerText,
            footerAlignment,
            headerFooterFontFamily);
        html = html.Replace("</style>", $"{pageRule}\n</style>");

        var tempPath = Path.Combine(Path.GetTempPath(), $"markleaf-pdf-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempPath, html, System.Text.Encoding.UTF8, cancellationToken);

        try
        {
            // 打印 WebView2 的视口必须与纸张内容区一致：否则前端脚本用
            // clientWidth 量出的“可用宽度”是 1px 视口，导致段间公式被过度缩小。
            var (widthIn, heightIn) = PaperSizeToInches(paperSize, landscape);
            var contentWidthPx = Math.Max(1, (int)Math.Round(widthIn * 96.0 - (marginLeft + marginRight) / 25.4 * 96.0));
            var contentHeightPx = Math.Max(1, (int)Math.Round(heightIn * 96.0 - (marginTop + marginBottom) / 25.4 * 96.0));

            using var printForm = new Form
            {
                ClientSize = new Size(contentWidthPx, contentHeightPx),
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            var printView = new WebView2 { Dock = DockStyle.Fill };
            printForm.Controls.Add(printView);
            printForm.Show();

            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _webView2UserDataDirectory);
            await printView.EnsureCoreWebView2Async(environment);
            var core = printView.CoreWebView2
                ?? throw new InvalidOperationException("PDF print WebView2 failed to initialize.");

            var loadComplete = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
            handler = (_, args) =>
            {
                core.NavigationCompleted -= handler;
                if (args.IsSuccess)
                    loadComplete.TrySetResult(true);
                else
                    loadComplete.TrySetException(
                        new InvalidOperationException($"PDF export page failed to load: {args.WebErrorStatus}"));
            };
            core.NavigationCompleted += handler;
            core.Navigate(new Uri(tempPath).AbsoluteUri);
            await loadComplete.Task.WaitAsync(cancellationToken);

            // 等待 KaTeX 字体加载完成后按可用宽度缩放段间公式（幂等，可重复执行），
            // 避免字体未就绪时以回退字体度量导致公式被过度缩小。
            try
            {
                await core.ExecuteScriptAsync(
                    "document.fonts.ready.then(function () { window.__markleafFitMath && window.__markleafFitMath(); })");
            }
            catch
            {
                // 非致命：公式缩放脚本执行失败时仍继续打印，不阻断导出。
            }

            var settings = core.Environment.CreatePrintSettings();
            settings.PageWidth = widthIn;
            settings.PageHeight = heightIn;
            settings.MarginTop = 0;
            settings.MarginBottom = 0;
            settings.MarginLeft = 0;
            settings.MarginRight = 0;
            settings.ShouldPrintBackgrounds = true;

            var pdfTempPath = Path.Combine(Path.GetTempPath(), $"markleaf-pdf-{Guid.NewGuid():N}.pdf");
            await core.PrintToPdfAsync(pdfTempPath, settings);
            var pdfBytes = await File.ReadAllBytesAsync(pdfTempPath, cancellationToken);
            try { File.Delete(pdfTempPath); } catch { }
            return pdfBytes;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    public async Task<IReadOnlyList<string>> CaptureExportImagesAsync(
        string html,
        string outputPath,
        int contentWidth,
        int maximumImageHeight,
        float scale,
        string format,
        int jpegQuality,
        CancellationToken cancellationToken = default)
    {
        contentWidth = Math.Clamp(contentWidth, 320, 4000);
        maximumImageHeight = Math.Clamp(maximumImageHeight, 1000, 30000);
        scale = float.IsFinite(scale) ? Math.Clamp(scale, 1f, 4f) : 2f;
        format = string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpeg" : "png";
        jpegQuality = Math.Clamp(jpegQuality, 1, 100);

        var tempPath = Path.Combine(Path.GetTempPath(), $"markleaf-image-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempPath, html, Encoding.UTF8, cancellationToken);

        try
        {
            using var captureForm = new Form
            {
                ClientSize = new Size(contentWidth + 112, 800),
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            var captureView = new WebView2 { Dock = DockStyle.Fill };
            captureForm.Controls.Add(captureView);
            captureForm.Show();

            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _webView2UserDataDirectory);
            await captureView.EnsureCoreWebView2Async(environment);
            var core = captureView.CoreWebView2
                ?? throw new InvalidOperationException("Image export WebView2 failed to initialize.");

            var loadComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
            handler = (_, args) =>
            {
                core.NavigationCompleted -= handler;
                if (args.IsSuccess)
                    loadComplete.TrySetResult(true);
                else
                    loadComplete.TrySetException(
                        new InvalidOperationException($"Image export page failed to load: {args.WebErrorStatus}"));
            };
            core.NavigationCompleted += handler;
            core.Navigate(new Uri(tempPath).AbsoluteUri);
            await loadComplete.Task.WaitAsync(cancellationToken);

            await core.ExecuteScriptAsync(
                "Promise.all([document.fonts.ready, Promise.all(Array.from(document.images).map(function (img) { return img.complete ? Promise.resolve() : new Promise(function (resolve) { img.addEventListener('load', resolve, { once: true }); img.addEventListener('error', resolve, { once: true }); }); }))]).then(function () { window.__markleafFitMath && window.__markleafFitMath(); })");
            await Task.Delay(100, cancellationToken);

            var metricsJson = await core.ExecuteScriptAsync(
                "(function () { var root = document.getElementById('export-root'); var rect = root ? root.getBoundingClientRect() : document.documentElement.getBoundingClientRect(); return JSON.stringify({ width: Math.ceil(rect.width), height: Math.ceil(Math.max(root ? root.scrollHeight : 0, document.documentElement.scrollHeight, document.body.scrollHeight)) }); })()");
            var metricsText = JsonSerializer.Deserialize<string>(metricsJson) ?? "{}";
            using var metrics = JsonDocument.Parse(metricsText);
            var pageWidth = Math.Max(1, metrics.RootElement.GetProperty("width").GetInt32());
            var pageHeight = Math.Max(1, metrics.RootElement.GetProperty("height").GetInt32());
            var chunkCssHeight = Math.Max(1, (int)Math.Floor(maximumImageHeight / scale));
            var chunkCount = (int)Math.Ceiling(pageHeight / (double)chunkCssHeight);
            var directory = Path.GetDirectoryName(outputPath) ?? "";
            var baseName = Path.GetFileNameWithoutExtension(outputPath);
            var extension = format == "jpeg" ? ".jpg" : ".png";
            var paths = new List<string>(chunkCount);

            for (var index = 0; index < chunkCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var y = index * chunkCssHeight;
                var height = Math.Min(chunkCssHeight, pageHeight - y);
                var parameters = JsonSerializer.Serialize(new
                {
                    format,
                    quality = format == "jpeg" ? jpegQuality : (int?)null,
                    fromSurface = true,
                    captureBeyondViewport = true,
                    clip = new { x = 0, y, width = pageWidth, height, scale },
                }, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                });
                var response = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", parameters);
                using var responseJson = JsonDocument.Parse(response);
                var bytes = Convert.FromBase64String(responseJson.RootElement.GetProperty("data").GetString() ?? "");
                var path = chunkCount == 1
                    ? Path.ChangeExtension(outputPath, extension)
                    : Path.Combine(directory, $"{baseName}-{index + 1:D2}{extension}");
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                paths.Add(path);
            }

            return paths;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static string BuildPdfPageRule(
        float marginTop,
        float marginRight,
        float marginBottom,
        float marginLeft,
        string headerText,
        string headerAlignment,
        string footerText,
        string footerAlignment,
        string headerFooterFontFamily = "")
    {
        var builder = new StringBuilder();
        builder.Append(CultureInvariant(
            $"@page {{ margin: {marginTop}mm {marginRight}mm {marginBottom}mm {marginLeft}mm; background-color: var(--bg-primary);"));
        AppendMarginBox(builder, "top", headerAlignment, headerText, headerFooterFontFamily);
        AppendMarginBox(builder, "bottom", footerAlignment, footerText, headerFooterFontFamily);
        builder.Append(" } html { background: var(--bg-primary); }");
        return builder.ToString();
    }

    private static void AppendMarginBox(
        StringBuilder builder,
        string vertical,
        string alignment,
        string text,
        string headerFooterFontFamily)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var horizontal = NormalizePdfHeaderFooterAlignment(alignment);
        var content = ToCssGeneratedContent(text);
        var fontFamily = string.IsNullOrWhiteSpace(headerFooterFontFamily)
            ? "serif, \"Source Han Serif CN\", \"Noto Serif CJK CN\""
            : headerFooterFontFamily;
        var offset = vertical == "top" ? "padding-top: 6mm;" : "padding-bottom: 6mm;";
        builder.Append(CultureInvariant(
            $" @{vertical}-{horizontal} {{ content: {content}; font-family: {fontFamily}; font-size: calc(var(--ml-font-size) * 0.875); color: var(--text-primary); {offset} }}"));
    }

    private static string NormalizePdfHeaderFooterAlignment(string alignment) =>
        alignment switch
        {
            "left" => "left",
            "right" => "right",
            _ => "center",
        };

    private static string ToCssGeneratedContent(string text)
    {
        var parts = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var pageIndex = text.IndexOf("{page}", index, StringComparison.Ordinal);
            var totalIndex = text.IndexOf("{total}", index, StringComparison.Ordinal);
            var next = MinPositive(pageIndex, totalIndex);
            if (next < 0)
            {
                AppendCssString(parts, text[index..]);
                break;
            }

            AppendCssString(parts, text[index..next]);
            if (next == pageIndex)
            {
                parts.Add("counter(page)");
                index = pageIndex + "{page}".Length;
            }
            else
            {
                parts.Add("counter(pages)");
                index = totalIndex + "{total}".Length;
            }
        }

        return parts.Count == 0 ? "\"\"" : string.Join(" ", parts);
    }

    private static int MinPositive(int first, int second) =>
        first < 0 ? second : second < 0 ? first : Math.Min(first, second);

    private static void AppendCssString(List<string> parts, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        parts.Add($"\"{EscapeCssString(value)}\"");
    }

    private static string EscapeCssString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r\n", "\\A ", StringComparison.Ordinal)
            .Replace("\n", "\\A ", StringComparison.Ordinal)
            .Replace("\r", "\\A ", StringComparison.Ordinal);
    }

    private static string CultureInvariant(FormattableString value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 直接打印编辑器当前内容，弹出系统打印对话框。
    /// </summary>
    public void PrintDocument()
    {
        _webView.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.System);
    }

    private static (double Width, double Height) PaperSizeToInches(string name, bool landscape)
    {
        var (wMm, hMm) = name switch
        {
            "A3" => (297.0, 420.0),
            "A5" => (148.0, 210.0),
            "Letter" => (215.9, 279.4),
            "Legal" => (215.9, 355.6),
            "B4" => (250.0, 353.0),
            "B5" => (176.0, 250.0),
            _ => (210.0, 297.0),
        };
        var widthIn = (landscape ? hMm : wMm) / 25.4;
        var heightIn = (landscape ? wMm : hMm) / 25.4;
        return (widthIn, heightIn);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initializationCancellation?.Cancel();
        _readyTimeoutCancellation?.Cancel();
        foreach (var request in _snapshotRequests.Values)
        {
            request.TrySetCanceled();
        }
        _snapshotRequests.Clear();
        foreach (var request in _commandRequests.Values)
        {
            request.TrySetCanceled();
        }
        _commandRequests.Clear();
        foreach (var request in _selectionExportRequests.Values)
        {
            request.TrySetCanceled();
        }
        _selectionExportRequests.Clear();
        foreach (var request in _exportRequests.Values)
        {
            request.TrySetCanceled();
        }
        _exportRequests.Clear();
        DetachCoreEvents();
    }

    private void ConfigureCoreWebView2()
    {
        var core = _webView.CoreWebView2;
        if (core is null)
        {
            throw new InvalidOperationException("CoreWebView2 was not created.");
        }

        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        // 关闭 WebView2 内置的 Ctrl+滚轮/快捷键缩放，统一由宿主通过 ZoomFactor 控制，
        // 保证状态栏与“设置缩放”菜单的数值始终一致。
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        _webView.AllowExternalDrop = true;
        core.SetVirtualHostNameToFolderMapping(
            "editor.local",
            _editorWebPath,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.AddWebResourceRequestedFilter(
            "https://assets.local/*",
            CoreWebView2WebResourceContext.Image,
            CoreWebView2WebResourceRequestSourceKinds.Document);
        _webView.ZoomFactor = 1;
        _logger.Info(
            $"WebView2 runtime {core.Environment.BrowserVersionString}; editor protocol {EditorProtocol.Version}.");

        if (_eventsAttached)
        {
            return;
        }

        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        core.WebResourceRequested += OnAssetResourceRequested;
        core.ProcessFailed += OnProcessFailed;
        _eventsAttached = true;
    }

    private static Uri CreateVersionedEditorUri(string editorWebPath)
    {
        var indexPath = Path.Combine(editorWebPath, "index.html");
        var version = File.Exists(indexPath)
            ? File.GetLastWriteTimeUtc(indexPath).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Uri($"https://editor.local/index.html?v={version}");
    }

    private void DetachCoreEvents()
    {
        var core = _webView.CoreWebView2;
        if (!_eventsAttached || core is null)
        {
            return;
        }

        core.NavigationStarting -= OnNavigationStarting;
        core.NavigationCompleted -= OnNavigationCompleted;
        core.NewWindowRequested -= OnNewWindowRequested;
        core.WebMessageReceived -= OnWebMessageReceived;
        core.WebResourceRequested -= OnAssetResourceRequested;
        core.ProcessFailed -= OnProcessFailed;
        _eventsAttached = false;
    }

    private void OnAssetResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        var core = _webView.CoreWebView2;
        if (core is null || !Uri.TryCreate(eventArgs.Request.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        var encodedPath = uri.Query.StartsWith("?path=", StringComparison.Ordinal)
            ? uri.Query[6..]
            : string.Empty;
        string path;
        try
        {
            path = Path.GetFullPath(Uri.UnescapeDataString(encodedPath).Replace('/', Path.DirectorySeparatorChar));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            eventArgs.Response = core.Environment.CreateWebResourceResponse(
                Stream.Null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        if (!ImageAssetService.IsSupportedImagePath(path) || !File.Exists(path))
        {
            eventArgs.Response = core.Environment.CreateWebResourceResponse(
                Stream.Null, 404, "Not Found", "Content-Type: text/plain");
            return;
        }

        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            eventArgs.Response = core.Environment.CreateWebResourceResponse(
                stream,
                200,
                "OK",
                $"Content-Type: {GetImageContentType(path)}\r\nCache-Control: no-store");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not serve editor image resource: {Path.GetFileName(path)}; {exception.Message}");
            eventArgs.Response = core.Environment.CreateWebResourceResponse(
                Stream.Null, 404, "Not Found", "Content-Type: text/plain");
        }
    }

    private static string GetImageContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream",
        };
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!IsAllowedEditorUri(eventArgs.Uri))
        {
            eventArgs.Cancel = true;
            _logger.Warning("Blocked WebView2 navigation outside editor.local.");
            return;
        }

        if (State == EditorLifecycleState.Ready)
        {
            _session.TransitionTo(EditorLifecycleState.LoadingPage);
            _webView.Visible = false;
        }

        if (State == EditorLifecycleState.LoadingPage)
        {
            _session.TransitionTo(EditorLifecycleState.WaitingForEditorReady);
            ShowLoading(Loc.Get("editor.handshakeMessage"), Loc.Get("editor.handshakeDetail"));
            NotifyStateChanged();
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            Fail(Loc.Format("editor.pageLoadFailed", eventArgs.WebErrorStatus), null);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        _logger.Warning("Blocked WebView2 new-window request.");
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        Fail(Loc.Format("editor.processCrashed", eventArgs.ProcessFailedKind), null);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        if (!string.Equals(eventArgs.Source, "https://editor.local/", StringComparison.OrdinalIgnoreCase)
            && !eventArgs.Source.StartsWith("https://editor.local/", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("Rejected WebView2 message from an unexpected origin.");
            return;
        }

        if (!EditorProtocol.TryDeserializeEditorMessage(
                eventArgs.WebMessageAsJson,
                out var message,
                out var error)
            || message is null)
        {
            _logger.Warning($"Rejected editor protocol message: {error}");
            return;
        }

        if (!_session.Accept(message))
        {
            _logger.Warning($"Rejected stale or foreign editor message: {message.Type}.");
            return;
        }

        switch (message.Type)
        {
            case "ready":
                HandleEditorReady();
                break;
            case "documentLoaded":
                _documentLoaded = true;
                _logger.Info($"Editor document loaded at revision {message.Revision}.");
                NotifyStateChanged();
                DocumentLoaded?.Invoke(this, message);
                break;
            case "dirtyChanged":
                DirtyChanged?.Invoke(this, message);
                break;
            case "commandStateChanged":
                CommandStateChanged?.Invoke(this, EditorCommandStatus.FromPayload(message.Payload));
                break;
            case "editorStatusChanged":
                EditorStatusChanged?.Invoke(this, EditorStatus.FromPayload(message.Payload));
                break;
            case "contextMenuRequested":
                ContextMenuRequested?.Invoke(
                    this,
                    new EditorContextMenuRequest(
                        message.Payload.GetProperty("clientX").GetDouble(),
                        message.Payload.GetProperty("clientY").GetDouble(),
                        message.Payload.TryGetProperty("menuHeight", out var menuHeight)
                            && menuHeight.ValueKind == System.Text.Json.JsonValueKind.Number
                            ? menuHeight.GetDouble()
                            : 0,
                        message.Payload.TryGetProperty("canStartFormatPainter", out var canStart)
                            && canStart.ValueKind == System.Text.Json.JsonValueKind.True,
                        message.Payload.TryGetProperty("formatPainterArmed", out var armed)
                            && armed.ValueKind == System.Text.Json.JsonValueKind.True,
                        message.Payload.TryGetProperty("readOnly", out var readOnly)
                            && readOnly.ValueKind == System.Text.Json.JsonValueKind.True,
                        message.Payload.TryGetProperty("sourceMode", out var sourceMode)
                            && sourceMode.ValueKind == System.Text.Json.JsonValueKind.True,
                        message.Payload.TryGetProperty("expandedSource", out var expandedSource)
                            && expandedSource.ValueKind == System.Text.Json.JsonValueKind.True));
                break;
            case "blockMenuRequested":
                BlockMenuRequested?.Invoke(
                    this,
                    new EditorBlockMenuRequest(
                        message.Payload.GetProperty("clientX").GetDouble(),
                        message.Payload.GetProperty("clientY").GetDouble(),
                        message.Payload.GetProperty("position").GetInt32()));
                break;
            case "mermaidEditRequested":
                MermaidEditRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "findResult":
                var replaced = message.Payload.TryGetProperty("replaced", out var replacedElement)
                    && replacedElement.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? replacedElement.GetInt32()
                    : (int?)null;
                FindResultReceived?.Invoke(
                    this,
                    new EditorFindResult(
                        message.Payload.GetProperty("current").GetInt32(),
                        message.Payload.GetProperty("total").GetInt32(),
                        replaced));
                break;
            case "outlineChanged":
                OutlineChanged?.Invoke(this, EditorOutline.FromPayload(message.Payload));
                break;
            case "outlineSelectionChanged":
                OutlineSelectionChanged?.Invoke(
                    this,
                    message.Payload.GetProperty("position").ValueKind == System.Text.Json.JsonValueKind.Null
                        ? null
                        : message.Payload.GetProperty("position").GetInt32());
                break;
            case "openLink":
                var url = message.Payload.GetProperty("url").GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    OpenLinkRequested?.Invoke(this, url);
                }
                break;
            case "dropFiles":
                var paths = eventArgs.AdditionalObjects
                    .OfType<CoreWebView2File>()
                    .Select(file => file.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Take(32)
                    .ToArray();
                if (paths.Length != message.Payload.GetProperty("count").GetInt32())
                {
                    _logger.Warning("Dropped-file message did not contain the expected WebView2 file objects.");
                    break;
                }
                FilesDropped?.Invoke(
                    this,
                    new DroppedFiles(
                        paths,
                        message.Payload.GetProperty("clientX").GetDouble(),
                        message.Payload.GetProperty("clientY").GetDouble()));
                break;
            case "pasteImage":
                PasteImageRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "zoomWheel":
                ZoomWheelRequested?.Invoke(this, message.Payload.GetProperty("deltaY").GetDouble());
                break;
            case "unsafeEmphasisRequested":
                UnsafeEmphasisRequested?.Invoke(
                    this,
                    new UnsafeEmphasisRequest(
                        message.RequestId ?? string.Empty,
                        message.Payload.GetProperty("kind").GetString() ?? "bold"));
                break;
            case "footnoteDefinitionMissing":
                FootnoteDefinitionMissing?.Invoke(this, EventArgs.Empty);
                break;
            case "footnoteReferenceMissing":
                FootnoteReferenceMissing?.Invoke(this, EventArgs.Empty);
                break;
            case "snapshot":
                if (_session.CompleteRequest(message.RequestId, "snapshot"))
                {
                    if (message.RequestId is not null
                        && _snapshotRequests.TryGetValue(message.RequestId, out var completion)
                        && message.Payload.TryGetProperty("markdown", out var markdownElement))
                    {
                        completion.TrySetResult(
                            new EditorSnapshot(markdownElement.GetString() ?? string.Empty, message.Revision));
                    }
                    SnapshotReceived?.Invoke(this, message);
                }
                else
                {
                    _logger.Warning("Rejected unmatched editor snapshot response.");
                }
                break;
            case "commandResult":
                if (_session.CompleteRequest(message.RequestId, "commandResult")
                    && message.RequestId is not null
                    && _commandRequests.TryGetValue(message.RequestId, out var commandCompletion))
                {
                    commandCompletion.TrySetResult(message.Payload.GetProperty("success").GetBoolean());
                }
                else
                {
                    _logger.Warning("Rejected unmatched editor command response.");
                }
                break;
            case "selectionExport":
                if (_session.CompleteRequest(message.RequestId, "selectionExport")
                    && message.RequestId is not null
                    && _selectionExportRequests.TryGetValue(message.RequestId, out var exportCompletion))
                {
                    exportCompletion.TrySetResult(new EditorSelectionExport(
                        message.Payload.GetProperty("text").GetString() ?? string.Empty,
                        message.Payload.GetProperty("markdown").GetString() ?? string.Empty,
                        message.Payload.GetProperty("html").GetString() ?? string.Empty));
                }
                else
                {
                    _logger.Warning("Rejected unmatched editor selection export response.");
                }
                break;
            case "exportContent":
                if (_session.CompleteRequest(message.RequestId, "exportContent")
                    && message.RequestId is not null
                    && _exportRequests.TryGetValue(message.RequestId, out var exportContentCompletion))
                {
                    exportContentCompletion.TrySetResult(
                        message.Payload.GetProperty("html").GetString() ?? string.Empty);
                }
                else
                {
                    _logger.Warning("Rejected unmatched editor export content response.");
                }
                break;
            case "error":
                var frontendMessage = message.Payload.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                _logger.Warning($"Editor frontend error: {frontendMessage ?? "unspecified"}.");
                break;
        }
    }

    private void HandleEditorReady()
    {
        if (State != EditorLifecycleState.WaitingForEditorReady)
        {
            return;
        }

        _session.TransitionTo(EditorLifecycleState.Ready);
        _readyTimeoutCancellation?.Cancel();
        _initializationTimer.Stop();
        _webView.Visible = true;
        _logger.Info($"Editor ready after {_initializationTimer.ElapsedMilliseconds} ms.");
        NotifyStateChanged();
        Ready?.Invoke(this, EventArgs.Empty);

        while (_readyActions.TryDequeue(out var action))
        {
            action();
        }

        if (_replayDocumentAfterNavigation && _lastDocumentId is { } documentId)
        {
            _replayDocumentAfterNavigation = false;
            _session.StartDocument(documentId, _lastDocumentRevision);
            Post("loadDocument", new
            {
                markdown = _lastDocumentMarkdown,
                readOnly = _lastDocumentReadOnly,
                documentType = _lastDocumentType,
            });
        }
    }

    private void EnqueueOrRun(Action action)
    {
        if (IsReady)
        {
            action();
            return;
        }

        _readyActions.Enqueue(action);
    }

    private void Post(string type, object? payload = null, string? requestId = null)
    {
        if (_webView.CoreWebView2 is null)
        {
            return;
        }

        _webView.CoreWebView2.PostWebMessageAsJson(
            EditorProtocol.SerializeHostMessage(
                type,
                _session.DocumentId,
                _session.ConfirmedRevision,
                payload,
                requestId));
    }

    private void Fail(string message, Exception? exception)
    {
        if (_failureShown)
        {
            return;
        }

        _failureShown = true;
        _readyTimeoutCancellation?.Cancel();
        if (State != EditorLifecycleState.Failed)
        {
            _session.TransitionTo(EditorLifecycleState.Failed);
        }

        _initializationTimer.Stop();
        _webView.Visible = false;
        _loadingView.Visible = true;
        _loadingView.ShowFailure(message);
        _logger.Error(message, exception);
        NotifyStateChanged();
    }

    private void ShowLoading(string title, string detail)
    {
        _loadingView.ShowLoading(title, detail);
    }

    private void NotifyStateChanged() => _stateChanged();

    private void BeginReadyTimeout()
    {
        _readyTimeoutCancellation?.Cancel();
        _readyTimeoutCancellation = new CancellationTokenSource();
        _ = WaitForReadyTimeoutAsync(_readyTimeoutCancellation.Token);
    }

    private async Task WaitForReadyTimeoutAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            if (State is EditorLifecycleState.LoadingPage or EditorLifecycleState.WaitingForEditorReady)
            {
                Fail(Loc.Get("editor.readyTimeout"), null);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsAllowedEditorUri(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && string.Equals(parsed.Host, "editor.local", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record DroppedFiles(IReadOnlyList<string> Paths, double ClientX, double ClientY);

internal sealed record EditorFindResult(int Current, int Total, int? Replaced);
