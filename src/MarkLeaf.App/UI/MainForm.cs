using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.ExternalLinks;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.UI;

internal sealed partial class MainForm : Form
{
    private readonly LaunchOptions _options;
    private readonly ApplicationPaths _paths;
    private readonly AppSettings _settings;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly CommandRouter _commandRouter;
    private readonly NativeMenuService _menuService;
    private readonly EditorSession _editorSession = new();
    private readonly DocumentFileService _documentFileService = new();
    private readonly ImageAssetService _imageAssetService;
    private EditorHostController? _editorHost;
    private MarkdownDocument? _document;
    private FileSystemWatcher? _documentWatcher;
    private readonly System.Windows.Forms.Timer _externalChangeTimer = new() { Interval = 600 };
    private bool _documentOperationInProgress;
    private bool _closeApproved;
    private int _effectiveDpi;
    private readonly SplitContainer _workspaceSplit;
    private readonly SplitContainer _outlineSplit;
    private readonly ToolStripStatusLabel _statusLabel = new("正在准备编辑器");
    private bool _settingsSaved;
    private bool _focusMode;
    private bool _workspaceVisibleBeforeFocus = true;
    private bool _outlineVisibleBeforeFocus = true;
    private bool _editorSmokeStarted;
    private bool _editorCommandSmokeStarted;
    private bool _documentSmokeStarted;
    private EditorCommandStatus _editorCommandStatus = EditorCommandStatus.Empty;

