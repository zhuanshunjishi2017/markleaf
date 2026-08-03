using System.Diagnostics;
using MarkLeaf.Documents;
using MarkLeaf.Services.Logging;
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

    public event EventHandler? Ready;

    public event EventHandler<EditorMessage>? DocumentLoaded;

    public event EventHandler<EditorMessage>? SnapshotReceived;

    public event EventHandler<EditorMessage>? DirtyChanged;

    public event EventHandler<EditorCommandStatus>? CommandStateChanged;

    public event EventHandler<EditorStatus>? EditorStatusChanged;

    public event EventHandler<EditorContextMenuRequest>? ContextMenuRequested;

    public event EventHandler<EditorOutline>? OutlineChanged;

    public event EventHandler<int?>? OutlineSelectionChanged;

    public event EventHandler<string>? OpenLinkRequested;

    public event EventHandler<DroppedFiles>? FilesDropped;

    public event EventHandler? PasteImageRequested;

    public EditorHostController(
        WebView2 webView,
        EditorLoadingView loadingView,
        EditorSession session,
        IAppLogger logger,
        string editorWebPath,
        Action stateChanged)
    {
        _webView = webView;
        _loadingView = loadingView;
        _session = session;
        _logger = logger;
        _editorWebPath = editorWebPath;
        _editorUri = CreateVersionedEditorUri(editorWebPath);
        _stateChanged = stateChanged;
        _loadingView.RetryRequested += (_, _) => _ = RetryAsync();
    }

    public EditorLifecycleState State => _session.State;

    public bool IsReady => State == EditorLifecycleState.Ready;

    public bool IsDocumentLoaded => IsReady && _documentLoaded;

    public Point EditorPointToScreen(EditorContextMenuRequest request)
    {
        var devicePoint = EditorCoordinateConverter.CssToDevicePoint(
            request.ClientX,
            request.ClientY,
            _webView.DeviceDpi);
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
            Fail("编辑器静态资源缺失。请重新构建 EditorWeb。", null);
            return;
        }

        _initializationTimer.Restart();
        _session.TransitionTo(EditorLifecycleState.Initializing);
        ShowLoading("正在初始化 WebView2…", "正在检查运行时并创建编辑器环境。");
        NotifyStateChanged();

        try
        {
            _initializationCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _webView.EnsureCoreWebView2Async().WaitAsync(_initializationCancellation.Token);
            ConfigureCoreWebView2();

            _session.TransitionTo(EditorLifecycleState.LoadingPage);
            ShowLoading("正在加载编辑器…", "正在载入本地编辑器资源。");
            NotifyStateChanged();
            _webView.Source = _editorUri;
            BeginReadyTimeout();
        }
        catch (OperationCanceledException exception)
        {
            Fail("WebView2 初始化超时。", exception);
        }
        catch (Exception exception)
        {
            Fail("WebView2 初始化失败。请确认 Evergreen Runtime 已安装。", exception);
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
        _loadingView.ShowLoading("正在重试编辑器…", "正在重新创建 WebView2 环境。");
        NotifyStateChanged();
        await InitializeAsync();
    }

    public void LoadDocument(string markdown)
    {
        LoadDocument(Guid.NewGuid(), 0, markdown);
    }

    public void LoadDocument(Guid documentId, long revision, string markdown)
    {
        EnqueueOrRun(() =>
        {
            _session.StartDocument(documentId, revision);
            _documentLoaded = false;
            Post("loadDocument", new { markdown });
        });
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

    public void ExecuteCommand(
        string command,
        string? text = null,
        bool applyToCurrentTextBlockWhenEmpty = false)
    {
        EnqueueOrRun(() => Post("command", new { command, text, applyToCurrentTextBlockWhenEmpty }));
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
        CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"markleaf-pdf-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempPath, html, System.Text.Encoding.UTF8, cancellationToken);

        try
        {
            using var printForm = new Form
            {
                Width = 1,
                Height = 1,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            var printView = new WebView2 { Dock = DockStyle.Fill };
            printForm.Controls.Add(printView);
            printForm.Show();

            var environment = await CoreWebView2Environment.CreateAsync();
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

            var settings = core.Environment.CreatePrintSettings();
            var (widthIn, heightIn) = PaperSizeToInches(paperSize, landscape);
            settings.PageWidth = widthIn;
            settings.PageHeight = heightIn;
            settings.MarginTop = marginTop / 25.4f;
            settings.MarginBottom = marginBottom / 25.4f;
            settings.MarginLeft = marginLeft / 25.4f;
            settings.MarginRight = marginRight / 25.4f;
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
            ShowLoading("正在准备编辑器…", "编辑器页面已加载，正在等待通信握手。");
            NotifyStateChanged();
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess)
        {
            Fail($"编辑器页面加载失败：{eventArgs.WebErrorStatus}", null);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        _logger.Warning("Blocked WebView2 new-window request.");
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        Fail($"WebView2 进程异常终止：{eventArgs.ProcessFailedKind}", null);
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
                        message.Payload.GetProperty("clientY").GetDouble()));
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
        _loadingView.Visible = false;
        _webView.Visible = true;
        _webView.BringToFront();
        _logger.Info($"Editor ready after {_initializationTimer.ElapsedMilliseconds} ms.");
        NotifyStateChanged();
        Ready?.Invoke(this, EventArgs.Empty);

        while (_readyActions.TryDequeue(out var action))
        {
            action();
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
        _loadingView.ShowFailure(message);
        _logger.Error(message, exception);
        NotifyStateChanged();
    }

    private void ShowLoading(string title, string detail)
    {
        _loadingView.ShowLoading(title, detail);
        _loadingView.Visible = true;
        _loadingView.BringToFront();
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
                Fail("编辑器页面未在规定时间内就绪。", null);
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
