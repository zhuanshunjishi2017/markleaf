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
using MarkLeaf.Workspace;
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
    private readonly WorkspaceService _workspaceService = new();
    private readonly WorkspaceTreeView _workspaceTree;
    private readonly WorkspaceDocumentListView _workspaceDocumentList;
    private IReadOnlyList<WorkspaceDocumentEntry> _workspaceDocuments = [];
    private readonly OutlineTreeView _outlineTree;
    private EditorHostController? _editorHost;
    private MarkdownDocument? _document;
    private FileSystemWatcher? _documentWatcher;
    private FileSystemWatcher? _workspaceWatcher;
    private readonly WorkspaceChangeDebouncer _workspaceChangeDebouncer;
    private CancellationTokenSource? _workspaceLoadCancellation;
    private string? _workspaceRoot;
    private readonly System.Windows.Forms.Timer _externalChangeTimer = new() { Interval = 600 };
    private bool _documentOperationInProgress;
    private bool _closeApproved;
    private int _effectiveDpi;
    private readonly SplitContainer _sidebarSplit;
    private StatusStrip? _statusStrip;
    private readonly ToolStripButton _viewToggleButton = new("")
    {
        Font = new Font(SystemIconProvider.IconFontName, 10F, FontStyle.Regular, GraphicsUnit.Point),
        AutoSize = false,
        Width = 32,
        Margin = new Padding(2, 0, 4, 0),
    };
    private readonly ToolStripStatusLabel _statusLabel = new("正在准备编辑器");
    private readonly ToolStripStatusLabel _characterCountLabel = new("字数 0");
    private readonly ToolStripStatusLabel _blockTypeLabel = new("正文");
    private readonly ToolStripStatusLabel _positionLabel = new("行 1，列 1");
    private readonly ToolStripStatusLabel _encodingLabel = new("UTF-8");
    private readonly ToolStripStatusLabel _newLineLabel = new("CRLF");
    private readonly ToolStripStatusLabel _modeLabel = new("可视化");
    private bool _settingsSaved;
    private bool _focusMode;
    private bool _sidebarVisibleBeforeFocus = true;
    private bool _editorSmokeStarted;
    private bool _editorCommandSmokeStarted;
    private bool _documentSmokeStarted;
    private EditorCommandStatus _editorCommandStatus = EditorCommandStatus.Empty;
    private EditorStatus _editorStatus = EditorStatus.Empty;
    private bool _editorContextMenuActive;
    private bool _workspaceListViewActive;
    private bool _initialDocumentOpened;
    private WorkspaceDocumentSortOrder _workspaceDocumentSortOrder = WorkspaceDocumentSortOrder.ModifiedTimeDescending;
    private string _markdownStyle = "serif";

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
        _markdownStyle = settings.MarkdownStyle is "sans" or "print" or "retro-print"
            ? settings.MarkdownStyle
            : "serif";
        _settingsService = settingsService;
        _logger = logger;
        _imageAssetService = new ImageAssetService(paths.ClipboardImageCacheDirectory);
        _effectiveDpi = options.LayoutDpiOverride ?? DeviceDpi;
        _commandRouter = new CommandRouter(GetCommandState, ExecuteCommand);
        _menuService = new NativeMenuService(_commandRouter, GetRecentWorkspaces);
        _workspaceChangeDebouncer = new WorkspaceChangeDebouncer(
            TimeSpan.FromMilliseconds(500),
            QueueWorkspaceRefresh);
        _workspaceTree = new WorkspaceTreeView();
        _workspaceDocumentList = new WorkspaceDocumentListView();
        _outlineTree = new OutlineTreeView();
        _outlineTree.ConfigureTypography(_effectiveDpi);
        _workspaceDocumentList.ConfigureTypography(_effectiveDpi);
        _workspaceTree.ConfigureTypography(_effectiveDpi);

        Text = "MarkLeaf";
        ShowIcon = true;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "App", "App.ico");
        if (File.Exists(iconPath))
        {
            try { Icon = new Icon(iconPath); }
            catch { /* icon file may be malformed; title bar will use default */ }
        }
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

        _sidebarSplit = CreateSidebarSplit(placement.WorkspaceWidth);

        Controls.Add(_sidebarSplit);
        _statusStrip = CreateStatusBar();
        Controls.Add(_statusStrip);
        if (string.IsNullOrWhiteSpace(settings.Workspace.LastFolder)
            || !Directory.Exists(settings.Workspace.LastFolder))
        {
            _sidebarSplit.Panel1Collapsed = true;
        }

        Shown += async (_, _) => await OnMainFormShownAsync(placement.IsMaximized);
        FormClosing += OnMainFormClosing;
        DpiChanged += (_, args) =>
        {
            if (_options.LayoutDpiOverride is null)
            {
                _effectiveDpi = args.DeviceDpiNew;
            }

            _logger.Info($"Main window DPI changed: {args.DeviceDpiOld} -> {args.DeviceDpiNew}.");
            _workspaceTree.ConfigureTypography(_effectiveDpi);
            _workspaceDocumentList.ConfigureTypography(_effectiveDpi);
            _outlineTree.ConfigureTypography(_effectiveDpi);
        };
    }

    private SplitContainer CreateSidebarSplit(int sidebarWidth)
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
        split.Panel1.Controls.Add(CreateSidebarTabs());
        split.Panel2.Controls.Add(CreateEditorHost());
        split.HandleCreated += (_, _) => SetSplitterDistanceSafely(split, sidebarWidth, FixedPanel.Panel1);
        return split;
    }

    private Control CreateSidebarTabs()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 8),
            BackColor = Color.White,
        };
        tabs.TabPages.Add(CreateSidebarPage("工作区", CreateWorkspacePanel()));
        tabs.TabPages.Add(CreateSidebarPage("大纲", CreateOutlineTree()));
        return tabs;
    }

    private static TabPage CreateSidebarPage(string title, Control content)
    {
        var page = new TabPage(title)
        {
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(0xF9, 0xF9, 0xF9),
        };
        page.Controls.Add(content);
        return page;
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
        _editorHost.DocumentLoaded += (_, _) => SetMarkdownStyle(_markdownStyle);
        _editorHost.SnapshotReceived += (_, message) => CompleteEditorSmoke(message);
        _editorHost.SnapshotReceived += (_, message) => CompleteEditorCommandSmoke(message);
        _editorHost.DirtyChanged += OnEditorDirtyChanged;
        _editorHost.CommandStateChanged += OnEditorCommandStateChanged;
        _editorHost.EditorStatusChanged += OnEditorStatusChanged;
        _editorHost.ContextMenuRequested += OnEditorContextMenuRequested;
        _editorHost.OutlineChanged += OnEditorOutlineChanged;
        _editorHost.OutlineSelectionChanged += OnEditorOutlineSelectionChanged;
        _editorHost.OpenLinkRequested += OnOpenLinkRequested;
        _editorHost.FilesDropped += OnEditorFilesDropped;
        _editorHost.PasteImageRequested += OnEditorPasteImageRequested;
        return panel;
    }

    private WorkspaceTreeView CreateWorkspaceTree()
    {
        _workspaceTree.NodeExpanding += async (_, args) =>
            await LoadWorkspaceDirectoryAsync(args.Entry.FullPath, _workspaceLoadCancellation?.Token ?? CancellationToken.None);
        _workspaceTree.NodeActivated += async (_, args) => await ActivateWorkspaceTreeEntryAsync(args.Entry);
        _workspaceTree.NodeContextRequested += (_, args) =>
            _ = ShowWorkspaceEntryMenuAsync(args.Entry, args.ScreenPoint);
        _workspaceTree.WorkspaceMenuRequested += (_, args) =>
            _ = ShowWorkspaceFolderMenuAtAsync(args.ScreenPoint);
        return _workspaceTree;
    }

    private Control CreateWorkspacePanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Color.FromArgb(0xF9, 0xF9, 0xF9),
        };
        content.Controls.Add(CreateWorkspaceTree());
        _workspaceDocumentList.Visible = false;
        _workspaceDocumentList.DocumentActivated += async (_, path) => await ActivateWorkspaceDocumentAsync(path);
        _workspaceDocumentList.DocumentContextRequested += (_, args) =>
            _ = ShowWorkspaceEntryMenuAsync(
                new WorkspaceEntry(args.Document.Name, args.Document.FullPath, false),
                args.ScreenPoint);
        _workspaceDocumentList.BackgroundContextRequested += (_, args) =>
            _ = ShowWorkspaceFolderMenuAtAsync(args.ScreenPoint);
        content.Controls.Add(_workspaceDocumentList);

        layout.Controls.Add(content, 0, 0);
        return layout;
    }

    private OutlineTreeView CreateOutlineTree()
    {
        _outlineTree.NodeActivated += (_, position) => ActivateOutlinePosition(position);
        return _outlineTree;
    }

    private void UpdateViewToggleIcon()
    {
        _viewToggleButton.Text = _workspaceListViewActive
            ? SystemIconProvider.ListViewIcon
            : SystemIconProvider.TreeViewIcon;
    }

    private StatusStrip CreateStatusBar()
    {
        var strip = new StatusStrip
        {
            SizingGrip = false,
            ShowItemToolTips = true,
        };
        _viewToggleButton.Click += (_, _) => ToggleWorkspaceView();
        _viewToggleButton.ToolTipText = "切换视图";
        UpdateViewToggleIcon();
        strip.Items.Add(_viewToggleButton);
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        strip.Items.Add(_statusLabel);
        strip.Items.Add(_characterCountLabel);
        strip.Items.Add(_blockTypeLabel);
        strip.Items.Add(_positionLabel);
        strip.Items.Add(_encodingLabel);
        strip.Items.Add(_newLineLabel);
        strip.Items.Add(_modeLabel);
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
            StopWatchingWorkspace();
            _externalChangeTimer.Dispose();
            _workspaceChangeDebouncer.Dispose();
            _workspaceLoadCancellation?.Dispose();
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

        if (!string.IsNullOrWhiteSpace(_options.InitialDocumentPath))
        {
            _initialDocumentOpened = true;
            await OpenDocumentPathAsync(_options.InitialDocumentPath);
        }

        if (!string.IsNullOrWhiteSpace(_settings.Workspace.LastFolder)
            && Directory.Exists(_settings.Workspace.LastFolder))
        {
            await OpenWorkspaceAsync(_settings.Workspace.LastFolder);
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
            SidebarVisible = !_sidebarSplit.Panel1Collapsed,
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
        if (command == AppCommand.SetSerifStyle)
            return new(_editorHost?.IsDocumentLoaded == true, _markdownStyle == "serif");
        if (command == AppCommand.SetSansStyle)
            return new(_editorHost?.IsDocumentLoaded == true, _markdownStyle == "sans");
        if (command == AppCommand.SetPrintStyle)
            return new(_editorHost?.IsDocumentLoaded == true, _markdownStyle == "print");
        if (command == AppCommand.SetRetroPrintStyle)
            return new(_editorHost?.IsDocumentLoaded == true, _markdownStyle == "retro-print");
        if (command is >= AppCommand.OpenRecentWorkspace1 and <= AppCommand.OpenRecentWorkspace8)
        {
            var index = (int)command - (int)AppCommand.OpenRecentWorkspace1;
            return new CommandState(index < GetRecentWorkspaces().Count);
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
            StatusBarVisible: _statusStrip?.Visible != false);
        var state = CommandStateResolver.Resolve(command, context);
        if (state.IsEnabled
            && context.EditorReady
            && IsEditorCommand(command)
            && command != AppCommand.InsertImage
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
                _sidebarSplit.Panel1Collapsed = !_sidebarSplit.Panel1Collapsed;
                if (!_sidebarSplit.Panel1Collapsed && _workspaceRoot is null)
                {
                    ShowNoWorkspacePlaceholder();
                }
                SetStatus(_sidebarSplit.Panel1Collapsed ? "侧栏已隐藏" : "侧栏已显示");
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
            case AppCommand.ShowAbout:
                ShowAbout();
                break;
            case AppCommand.InsertLink:
                InsertLink();
                break;
            case AppCommand.InsertImage:
                _ = SelectAndInsertImagesAsync();
                break;
            case AppCommand.SetSerifStyle:
                SetMarkdownStyle("serif");
                break;
            case AppCommand.SetSansStyle:
                SetMarkdownStyle("sans");
                break;
            case AppCommand.SetPrintStyle:
                SetMarkdownStyle("print");
                break;
            case AppCommand.SetRetroPrintStyle:
                SetMarkdownStyle("retro-print");
                break;
            case AppCommand.Exit:
                Close();
                break;
            default:
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

    private void ToggleFocusMode()
    {
        if (!_focusMode)
        {
            _sidebarVisibleBeforeFocus = !_sidebarSplit.Panel1Collapsed;
            _sidebarSplit.Panel1Collapsed = true;
            _menuService.Detach();
            MainMenuStrip = null;
            if (_statusStrip is not null) _statusStrip.Visible = false;
            _focusMode = true;
            SetStatus("专注模式已开启");
            return;
        }

        _focusMode = false;
        _sidebarSplit.Panel1Collapsed = !_sidebarVisibleBeforeFocus;
        if (!IsDisposed)
        {
            _menuService.Attach(Handle);
            if (_statusStrip is not null) _statusStrip.Visible = true;
        }
        SetStatus("专注模式已关闭");
    }

    private void SetMarkdownStyle(string style)
    {
        _markdownStyle = style is "sans" or "print" or "retro-print" ? style : "serif";
        _editorHost?.ExecuteCommand("setStyle", _markdownStyle);
        _menuService.RefreshStates();
    }

    private void OpenDocumentInNewWindow()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = DocumentFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            Title = "在新窗口中打开 Markdown 文档",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            StartNewWindow(dialog.FileName);
        }
    }

    private void StartNewWindow(string? documentPath = null)
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定 MarkLeaf 可执行文件路径。");
            var startInfo = new System.Diagnostics.ProcessStartInfo(executable)
            {
                UseShellExecute = false,
            };
            if (!string.IsNullOrWhiteSpace(documentPath))
            {
                startInfo.ArgumentList.Add("--open-document");
                startInfo.ArgumentList.Add(Path.GetFullPath(documentPath));
            }
            System.Diagnostics.Process.Start(startInfo);
            SetStatus(documentPath is null ? "已打开新窗口" : "文档已在新窗口中打开");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not start a new MarkLeaf window.", exception);
            MessageBox.Show(this, "无法打开新窗口。\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowShortcutHelp()
    {
        using var dialog = new ShortcutDialog();
        dialog.ShowDialog(this);
    }

    private void ShowAbout()
    {
        var displayName = FetchGitHubDisplayName();
        using var dialog = new AboutDialog(displayName);
        dialog.ShowDialog(this);
    }

    private static string? FetchGitHubDisplayName()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MarkLeaf");
            var response = client.GetStringAsync("https://api.github.com/users/zhuanshunjishi2017")
                .GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return name.GetString();
            }
        }
        catch
        {
        }

        return null;
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
            || command is AppCommand.ToggleUnderline or AppCommand.ToggleStrike or AppCommand.ToggleInlineCode
                or AppCommand.PromoteHeading or AppCommand.DemoteHeading
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
            || _initialDocumentOpened
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

    private void ShowExportCompleteDialog(string fileName, string filePath, string folderPath)
    {
        var openButton = new TaskDialogButton("打开");
        openButton.Click += (_, _) => ExternalLinkService.OpenLocal(filePath);

        var openFolderButton = new TaskDialogButton("打开所在文件夹");
        openFolderButton.Click += (_, _) => ExternalLinkService.OpenLocal(folderPath);

        var page = new TaskDialogPage
        {
            Caption = "MarkLeaf",
            Icon = TaskDialogIcon.Information,
            Heading = "导出完成",
            Text = $"{fileName}\n已导出到：\n{filePath}",
            Buttons = { openButton, openFolderButton, TaskDialogButton.Close },
        };

        TaskDialog.ShowDialog(this, page);
    }

    private async Task ExportDocumentAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document is null)
        {
            return;
        }

        var docName = _document.FilePath is not null
            ? Path.GetFileName(_document.FilePath)
            : "未命名文档";
        var defaultName = _document.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : "未命名文档";
        using var dialog = new ExportDialog(docName, defaultName, _markdownStyle);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var options = dialog.Options;
        if (options is null || string.IsNullOrWhiteSpace(options.OutputPath))
        {
            SetStatus("导出路径不能为空");
            return;
        }

        var exportDir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(exportDir) && !Directory.Exists(exportDir))
        {
            Directory.CreateDirectory(exportDir);
        }

        try
        {
            SetStatus("正在生成导出内容…");
            var html = await _editorHost.RequestExportAsync(
                options.Format,
                options.Style,
                options.HtmlHeader,
                options.HtmlFooter);

            if (string.IsNullOrEmpty(html))
            {
                SetStatus("导出失败：编辑器未返回内容");
                return;
            }

            var outputPath = options.OutputPath;
            if (!Path.HasExtension(outputPath))
            {
                outputPath = Path.ChangeExtension(
                    outputPath,
                    options.Format == "pdf" ? ".pdf" : ".html");
            }

            if (options.Format == "pdf")
            {
                SetStatus("正在生成 PDF…");
                var pdfBytes = await _editorHost.PrintExportToPdfAsync(
                    html,
                    options.PaperSize,
                    options.Landscape,
                    options.MarginTop,
                    options.MarginBottom,
                    options.MarginLeft,
                    options.MarginRight);
                await File.WriteAllBytesAsync(outputPath, pdfBytes);
            }
            else
            {
                await File.WriteAllTextAsync(outputPath, html, System.Text.Encoding.UTF8);
            }

            SetStatus("文档已导出");
            _logger.Info($"Document exported: {options.Format}/{options.Style} → {outputPath}");

            var exportedName = Path.GetFileName(outputPath);
            ShowExportCompleteDialog(exportedName, outputPath, exportDir!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Export failed: {options.OutputPath}.", exception);
            MessageBox.Show(this, "导出失败。\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
        RefreshPersistentStatusBar();
        _menuService.RefreshStates();
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

    private static bool IsInlineFormatCommand(AppCommand command) =>
        command is AppCommand.ToggleBold or AppCommand.ToggleItalic;

    private void RefreshPersistentStatusBar()
    {
        _characterCountLabel.Text = StatusBarFormatter.FormatCharacterCount(_editorStatus);
        _blockTypeLabel.Text = StatusBarFormatter.FormatBlockType(_editorStatus.BlockType);
        _positionLabel.Text = StatusBarFormatter.FormatPosition(_editorStatus);
        _encodingLabel.Text = _document is null
            ? "UTF-8"
            : StatusBarFormatter.FormatEncoding(_document.Encoding, _document.HasBom);
        _newLineLabel.Text = _document is null
            ? StatusBarFormatter.FormatNewLine(Environment.NewLine)
            : StatusBarFormatter.FormatNewLine(_document.NewLine);
        _modeLabel.Text = _editorCommandStatus.SourceMode ? "源码" : "可视化";
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
                SetStatus("当前没有可复制的文本");
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

            if (!_editorCommandStatus.SourceMode
                && Clipboard.TryGetData<string>(DataFormats.Html, out var clipboardHtml))
            {
                if (!string.IsNullOrWhiteSpace(clipboardHtml))
                {
                    _editorHost.ExecuteCommand("pasteHtml", ClipboardHtmlFormatter.ExtractFragment(clipboardHtml));
                    SetStatus("已粘贴格式化内容");
                    return;
                }
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
                _sidebarSplit.SplitterDistance,
                _effectiveDpi),
            OutlineWidth = _settings.MainWindow.OutlineWidth,
        };
        _settings.Workspace.LastFolder = _workspaceRoot;
        _settings.Workspace.RecentFolders = GetRecentWorkspaces().ToList();
        _settings.MarkdownStyle = _markdownStyle;

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
            WorkspaceWidth = _sidebarSplit.SplitterDistance,
            OutlineWidth = 0,
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