    public MainForm(
        LaunchOptions options,
        ApplicationPaths paths,
        AppSettings settings,
        ISettingsService settingsService,
        IAppLogger logger)
    {
        _options = options;
        _paths = paths;
        _settings = settings;
        _settingsService = settingsService;
        _logger = logger;
        _imageAssetService = new ImageAssetService(paths.DraftAssetsDirectory);
        _effectiveDpi = options.LayoutDpiOverride ?? DeviceDpi;
        _commandRouter = new CommandRouter(GetCommandState, ExecuteCommand);
        _menuService = new NativeMenuService(_commandRouter);

        Text = "MarkLeaf";
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 600);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular);

        var placement = WindowPlacementCalculator.Normalize(
            settings.MainWindow,
            _effectiveDpi,
            Screen.AllScreens
                .OrderByDescending(screen => screen.Primary)
                .Select(screen => new ScreenArea(
                    screen.WorkingArea.Left,
                    screen.WorkingArea.Top,
                    screen.WorkingArea.Width,
                    screen.WorkingArea.Height))
                .ToArray());

        Bounds = new Rectangle(placement.Left, placement.Top, placement.Width, placement.Height);

        _outlineSplit = CreateEditorAndOutlineSplit(placement.OutlineWidth);
        _workspaceSplit = CreateWorkspaceSplit(placement.WorkspaceWidth, _outlineSplit);

        Controls.Add(_workspaceSplit);
        Controls.Add(CreateStatusBar());

        Shown += async (_, _) => await OnMainFormShownAsync(placement.IsMaximized);
        FormClosing += OnMainFormClosing;
        DpiChanged += (_, args) =>
        {
            if (_options.LayoutDpiOverride is null)
            {
                _effectiveDpi = args.DeviceDpiNew;
            }

            _logger.Info($"Main window DPI changed: {args.DeviceDpiOld} -> {args.DeviceDpiNew}.");
        };
    }

    private SplitContainer CreateWorkspaceSplit(int workspaceWidth, Control content)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1280, 740),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 1,
            Panel1MinSize = 160,
            Panel2MinSize = 500,
            IsSplitterFixed = false,
        };
        split.Panel1.Controls.Add(CreateSidePanel("工作区", CreateWorkspaceTree()));
        split.Panel2.Controls.Add(content);
        split.HandleCreated += (_, _) => SetSplitterDistanceSafely(split, workspaceWidth, FixedPanel.Panel1);
        return split;
    }

    private SplitContainer CreateEditorAndOutlineSplit(int outlineWidth)
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1060, 740),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 1,
            Panel1MinSize = 500,
            Panel2MinSize = 160,
            IsSplitterFixed = false,
        };
        split.Panel1.Controls.Add(CreateEditorHost());
        split.Panel2.Controls.Add(CreateSidePanel("大纲", CreateOutlineTree()));
        split.HandleCreated += (_, _) => SetSplitterDistanceSafely(split, outlineWidth, FixedPanel.Panel2);
        return split;
    }

    private Control CreateEditorHost()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
        };
        var webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
            TabStop = true,
            AllowExternalDrop = true,
        };
        var loadingView = new EditorLoadingView();
        panel.Controls.Add(webView);
        panel.Controls.Add(loadingView);
        _editorHost = new EditorHostController(
            webView,
            loadingView,
            _editorSession,
            _logger,
            _options.EditorWebRoot ?? Path.Combine(AppContext.BaseDirectory, "EditorWeb"),
            OnEditorStateChanged);
        _editorHost.Ready += (_, _) => BeginEditorSmokeIfRequested();
        _editorHost.Ready += (_, _) => LoadInitialDocumentIfNeeded();
        _editorHost.Ready += async (_, _) => await BeginDocumentSmokeIfRequestedAsync();
        _editorHost.DocumentLoaded += (_, _) => ContinueEditorSmokeAfterLoad();
        _editorHost.DocumentLoaded += (_, _) => BeginEditorCommandSmokeIfRequested();
        _editorHost.DocumentLoaded += async (_, _) => await ContinueDocumentSmokeAfterLoadAsync();
        _editorHost.SnapshotReceived += (_, message) => CompleteEditorSmoke(message);
        _editorHost.SnapshotReceived += (_, message) => CompleteEditorCommandSmoke(message);
        _editorHost.DirtyChanged += OnEditorDirtyChanged;
        _editorHost.CommandStateChanged += OnEditorCommandStateChanged;
        _editorHost.OpenLinkRequested += OnOpenLinkRequested;
        _editorHost.FilesDropped += OnEditorFilesDropped;
        _editorHost.PasteImageRequested += OnEditorPasteImageRequested;
        return panel;
    }

    private Control CreateSidePanel(string title, Control content)
    {
        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = title,
            Padding = new Padding(10, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText,
        };
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = SystemColors.Window,
        };
        panel.Controls.Add(content);
        panel.Controls.Add(header);
        return panel;
    }

    private TreeView CreateWorkspaceTree()
    {
        var tree = CreateTreeView();
        tree.Nodes.Add("尚未打开工作区");
        return tree;
    }

    private TreeView CreateOutlineTree()
    {
        var tree = CreateTreeView();
        tree.Nodes.Add("暂无文档大纲");
        return tree;
    }

    private static TreeView CreateTreeView()
    {
        return new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ShowLines = false,
            HideSelection = false,
            TabStop = true,
        };
    }

    private StatusStrip CreateStatusBar()
    {
        var strip = new StatusStrip
        {
            SizingGrip = false,
            ShowItemToolTips = true,
        };
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        strip.Items.Add(_statusLabel);
        strip.Items.Add(new ToolStripStatusLabel("阶段 5"));
        return strip;
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        _menuService.Attach(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs eventArgs)
    {
        _menuService.Detach();
        base.OnHandleDestroyed(eventArgs);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        return _commandRouter.TryExecuteShortcut(keyData)
            || base.ProcessCmdKey(ref message, keyData);
    }

    protected override void WndProc(ref Message message)
    {
        const int wmCommand = 0x0111;
        const int wmInitMenu = 0x0116;
        const int wmInitMenuPopup = 0x0117;

        if (message.Msg is wmInitMenu or wmInitMenuPopup)
        {
            _menuService.RefreshStates();
        }
        else if (message.Msg == wmCommand)
        {
            var commandId = unchecked((int)(message.WParam.ToInt64() & 0xffff));
            if (_commandRouter.TryExecuteById(commandId))
            {
                return;
            }
        }

        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWatchingDocument();
            _externalChangeTimer.Dispose();
            _editorHost?.Dispose();
            _menuService.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task OnMainFormShownAsync(bool maximize)
    {
        if (maximize)
        {
            WindowState = FormWindowState.Maximized;
        }

        _logger.Info($"Main window shown at DPI {_effectiveDpi}.");
        WriteWindowReport();

        if (_editorHost is not null)
        {
            await _editorHost.InitializeAsync();
        }

        if (!string.IsNullOrWhiteSpace(_options.SmokeCommand))
        {
            BeginInvoke(RunSmokeCommand);
            return;
        }

        if (_options.AutoCloseMilliseconds is { } milliseconds)
        {
            var timer = new System.Windows.Forms.Timer { Interval = Math.Max(100, milliseconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                Close();
            };
            timer.Start();
        }
    }

    private void RunSmokeCommand()
    {
        if (!Enum.TryParse<AppCommand>(_options.SmokeCommand, ignoreCase: true, out var command))
        {
            Environment.ExitCode = 2;
            Close();
            return;
        }

        var executed = _commandRouter.ExecuteIfEnabled(command);
        if (string.IsNullOrWhiteSpace(_options.CommandReportPath))
        {
            Close();
            return;
        }

        var report = new
        {
            Command = command.ToString(),
            Executed = executed,
            WorkspaceVisible = !_workspaceSplit.Panel1Collapsed,
            OutlineVisible = !_outlineSplit.Panel2Collapsed,
            FocusMode = _focusMode,
            Status = _statusLabel.Text,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_options.CommandReportPath)!);
        File.WriteAllText(
            _options.CommandReportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }

    private CommandState GetCommandState(AppCommand command)
    {
        var context = new CommandContext(
            DocumentAvailable: _document is not null,
            EditorReady: _editorHost?.IsDocumentLoaded == true,
            CanUndo: _editorCommandStatus.CanUndo,
            CanRedo: _editorCommandStatus.CanRedo,
            HasSelection: _editorCommandStatus.HasSelection,
            WorkspaceVisible: !_workspaceSplit.Panel1Collapsed,
            OutlineVisible: !_outlineSplit.Panel2Collapsed,
            FocusMode: _focusMode,
            SourceMode: false,
            ParagraphActive: _editorCommandStatus.Paragraph,
            HeadingLevel: _editorCommandStatus.HeadingLevel,
            BoldActive: _editorCommandStatus.Bold,
            ItalicActive: _editorCommandStatus.Italic,
            LinkActive: _editorCommandStatus.Link,
            QuoteActive: _editorCommandStatus.Blockquote,
            CodeBlockActive: _editorCommandStatus.CodeBlock,
            BulletListActive: _editorCommandStatus.BulletList,
            OrderedListActive: _editorCommandStatus.OrderedList,
            TaskListActive: _editorCommandStatus.TaskList,
            InTable: _editorCommandStatus.InTable,
            TableAlign: _editorCommandStatus.TableAlign,
            ImageSelected: _editorCommandStatus.ImageSelected,
            DocumentSaved: _document?.FilePath is not null);
        var state = CommandStateResolver.Resolve(command, context);
        if (state.IsEnabled
            && context.EditorReady
            && IsEditorCommand(command)
            && command != AppCommand.InsertImage
            && !TryMapEditorCommand(command, out _))
        {
            return new CommandState(false, state.IsChecked);
        }

        return state;
    }

    private void ExecuteCommand(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.NewDocument:
                _ = NewDocumentAsync();
                break;
            case AppCommand.OpenDocument:
                _ = OpenDocumentAsync();
                break;
            case AppCommand.SaveDocument:
                _ = SaveDocumentAsync(saveAs: false);
                break;
            case AppCommand.SaveDocumentAs:
                _ = SaveDocumentAsync(saveAs: true);
                break;
            case AppCommand.CleanUnreferencedAssets:
                _ = CleanUnreferencedAssetsAsync();
                break;
            case AppCommand.Cut:
                _ = ExecuteClipboardCopyAsync(cut: true);
                break;
            case AppCommand.Copy:
                _ = ExecuteClipboardCopyAsync(cut: false);
                break;
            case AppCommand.Paste:
                _ = PasteClipboardContentAsync();
                break;
            case AppCommand.ToggleWorkspace:
                _workspaceSplit.Panel1Collapsed = !_workspaceSplit.Panel1Collapsed;
                SetStatus(_workspaceSplit.Panel1Collapsed ? "工作区侧栏已隐藏" : "工作区侧栏已显示");
                break;
            case AppCommand.ToggleOutline:
                _outlineSplit.Panel2Collapsed = !_outlineSplit.Panel2Collapsed;
                SetStatus(_outlineSplit.Panel2Collapsed ? "文档大纲已隐藏" : "文档大纲已显示");
                break;
            case AppCommand.ToggleFocusMode:
                ToggleFocusMode();
                break;
            case AppCommand.ShowShortcuts:
                ShowShortcutHelp();
                break;
            case AppCommand.ShowAbout:
                ShowAbout();
                break;
            case AppCommand.InsertLink:
                InsertLink();
                break;
            case AppCommand.InsertImage:
                _ = SelectAndInsertImagesAsync();
                break;
            case AppCommand.Exit:
                Close();
                break;
            default:
                if (_editorHost?.IsDocumentLoaded == true && TryMapEditorCommand(command, out var editorCommand))
                {
                    _editorHost.ExecuteCommand(editorCommand);
                    SetStatus($"已发送命令：{command}");
                    break;
                }

                _logger.Warning($"Command has no available handler: {command}.");
                return;
        }

        _logger.Info($"Command executed: {command}.");
        _menuService.RefreshStates();
    }

    private void ToggleFocusMode()
    {
        if (!_focusMode)
        {
            _workspaceVisibleBeforeFocus = !_workspaceSplit.Panel1Collapsed;
            _outlineVisibleBeforeFocus = !_outlineSplit.Panel2Collapsed;
            _workspaceSplit.Panel1Collapsed = true;
            _outlineSplit.Panel2Collapsed = true;
            _focusMode = true;
            SetStatus("专注模式已开启");
            return;
        }

        _focusMode = false;
        _workspaceSplit.Panel1Collapsed = !_workspaceVisibleBeforeFocus;
        _outlineSplit.Panel2Collapsed = !_outlineVisibleBeforeFocus;
        SetStatus("专注模式已关闭");
    }

    private void ShowShortcutHelp()
    {
        const string shortcuts =
            "文件\r\n" +
            "Ctrl+N  新建    Ctrl+O  打开    Ctrl+S  保存\r\n" +
            "Ctrl+Shift+S  另存为\r\n\r\n" +
            "编辑\r\n" +
            "Ctrl+Z  撤销    Ctrl+Y  重做\r\n" +
            "Ctrl+F  查找    Ctrl+H  替换\r\n\r\n" +
            "格式\r\n" +
            "Ctrl+B  粗体    Ctrl+I  斜体    Ctrl+K  插入链接\r\n" +
            "Ctrl+1..6  标题级别\r\n\r\n" +
            "视图\r\n" +
            "F11  专注模式\r\n\r\n" +
            "需要编辑器或文档的命令将在相应功能接入后启用。";
        MessageBox.Show(this, shortcuts, "MarkLeaf 快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            "MarkLeaf\r\n\r\nWindows 原生轻量化 Markdown 编辑器\r\n阶段 6：表格、任务列表与图片",
            "关于 MarkLeaf",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
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
            EditorLifecycleState.Initializing => "正在初始化 WebView2",
            EditorLifecycleState.LoadingPage => "正在加载编辑器页面",
            EditorLifecycleState.WaitingForEditorReady => "正在等待编辑器就绪",
            EditorLifecycleState.Ready => "编辑器已就绪",
            EditorLifecycleState.Failed => "编辑器启动失败",
            _ => "正在准备编辑器",
        };
        _menuService.RefreshStates();

        if (_editorSession.State == EditorLifecycleState.Failed
            && !string.IsNullOrWhiteSpace(_options.EditorStateReportPath))
        {
            WriteEditorStateReport();
            BeginInvoke(Close);
        }
    }

    private static bool TryMapEditorCommand(AppCommand command, out string editorCommand)
    {
        editorCommand = command switch
        {
            AppCommand.Undo => "undo",
            AppCommand.Redo => "redo",
            AppCommand.ToggleBold => "toggleBold",
            AppCommand.ToggleItalic => "toggleItalic",
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
            _ => string.Empty,
        };
        return editorCommand.Length > 0;
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

    private static bool IsEditorCommand(AppCommand command)
    {
        return command is >= AppCommand.Undo and <= AppCommand.Replace
            || command is >= AppCommand.SetParagraph and <= AppCommand.DeleteTable
            || command == AppCommand.ToggleSourceMode;
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
            || _editorHost?.IsDocumentLoaded != false)
        {
            return;
        }

        _document ??= _documentFileService.CreateNew();
        if (!string.IsNullOrWhiteSpace(_options.EditorCommandSmoke))
        {
            _document.Markdown = "段落命令";
        }
        LoadDocumentIntoEditor(_document);
    }

    private void InsertLink()
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new LinkInputDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand("setLink", dialog.LinkAddress);
        SetStatus("已插入链接");
    }

    private void OnEditorCommandStateChanged(object? sender, EditorCommandStatus status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorCommandStateChanged(sender, status));
            return;
        }

        _editorCommandStatus = status;
        _menuService.RefreshStates();
    }

    private void OnOpenLinkRequested(object? sender, string url)
    {
        try
        {
            ExternalLinkService.Open(url);
            SetStatus("已在系统浏览器中打开链接");
        }
        catch (Exception exception)
        {
            _logger.Error("External link could not be opened.", exception);
            MessageBox.Show(
                this,
                "无法使用系统默认程序打开该链接。",
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task ExecuteClipboardCopyAsync(bool cut)
    {
        if (_editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        try
        {
            var selectedText = await _editorHost.GetSelectedTextAsync();
            if (string.IsNullOrEmpty(selectedText))
            {
                SetStatus("当前没有可复制的文本");
                return;
            }

            Clipboard.SetText(selectedText, TextDataFormat.UnicodeText);
            if (cut)
            {
                _editorHost.ExecuteCommand("deleteSelection");
            }
            SetStatus(cut ? "已剪切所选内容" : "已复制所选内容");
        }
        catch (Exception exception)
        {
            _logger.Error("Clipboard copy command failed.", exception);
            SetStatus("剪贴板操作失败");
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

            if (!Clipboard.ContainsText())
            {
                SetStatus("剪贴板中没有可粘贴的文本");
                return;
            }

            _editorHost.ExecuteCommand("pasteText", Clipboard.GetText(TextDataFormat.UnicodeText));
            SetStatus("已粘贴纯文本");
        }
        catch (Exception exception)
        {
            _logger.Error("Clipboard paste command failed.", exception);
            SetStatus("剪贴板操作失败");
        }
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

    private void SaveSettings()
    {
        if (_settingsSaved)
        {
            return;
        }

        _settingsSaved = true;
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        _settings.MainWindow = new WindowSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = WindowPlacementCalculator.ToLogicalPixels(bounds.Width, _effectiveDpi),
            Height = WindowPlacementCalculator.ToLogicalPixels(bounds.Height, _effectiveDpi),
            Dpi = _effectiveDpi,
            IsMaximized = WindowState == FormWindowState.Maximized,
            WorkspaceWidth = WindowPlacementCalculator.ToLogicalPixels(
                _workspaceSplit.SplitterDistance,
                _effectiveDpi),
            OutlineWidth = WindowPlacementCalculator.ToLogicalPixels(
                Math.Max(0, _outlineSplit.Width - _outlineSplit.SplitterDistance - _outlineSplit.SplitterWidth),
                _effectiveDpi),
        };

        try
        {
            _settingsService.SaveAsync(_settings).GetAwaiter().GetResult();
            _logger.Info("Window settings saved.");
        }
        catch (Exception exception)
        {
            _logger.Error("Window settings could not be saved.", exception);
        }
    }

    private void WriteWindowReport()
    {
        if (string.IsNullOrWhiteSpace(_options.WindowReportPath))
        {
            return;
        }

        var reportPath = _options.WindowReportPath;
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var report = new
        {
            Bounds.Left,
            Bounds.Top,
            Bounds.Width,
            Bounds.Height,
            EffectiveDpi = _effectiveDpi,
            WorkspaceWidth = _workspaceSplit.SplitterDistance,
            OutlineWidth = Math.Max(0, _outlineSplit.Width - _outlineSplit.SplitterDistance - _outlineSplit.SplitterWidth),
            WindowState = WindowState.ToString(),
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetSplitterDistanceSafely(
        SplitContainer split,
        int desiredPanelWidth,
        FixedPanel fixedPanel)
    {
        var minimum = split.Panel1MinSize;
        var maximum = Math.Max(minimum, split.Width - split.Panel2MinSize - split.SplitterWidth);
        var distance = fixedPanel == FixedPanel.Panel1
            ? desiredPanelWidth
            : split.Width - desiredPanelWidth - split.SplitterWidth;
        split.SplitterDistance = Math.Clamp(distance, minimum, maximum);
    }

}
