using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.ExternalLinks;
using MarkLeaf.Services.Recovery;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using MarkLeaf.UI.Dialogs;
using MarkLeaf.Services.Styles;
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
    private Control _workspacePanelHost = default!;
    private Control _outlinePanelHost = default!;
    private Panel _sidebarPanel = default!;
    private Panel _editorPanel = default!;
    private Panel _workspaceContentPanel = default!;
    private EditorLoadingView _editorLoadingView = default!;
    private IReadOnlyList<WorkspaceDocumentEntry> _workspaceDocuments = [];
    private readonly OutlineTreeView _outlineTree;
    private readonly SidebarTabBar _sidebarTabBar = new();
    private readonly OpenFolderPrompt _openFolderPrompt = new();
    private EditorHostController? _editorHost;
    private WebView2? _webView;
    private MarkdownDocument? _document;
    private FileSystemWatcher? _documentWatcher;
    private FileSystemWatcher? _workspaceWatcher;
    private readonly RecoveryService _recoveryService;
    private readonly System.Windows.Forms.Timer _recoveryTimer = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer _autoSaveTimer = new() { Interval = 500 };
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
    private readonly ToolStripStatusLabel _statusLabel = new(Services.Loc.Get("statusBar.preparing"));
    private readonly ToolStripStatusLabel _characterCountLabel = new(Loc.Format("statusBar.wordCount", 0));
    private readonly ToolStripStatusLabel _blockTypeLabel = new(Loc.Get("statusBar.blockType.paragraph"));
    private readonly ToolStripStatusLabel _positionLabel = new(Loc.Format("statusBar.position", 1, 1));
    private readonly ToolStripStatusLabel _encodingLabel = new("UTF-8");
    private readonly ToolStripStatusLabel _newLineLabel = new("CRLF");
    private readonly ToolStripStatusLabel _modeLabel = new(Loc.Get("statusBar.mode.visual"));
    private readonly ToolStripStatusLabel _zoomLabel = new("100%");
    private SolidBrush _menuBgBrush = new(Color.White);
    private SolidBrush _menuHighlightBrush = new(Color.FromArgb(0xF0, 0xF0, 0xF0));
    private SolidBrush _menuTextBrush = new(Color.Black);
    private SolidBrush _menuDisabledBrush = new(Color.FromArgb(0x6D, 0x6D, 0x6D));
    private bool _menuDarkMode;
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
    private bool _sidebarActiveOutline;
    private bool _initialDocumentOpened;
    private WorkspaceDocumentSortOrder _workspaceDocumentSortOrder = WorkspaceDocumentSortOrder.ModifiedTimeDescending;
    private string _markdownStyle = "serif";
    private string _colorTheme = "white";
    private int _zoomPercent = 100;

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
        _markdownStyle = StyleService.TryGetStyle(settings.MarkdownStyle) is not null
            ? settings.MarkdownStyle
            : StyleService.DefaultStyleId;
        _colorTheme = ColorThemeService.TryGetTheme(settings.ColorTheme) is not null
            ? settings.ColorTheme
            : ColorThemeService.All.Count > 0 ? ColorThemeService.All[0].Id : "white";
        if (settings.Appearance.FollowSystemColorMode)
            _colorTheme = ColorThemeService.GetSystemDefaultThemeId();
        ColorThemeService.SetActiveTheme(_colorTheme);
        _zoomPercent = NearestZoom(settings.Appearance.ZoomPercent);
        _settingsService = settingsService;
        _logger = logger;
        if (string.IsNullOrWhiteSpace(_settings.Image.DefaultDirectory))
        {
            _settings.Image.DefaultDirectory = _paths.DefaultImageDirectory;
        }
        _imageAssetService = new ImageAssetService(_paths.DefaultImageDirectory);
        _effectiveDpi = options.LayoutDpiOverride ?? DeviceDpi;
        _commandRouter = new CommandRouter(GetCommandState, ExecuteCommand);
        _menuService = new NativeMenuService(_commandRouter, GetRecentWorkspaces, GetRecentFiles, () => _markdownStyle, () => _zoomPercent, () => _colorTheme);
        _workspaceChangeDebouncer = new WorkspaceChangeDebouncer(
            TimeSpan.FromMilliseconds(500),
            QueueWorkspaceRefresh);
        _recoveryService = new RecoveryService(paths.RecoveryDirectory, logger);
        _recoveryService.SnapshotSaved += (_, time) =>
            BeginInvoke(() => SetStatus(Loc.Format("status.snapshotSaved", $"{time.LocalDateTime:HH:mm:ss}")));
        _recoveryTimer.Tick += OnRecoveryTimerTick;
        _recoveryTimer.Interval = Math.Clamp(settings.File.SnapshotIntervalSeconds, 10, 300) * 1000;
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
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
            _sidebarSplit.Panel1Collapsed = settings.MainWindow.SidebarCollapsed;
            if (!_sidebarSplit.Panel1Collapsed)
                ShowNoWorkspacePlaceholder();
        }

        // 原生部分立即加载颜色主题，不等待编辑器就绪。
        ApplySidebarColors();
        ApplyWindowDarkMode(ColorThemeService.IsActiveThemeDark());

        Shown += async (_, _) => await OnMainFormShownAsync(placement.IsMaximized);
        FormClosing += OnMainFormClosing;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        DpiChanged += (_, args) =>
        {
            if (_options.LayoutDpiOverride is null)
            {
                _effectiveDpi = args.DeviceDpiNew;
            }

            _logger.Info($"Main window DPI changed: {args.DeviceDpiOld} -> {args.DeviceDpiNew}.");
            _sidebarTabBar.ConfigureTypography(_effectiveDpi);
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
        split.Panel1.Controls.Add(CreateSidebarPanel());
        split.Panel2.Controls.Add(CreateEditorHost());
        split.HandleCreated += (_, _) => SetSplitterDistanceSafely(split, sidebarWidth, FixedPanel.Panel1);
        return split;
    }

    private Control CreateSidebarPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
        };
        _sidebarPanel = panel;

        _sidebarTabBar.TabChanged += (_, index) => ShowSidebarView(outline: index == 1);
        _sidebarTabBar.TabReclicked += (_, index) =>
        {
            if (index == 0) ToggleWorkspaceView();
        };
        _sidebarTabBar.CollapseClicked += OnSidebarCollapseClicked;
        panel.Controls.Add(_sidebarTabBar);

        _workspacePanelHost = CreateWorkspacePanel();
        _workspacePanelHost.Dock = DockStyle.Fill;
        _outlinePanelHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
        };
        _outlinePanelHost.Controls.Add(CreateOutlineTree());
        panel.Controls.Add(_outlinePanelHost);
        panel.Controls.Add(_workspacePanelHost);

        ShowSidebarView(outline: false);
        return panel;
    }

    private Control CreateEditorHost()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
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
        var loadingView = new EditorLoadingView();
        _editorLoadingView = loadingView;
        panel.Controls.Add(webView);
        panel.Controls.Add(loadingView);
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
            _editorHost?.ApplyStyles(StyleService.BaseCss, StyleService.Styles, _markdownStyle);
            var e = _settings.Editor;
            _editorHost?.ApplyCssVariables(e.VisualLineHeight, e.VisualFontSize, e.VisualMaxContentWidth, e.SourceFontSize, e.SourceFontFamily, e.SourceCjkFontFamily);
            _editorHost?.ApplySourceSettings(e.SourceIndentWidth);
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
        _editorHost.CommandStateChanged += OnEditorCommandStateChanged;
        _editorHost.EditorStatusChanged += OnEditorStatusChanged;
        _editorHost.ContextMenuRequested += OnEditorContextMenuRequested;
        _editorHost.OutlineChanged += OnEditorOutlineChanged;
        _editorHost.OutlineSelectionChanged += OnEditorOutlineSelectionChanged;
        _editorHost.OpenLinkRequested += OnOpenLinkRequested;
        _editorHost.FilesDropped += OnEditorFilesDropped;
        _editorHost.PasteImageRequested += OnEditorPasteImageRequested;
        _editorHost.ZoomWheelRequested += (_, deltaY) =>
        {
            if (!_settings.Appearance.CtrlWheelZoom)
            {
                return;
            }
            SetZoomPercent(NextZoom(_zoomPercent, deltaY < 0 ? 1 : -1));
        };
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
        _workspaceTree.FilesDropped += (_, args) =>
            _ = ImportWorkspaceFilesAsync(args.Paths);
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
            BackColor = Color.White,
        };
        _workspaceContentPanel = content;
        content.Controls.Add(CreateWorkspaceTree());
        _workspaceDocumentList.Visible = false;
        _workspaceDocumentList.DocumentActivated += async (_, path) => await ActivateWorkspaceDocumentAsync(path);
        _workspaceDocumentList.DocumentContextRequested += (_, args) =>
            _ = ShowWorkspaceEntryMenuAsync(
                new WorkspaceEntry(args.Document.Name, args.Document.FullPath, false),
                args.ScreenPoint);
        _workspaceDocumentList.BackgroundContextRequested += (_, args) =>
            _ = ShowWorkspaceFolderMenuAtAsync(args.ScreenPoint);
        _workspaceDocumentList.FilesDropped += (_, args) =>
            _ = ImportWorkspaceFilesAsync(args.Paths);
        content.Controls.Add(_workspaceDocumentList);

        _openFolderPrompt.Dock = DockStyle.Fill;
        _openFolderPrompt.Visible = false;
        _openFolderPrompt.FolderOpenRequested += async (_, _) => await SelectWorkspaceFolderAsync();
        content.Controls.Add(_openFolderPrompt);

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
        if (_sidebarSplit.Panel1Collapsed)
        {
            _viewToggleButton.Text = SystemIconProvider.ExpandSidebarIcon;
            _viewToggleButton.ToolTipText = Loc.Get("tooltip.expandSidebar");
        }
        else
        {
            _viewToggleButton.Text = _workspaceListViewActive
                ? SystemIconProvider.ListViewIcon
                : SystemIconProvider.TreeViewIcon;
            _viewToggleButton.ToolTipText = Loc.Get("tooltip.switchView");
        }
    }

    private StatusStrip CreateStatusBar()
    {
        var strip = new StatusStrip
        {
            SizingGrip = false,
            ShowItemToolTips = true,
            MinimumSize = new Size(0, 45),
            Renderer = new SolidStatusBarRenderer(),
        };
        _viewToggleButton.Click += (_, _) =>
        {
            if (_sidebarSplit.Panel1Collapsed)
            {
                ExpandSidebar();
            }
            else
            {
                ToggleWorkspaceView();
            }
        };
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
        _zoomLabel.Text = $"{_zoomPercent}%";
        strip.Items.Add(_zoomLabel);
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

    protected override void WndProc(ref Message message)
    {
        const int wmCommand = 0x0111;
        const int wmInitMenu = 0x0116;
        const int wmInitMenuPopup = 0x0117;

        if (_menuDarkMode && IsHandleCreated && !IsDisposed)
        {
            switch (message.Msg)
            {
                case MenuBarDarkMode.WmUahDrawMenu:
                    DrawMenuBarBackground(ref message);
                    return;
                case MenuBarDarkMode.WmUahDrawMenuItem:
                    DrawDarkMenuItem(ref message);
                    return;
                case MenuBarDarkMode.WmUahMeasureMenuItem:
                    base.WndProc(ref message);
                    return;
                case MenuBarDarkMode.WmNcPaint:
                case 0x0086: // WM_NCACTIVATE
                    base.WndProc(ref message);
                    OverpaintMenuSeparator();
                    return;
            }
        }

        if (message.Msg is wmInitMenu or wmInitMenuPopup)
        {
            _menuService.RefreshStates();
        }
        else if (message.Msg == wmCommand)
        {
            var commandId = unchecked((int)(message.WParam.ToInt64() & 0xffff));
            if (_menuService.TryGetStyleByCommandId((uint)commandId, out var styleId))
            {
                SetMarkdownStyle(styleId);
                return;
            }

            if (_menuService.TryGetZoomByCommandId((uint)commandId, out var zoomPercent))
            {
                SetZoomPercent(zoomPercent);
                return;
            }

            if (_menuService.TryGetColorThemeByCommandId((uint)commandId, out var colorThemeId))
            {
                SetColorTheme(colorThemeId);
                return;
            }

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
            _recoveryTimer.Dispose();
            _autoSaveTimer.Dispose();
            _recoveryService.Dispose();
            _workspaceLoadCancellation?.Dispose();
            _editorHost?.Dispose();
            _menuService.Dispose();
            _menuBgBrush.Dispose();
            _menuHighlightBrush.Dispose();
            _menuTextBrush.Dispose();
            _menuDisabledBrush.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task OnMainFormShownAsync(bool maximize)
    {
        if (maximize)
        {
            WindowState = FormWindowState.Maximized;
        }

        TopMost = _settings.Appearance.TopMostWindow;
        ApplyWindowDarkMode(ColorThemeService.IsActiveThemeDark());

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

        if (_settings.File.StartupAction != StartupAction.NewDocument
            && !string.IsNullOrWhiteSpace(_settings.Workspace.LastFolder)
            && Directory.Exists(_settings.Workspace.LastFolder))
        {
            await OpenWorkspaceAsync(_settings.Workspace.LastFolder);

            if (_settings.File.StartupAction == StartupAction.OpenLastWorkspaceAndFiles
                && !string.IsNullOrWhiteSpace(_settings.Workspace.LastFile)
                && File.Exists(_settings.Workspace.LastFile)
                && !_initialDocumentOpened)
            {
                await OpenDocumentPathAsync(_settings.Workspace.LastFile);
            }
        }

        if (_settings.File.StartupAction == StartupAction.NewDocument
            && !_initialDocumentOpened)
        {
            _sidebarSplit.Panel1Collapsed = true;
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
            OutlineActive: _sidebarActiveOutline);
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

    private void ResetAllSettingsToDefaults()
    {
        _settingsService.SaveAsync(_settings).GetAwaiter().GetResult();
        SetStatus(Loc.Get("status.settingsReset"));
    }

    private void ApplySidebarAutoHideScrollbar()
    {
        var enabled = _settings.Appearance.AutoHideScrollbars;
        _workspaceTree.AutoHideScrollbar = enabled;
        _workspaceDocumentList.AutoHideScrollbar = enabled;
        _outlineTree.AutoHideScrollbar = enabled;
    }

    private void OnSidebarCollapseClicked(object? sender, EventArgs e)
    {
        ToggleSidebarWithWindowResize();
    }

    /// <summary>
    /// 切换侧边栏显隐并同步调整窗口宽度，保持左上角固定、编辑器区宽度不变。
    /// </summary>
    private void ToggleSidebarWithWindowResize()
    {
        if (_sidebarSplit.Panel1Collapsed)
        {
            ExpandSidebar();
        }
        else
        {
            CollapseSidebar();
        }
    }

    private void CollapseSidebar()
    {
        if (_sidebarSplit.Panel1Collapsed) return;
        _sidebarSplit.Panel1Collapsed = true;
        _settings.MainWindow.SidebarCollapsed = true;
        UpdateViewToggleIcon();
        if (_workspaceRoot is null)
            _openFolderPrompt.Visible = true;
        SetStatus(Loc.Get("status.sidebarCollapsed"));
    }

    private void ExpandSidebar()
    {
        if (!_sidebarSplit.Panel1Collapsed) return;
        _sidebarSplit.Panel1Collapsed = false;
        _settings.MainWindow.SidebarCollapsed = false;
        UpdateViewToggleIcon();
        if (_workspaceRoot is null) ShowNoWorkspacePlaceholder();
        SetStatus(Loc.Get("status.sidebarExpanded"));
    }

    private void ShowSidebarView(bool outline)
    {
        _sidebarActiveOutline = outline;
        _sidebarTabBar.SetSelectedIndexSilently(outline ? 1 : 0);
        _workspacePanelHost.Visible = !outline;
        _outlinePanelHost.Visible = outline;
        if (outline)
        {
            _outlinePanelHost.BringToFront();
        }
        else
        {
            _workspacePanelHost.BringToFront();
        }

        if (_workspaceRoot is null)
            _openFolderPrompt.BringToFront();

        _menuService.RefreshStates();
    }

    private void ToggleFocusMode()
    {
        if (!_focusMode)
        {
            _sidebarVisibleBeforeFocus = !_sidebarSplit.Panel1Collapsed;
            if (_sidebarVisibleBeforeFocus)
                CollapseSidebar();
            _menuService.Detach();
            MainMenuStrip = null;
            if (_statusStrip is not null) _statusStrip.Visible = false;
            _focusMode = true;
            SetStatus(Loc.Get("status.focusModeOn"));
            return;
        }

        _focusMode = false;
        if (!IsDisposed)
        {
            _menuService.Attach(Handle);
            if (_statusStrip is not null) _statusStrip.Visible = true;
        }

        if (_sidebarVisibleBeforeFocus)
            ExpandSidebar();
        SetStatus(Loc.Get("status.focusModeOff"));
    }

    private void SetMarkdownStyle(string style)
    {
        _markdownStyle = StyleService.TryGetStyle(style) is not null ? style : StyleService.DefaultStyleId;
        _settings.MarkdownStyle = _markdownStyle;
        var editor = _settings.Editor;
        _editorHost?.ApplyCssVariables(editor.VisualLineHeight, editor.VisualFontSize, editor.VisualMaxContentWidth, editor.SourceFontSize, editor.SourceFontFamily, editor.SourceCjkFontFamily);
        _editorHost?.ApplySourceSettings(editor.SourceIndentWidth);
        _editorHost?.ExecuteCommand("setStyle", _markdownStyle);
        _menuService.RefreshStates();
    }

    private void SetColorTheme(string themeId)
    {
        if (ColorThemeService.TryGetTheme(themeId) is null) return;
        _colorTheme = themeId;
        ColorThemeService.SetActiveTheme(themeId);
        _settings.ColorTheme = themeId;
        ApplySidebarColors();
        _editorHost?.ApplyStyles(StyleService.BaseCss, StyleService.Styles, _markdownStyle);
        _menuService.RefreshStates();
        ApplyWindowDarkMode(ColorThemeService.IsActiveThemeDark());
    }

    private void ApplyWindowDarkMode(bool dark)
    {
        DarkModeService.Apply(dark);
        if (!IsHandleCreated) return;
        var value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref value,
            sizeof(int));
        NativeMethods.SetWindowPos(Handle, 0, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize
            | NativeMethods.SwpNoZOrder | NativeMethods.SwpFrameChanged);
        NativeMethods.DrawMenuBar(Handle);
    }

    private void OnSystemPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General)
            return;
        if (!_settings.Appearance.FollowSystemColorMode)
            return;

        var targetThemeId = ColorThemeService.GetSystemDefaultThemeId();
        if (string.Equals(_colorTheme, targetThemeId, StringComparison.Ordinal))
            return;

        BeginInvoke(() => SetColorTheme(targetThemeId));
    }

    private void OverpaintMenuSeparator()
    {
        try
        {
            MenuBarDarkMode.GetWindowRect(Handle, out var rcWindow);
            MenuBarDarkMode.GetClientRect(Handle, out var rcClient);
            var clientTopLeft = new System.Drawing.Point(0, 0);
            MenuBarDarkMode.ClientToScreen(Handle, ref clientTopLeft);

            var clientTop = clientTopLeft.Y - rcWindow.Top;
            var stripLeft = clientTopLeft.X - rcWindow.Left;
            var stripWidth = rcClient.Right - rcClient.Left;

            var dc = MenuBarDarkMode.GetWindowDC(Handle);
            if (dc == 0) return;
            try
            {
                using var g = Graphics.FromHdc(dc);
                g.FillRectangle(_menuHighlightBrush, stripLeft, clientTop - 1, stripWidth, 1);
            }
            finally
            {
                MenuBarDarkMode.ReleaseDC(Handle, dc);
            }
        }
        catch (ArgumentException) { }
    }

    private void DrawMenuBarBackground(ref Message m)
    {
        try
        {
            var um = Marshal.PtrToStructure<MenuBarDarkMode.UahMenu>(m.LParam);
            if (um.Hdc == 0) return;

            var mbi = new MenuBarDarkMode.MenuBarInfo { CbSize = Marshal.SizeOf<MenuBarDarkMode.MenuBarInfo>() };
            if (!MenuBarDarkMode.GetMenuBarInfo(Handle, MenuBarDarkMode.ObjidMenuId, 0, ref mbi))
                return;

            MenuBarDarkMode.GetWindowRect(Handle, out var rcWindow);
            var rc = mbi.RcBar;
            rc.Left -= rcWindow.Left;
            rc.Top -= rcWindow.Top;
            rc.Right -= rcWindow.Left;
            rc.Bottom -= rcWindow.Top;

            using var g = Graphics.FromHdc(um.Hdc);
            g.FillRectangle(_menuBgBrush, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
            m.Result = 0;
        }
        catch (ArgumentException) { }
    }

    private void DrawDarkMenuItem(ref Message m)
    {
        try
        {
            var di = Marshal.PtrToStructure<MenuBarDarkMode.UahDrawMenuItem>(m.LParam);
            var rc = di.Dis.RcItem;
            var state = di.Dis.ItemState;

            var isSelected = (state & MenuBarDarkMode.OdsSelected) != 0;
            var isHot = (state & MenuBarDarkMode.OdsHotLight) != 0;
            var isDisabled = (state & (MenuBarDarkMode.OdsGrayed | MenuBarDarkMode.OdsDisabled)) != 0;

            using var g = Graphics.FromHdc(di.Um.Hdc);

            g.FillRectangle(
                isSelected || isHot ? _menuHighlightBrush : _menuBgBrush,
                rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);

            var itemText = GetMenuItemText(di.Um.HMenu, di.Umi.IPosition);
            if (string.IsNullOrEmpty(itemText))
            {
                m.Result = 0;
                return;
            }

            var color = isDisabled ? ((SolidBrush)_menuDisabledBrush).Color
                                   : ((SolidBrush)_menuTextBrush).Color;
            var textRect = new Rectangle(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);

            TextRenderer.DrawText(g, itemText, SystemFonts.MenuFont!, textRect, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine);
            m.Result = 0;
        }
        catch (ArgumentException) { }
    }

    private static string GetMenuItemText(nint hMenu, int position)
    {
        if (hMenu == 0) return "";

        var info = new MenuItemInfoW();
        info.cbSize = (uint)Marshal.SizeOf<MenuItemInfoW>();
        info.fMask = MiiString;
        info.dwTypeData = nint.Zero;
        info.cch = 0;

        // First call to get the length
        if (!GetMenuItemInfoW(hMenu, (uint)position, true, ref info))
            return "";

        info.cch += 1; // +1 for null terminator
        info.dwTypeData = Marshal.AllocHGlobal((int)(info.cch * 2));

        try
        {
            if (!GetMenuItemInfoW(hMenu, (uint)position, true, ref info))
                return "";

            return Marshal.PtrToStringUni(info.dwTypeData, (int)info.cch) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(info.dwTypeData);
        }
    }

    private const uint MiiString = 0x00000040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MenuItemInfoW
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public nint hSubMenu;
        public nint hbmpChecked;
        public nint hbmpUnchecked;
        public nint dwItemData;
        public nint dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMenuItemInfoW(nint hMenu, uint uItem, bool fByPosition, ref MenuItemInfoW lpmii);

    private void ApplySidebarColors()
    {
        var colors = ColorThemeService.GetActiveColors();
        if (colors.Count == 0) return;

        if (colors.TryGetValue("bg-primary", out var bg))
        {
            _sidebarPanel.BackColor = bg;
            _workspacePanelHost.BackColor = bg;
            _outlinePanelHost.BackColor = bg;
            _sidebarSplit.Panel2.BackColor = bg;
            _editorPanel.BackColor = bg;
            _workspaceContentPanel.BackColor = bg;
            _editorLoadingView.BackColor = bg;
        }
        if (colors.TryGetValue("bg-hover", out var splitter))
            _sidebarSplit.BackColor = splitter;

        if (_statusStrip is not null)
        {
            if (colors.TryGetValue("bg-hover", out var statusBg))
                _statusStrip.BackColor = statusBg;
            if (colors.TryGetValue("text-primary", out var statusText))
            {
                _statusStrip.ForeColor = statusText;
                foreach (ToolStripItem item in _statusStrip.Items)
                    item.ForeColor = statusText;
            }
        }

        _sidebarTabBar.ApplyThemeColors(colors);
        _openFolderPrompt.ApplyThemeColors(colors);
        _workspaceTree.ApplyThemeColors(colors);
        _workspaceDocumentList.ApplyThemeColors(colors);
        _outlineTree.ApplyThemeColors(colors);

        if (colors.TryGetValue("bg-primary", out var menuBg))
        {
            _menuBgBrush.Dispose();
            _menuBgBrush = new SolidBrush(menuBg);
        }
        if (colors.TryGetValue("bg-hover", out var menuHl))
        {
            _menuHighlightBrush.Dispose();
            _menuHighlightBrush = new SolidBrush(menuHl);
        }
        if (colors.TryGetValue("text-primary", out var menuText))
        {
            _menuTextBrush.Dispose();
            _menuTextBrush = new SolidBrush(menuText);
        }
        if (colors.TryGetValue("text-tertiary", out var menuDisabled))
        {
            _menuDisabledBrush.Dispose();
            _menuDisabledBrush = new SolidBrush(menuDisabled);
        }
        _menuDarkMode = _settings.Appearance.MenuBarStyle switch
        {
            Services.Settings.MenuBarStyle.Always => true,
            Services.Settings.MenuBarStyle.System => false,
            _ => ColorThemeService.IsActiveThemeDark(),
        };
    }

    private void AddThemeFromFile()
    {
        var directory = StyleService.StylesDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ShowMessage(this, Loc.Get("error.themeFolderNotFound"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = Loc.Get("dialog.selectThemeCss"),
            Filter = Loc.Get("fileFilter.css"),
            DefaultExt = "css",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var sourceFile = dialog.FileName;
        if (!File.Exists(sourceFile))
            return;

        var destFile = Path.Combine(directory, Path.GetFileName(sourceFile));
        if (File.Exists(destFile))
        {
            var choice = MessageBox.Show(
                this,
                Loc.Format("dialog.themeFileExists", Path.GetFileName(destFile)),
                Loc.Get("dialog.fileExistsTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes)
                return;
        }

        try
        {
            File.Copy(sourceFile, destFile, overwrite: true);
            RefreshColorThemes();
            _logger.Info($"Theme file added: {Path.GetFileName(destFile)}");
        }
        catch (Exception exception)
        {
            ShowMessage(this, Loc.Format("error.copyThemeFailed", exception.Message),
                "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshColorThemes()
    {
        var directory = StyleService.StylesDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;

        ColorThemeService.Initialize(directory);
        _menuService.RefreshStates();
        ApplySidebarColors();
    }

    private void OpenThemeFolder()
    {
        var directory = StyleService.StylesDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            ShowMessage(this, Loc.Get("error.themeFolderNotFound"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        OpenFolderInExplorer(directory, Loc.Get("folder.themes"));
    }

    private void ApplyFileAssociations()
    {
        var enabled = GetEnabledExtensions();
        try
        {
            FileAssociationService.ApplyFileAssociations(Application.ExecutablePath, enabled);
            SetStatus(enabled.Count > 0
                ? Loc.Format("status.fileAssociationAdded", string.Join("、", enabled))
                : Loc.Get("status.fileAssociationRemoved"));
        }
        catch (Exception exception) when (FileAssociationService.IsExpectedRegistryException(exception))
        {
            _logger.Error("Failed to update file association.", exception);
            ShowMessage(this, Loc.Get("error.fileAssociationFailed") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string DefaultNewLine =>
        _settings.File.NewLineStyle == NewLineStyle.Lf ? "\n" : "\r\n";

    private IReadOnlySet<string> GetEnabledExtensions()
    {
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_settings.General.AssociateMarkdownFiles)
        {
            enabled.Add(".md");
            enabled.Add(".markdown");
        }
        if (_settings.General.AssociateTextFiles)
        {
            enabled.Add(".txt");
        }

        return enabled;
    }

    private void OpenCacheFolder()
    {
        var directory = Path.Combine(_paths.DataDirectory, "Cache");
        Directory.CreateDirectory(directory);
        OpenFolderInExplorer(directory, Loc.Get("folder.cache"));
    }

    private void OpenLogFolder()
    {
        OpenFolderInExplorer(_paths.LogDirectory, Loc.Get("folder.logs"));
    }

    private void OpenSettingsJson()
    {
        var file = _paths.SettingsFile;
        if (!File.Exists(file))
        {
            ShowMessage(this, Loc.Get("dialog.settingsFileNotCreated"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error($"Failed to open settings file: {file}.", exception);
            ShowMessage(this, Loc.Get("error.cannotOpenSettingsFile") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearLogs()
    {
        if (!Directory.Exists(_paths.LogDirectory))
        {
            return;
        }

        var deleted = 0;
        foreach (var file in Directory.GetFiles(_paths.LogDirectory, "*.log"))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Could not delete log file: {file}. {exception.Message}");
            }
        }

        SetStatus(deleted > 0 ? Loc.Format("status.logsCleared", deleted) : Loc.Get("status.noLogsToClear"));
    }

    private void OpenFolderInExplorer(string directory, string displayName)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add(directory);
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error($"Failed to open {displayName}: {directory}.", exception);
            ShowMessage(this, Loc.Format("error.openFolderFailed", displayName) + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 显示模态对话框/消息框期间临时关闭窗口置顶，避免 TopMost 主窗口覆盖其
    /// 自身弹出的二级窗口；对话框关闭后恢复置顶。
    /// </summary>
    private T ShowModal<T>(Func<T> show)
    {
        var wasTopMost = TopMost;
        if (wasTopMost)
        {
            TopMost = false;
        }

        try
        {
            return show();
        }
        finally
        {
            if (wasTopMost)
            {
                TopMost = true;
            }
        }
    }

    private DialogResult ShowMessage(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        return ShowModal(() => MessageBox.Show(owner, text, caption, buttons, icon));
    }

    private DialogResult ShowMessage(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton)
    {
        return ShowModal(() => MessageBox.Show(owner, text, caption, buttons, icon, defaultButton));
    }

    private void SetZoomPercent(int percent)
    {
        var target = NearestZoom(percent);
        _zoomPercent = target;
        _settings.Appearance.ZoomPercent = target;
        _zoomLabel.Text = $"{target}%";
        _editorHost?.SetZoomPercent(target);
        _menuService.RefreshStates();
    }

    private static int NextZoom(int current, int delta)
    {
        var options = AppearanceSettings.ZoomPercentOptions;
        if (options.Length == 0)
        {
            return 100;
        }

        var index = Array.IndexOf(options, current);
        if (index < 0)
        {
            index = 0;
        }

        return options[Math.Clamp(index + delta, 0, options.Length - 1)];
    }

    private static int NearestZoom(int percent)
    {
        var options = AppearanceSettings.ZoomPercentOptions;
        if (options.Length == 0)
        {
            return 100;
        }

        var closest = options[0];
        foreach (var option in options)
        {
            if (Math.Abs(option - percent) < Math.Abs(closest - percent))
            {
                closest = option;
            }
        }

        return closest;
    }

    private void OpenDocumentInNewWindow()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = DocumentFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            Title = Loc.Get("dialog.openInNewWindow"),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) == DialogResult.OK)
        {
            StartNewWindow(dialog.FileName);
        }
    }

    private void StartNewWindow(string? documentPath = null)
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException(Loc.Get("dialog.cannotFindExecutable"));
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
            SetStatus(documentPath is null ? Loc.Get("status.newWindowOpened") : Loc.Get("status.documentOpenedInNewWindow"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not start a new MarkLeaf window.", exception);
            ShowMessage(this, Loc.Get("dialog.cannotOpenNewWindow") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowShortcutHelp()
    {
        using var dialog = new ShortcutDialog();
        ShowModal(() => dialog.ShowDialog(this));
    }

    private void ShowPreferences()
    {
        var previousAssociateMarkdown = _settings.General.AssociateMarkdownFiles;
        var previousAssociateText = _settings.General.AssociateTextFiles;

        using var dialog = new PreferencesDialog(
            _settings,
            RecoverUnsavedFiles,
            ShowShortcutHelp,
            OpenThemeFolder,
            AddThemeFromFile,
            OpenCacheFolder,
            OpenLogFolder,
            ClearLogs,
            OpenSettingsJson,
            ClearHistory,
            ResetAllSettingsToDefaults);
        var previousLanguage = _settings.General.UiLanguage ?? "";
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;

        var newLanguage = _settings.General.UiLanguage ?? "";
        if (!string.Equals(previousLanguage, newLanguage, StringComparison.Ordinal))
        {
            ShowMessage(this,
                Loc.Get("dialog.languageRestart"),
                "MarkLeaf", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        _recoveryTimer.Interval = Math.Clamp(_settings.File.SnapshotIntervalSeconds, 10, 300) * 1000;
        _recoveryTimer.Stop();
        _recoveryTimer.Start();

        var editor = _settings.Editor;
        _editorHost?.ApplyCssVariables(editor.VisualLineHeight, editor.VisualFontSize, editor.VisualMaxContentWidth, editor.SourceFontSize, editor.SourceFontFamily, editor.SourceCjkFontFamily);
        _editorHost?.ApplySourceSettings(editor.SourceIndentWidth);

        SetMarkdownStyle(_settings.MarkdownStyle);
        SetColorTheme(_settings.ColorTheme);
        SetZoomPercent(_settings.Appearance.ZoomPercent);
        TopMost = _settings.Appearance.TopMostWindow;
        _editorHost?.ApplyAutoHideScrollbar(_settings.Appearance.AutoHideScrollbars);
        ApplySidebarAutoHideScrollbar();

        // 仅在文件关联设置实际变化时才修改注册表。
        if (_settings.General.AssociateMarkdownFiles != previousAssociateMarkdown
            || _settings.General.AssociateTextFiles != previousAssociateText)
        {
            ApplyFileAssociations();
        }

        UpdateDocumentChrome();
    }

    private void ShowAbout()
    {
        using var dialog = new AboutDialog();
        ShowModal(() => dialog.ShowDialog(this));
    }

    private async void OnRecoveryTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_document is null || !_document.IsDirty || _editorHost?.IsDocumentLoaded != true) return;
        try
        {
            _logger.Info("Recovery timer: requesting snapshot...");
            var snapshot = await _editorHost.RequestSnapshotAsync();
            await _recoveryService.WriteSnapshotAsync(
                RecoverySnapshot.FromDocument(_document, snapshot.Markdown));
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Recovery timer: snapshot request timed out.");
        }
        catch (Exception exception)
        {
            _logger.Warning($"Recovery timer: {exception.Message}");
        }
    }

    private async void OnAutoSaveTimerTick(object? sender, EventArgs eventArgs)
    {
        _autoSaveTimer.Stop();
        if (_document is null
            || !_document.IsDirty
            || _editorHost?.IsDocumentLoaded != true
            || _document.FilePath is null
            || _documentOperationInProgress) return;

        _logger.Info("Auto-save timer: saving...");
        await SaveDocumentAsync(saveAs: false);
        UpdateDocumentChrome();
    }

    private void RecoverUnsavedFiles()
    {
        var pending = RecoveryService.GetPendingRecoveries(_paths.RecoveryDirectory, _logger);
        if (pending.Count == 0)
        {
            ShowMessage(this, Loc.Get("dialog.noRecoverableFiles"), "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new RecoveryDialog(pending);
        ShowModal(() => dialog.ShowDialog(this));

        switch (dialog.Choice)
        {
            case RecoveryChoice.Restore when dialog.SelectedSnapshot is not null:
                SaveAndOpenRecovery(dialog.SelectedSnapshot);
                break;
            case RecoveryChoice.Discard:
                foreach (var snapshot in pending)
                {
                    foreach (var file in Directory.GetFiles(
                        _paths.RecoveryDirectory,
                        $"doc-*-{snapshot.DocumentId:N}.*"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                break;
        }
    }

    private async void SaveAndOpenRecovery(RecoverySnapshot recovery)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = Loc.Get("fileFilter.markdown"),
            AddExtension = true,
            DefaultExt = "md",
            RestoreDirectory = true,
            OverwritePrompt = true,
            Title = Loc.Get("dialog.saveRecovery"),
            FileName = recovery.DocumentPath is not null
                ? Path.GetFileName(recovery.DocumentPath)
                : (recovery.DisplayName ?? Loc.Get("document.untitledMd")),
        };
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK) return;

        _documentOperationInProgress = true;
        try
        {
            var targetPath = dialog.FileName;
            await File.WriteAllTextAsync(targetPath, recovery.Markdown, System.Text.Encoding.UTF8);

            foreach (var file in Directory.GetFiles(
                _paths.RecoveryDirectory,
                $"doc-*-{recovery.DocumentId:N}.*"))
            {
                try { File.Delete(file); } catch { }
            }

            StopWatchingDocument();
            var opened = await _documentFileService.OpenAsync(targetPath);
            _document = opened;
            _workspaceTree.SelectedPath = opened.FilePath;
            _workspaceDocumentList.SelectedPath = opened.FilePath;
            LoadDocumentIntoEditor(opened);
            StartWatchingDocument(opened.FilePath!);
            _logger.Info($"Recovery snapshot saved and opened: {targetPath}.");
            SetStatus(Loc.Get("status.recoveredUnsaved"));
        }
        catch (Exception exception)
        {
            _logger.Error("Failed to save recovery snapshot.", exception);
            ShowMessage(this,
                Loc.Get("dialog.saveRecoveryFailed") + "\r\n\r\n" + exception.Message,
                "MarkLeaf",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _documentOperationInProgress = false;
        }
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
            EditorLifecycleState.Initializing => Loc.Get("editor.initializing"),
            EditorLifecycleState.LoadingPage => Loc.Get("editor.loadingPage"),
            EditorLifecycleState.WaitingForEditorReady => Loc.Get("editor.waitingForEditorReady"),
            EditorLifecycleState.Ready => Loc.Get("editor.ready"),
            EditorLifecycleState.Failed => Loc.Get("editor.failed"),
            _ => Loc.Get("statusBar.preparing"),
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

        _document ??= _documentFileService.CreateNew(DefaultNewLine);
        if (!string.IsNullOrWhiteSpace(_options.EditorCommandSmoke))
        {
            _document.Markdown = "段落命令";
        }
        LoadDocumentIntoEditor(_document);
    }

    private void ShowExportCompleteDialog(string fileName, string filePath, string folderPath)
    {
        var openButton = new TaskDialogButton(Loc.Get("export.open"));
        openButton.Click += (_, _) => ExternalLinkService.OpenLocal(filePath);

        var openFolderButton = new TaskDialogButton(Loc.Get("export.openFolder"));
        openFolderButton.Click += (_, _) => ExternalLinkService.OpenLocal(folderPath);

        var page = new TaskDialogPage
        {
            Caption = "MarkLeaf",
            Icon = TaskDialogIcon.Information,
            Heading = Loc.Get("export.complete"),
            Text = Loc.Format("status.exportCompleteWithPath", fileName, filePath),
            Buttons = { openButton, openFolderButton, TaskDialogButton.Close },
        };

        ShowModal(() => TaskDialog.ShowDialog(this, page));
    }

    private async Task ExportDocumentAsync()
    {
        if (_editorHost?.IsDocumentLoaded != true || _document is null)
        {
            return;
        }

        var docName = _document.FilePath is not null
            ? Path.GetFileName(_document.FilePath)
            : Loc.Get("common.unnamed");
        var defaultName = _document.FilePath is not null
            ? Path.GetFileNameWithoutExtension(_document.FilePath)
            : Loc.Get("common.unnamed");
        using var dialog = new ExportDialog(docName, defaultName, _markdownStyle, StyleService.GetAllStyles());
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        var options = dialog.Options;
        if (options is null || string.IsNullOrWhiteSpace(options.OutputPath))
        {
            SetStatus(Loc.Get("export.emptyPath"));
            return;
        }

        var exportDir = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(exportDir) && !Directory.Exists(exportDir))
        {
            Directory.CreateDirectory(exportDir);
        }

        try
        {
            SetStatus(Loc.Get("export.generating"));
            var editor = _settings.Editor;
            var colorThemeCss = ColorThemeService.GetThemeCss(options.ColorScheme);
            var html = await _editorHost.RequestExportAsync(
                options.Format,
                options.Style,
                options.HtmlHeader,
                options.HtmlFooter,
                editor.VisualFontSize,
                editor.VisualLineHeight,
                editor.VisualMaxContentWidth,
                colorThemeCss);

            if (string.IsNullOrEmpty(html))
            {
                SetStatus(Loc.Get("export.noContent"));
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
                SetStatus(Loc.Get("export.generatingPdf"));
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

            SetStatus(Loc.Get("export.complete"));
            _logger.Info($"Document exported: {options.Format}/{options.Style} → {outputPath}");

            var exportedName = Path.GetFileName(outputPath);
            ShowExportCompleteDialog(exportedName, outputPath, exportDir!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Export failed: {options.OutputPath}.", exception);
            ShowMessage(this, Loc.Get("export.failed") + "\r\n\r\n" + exception.Message, "MarkLeaf",
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
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        _editorHost.ExecuteCommand("setLink", dialog.LinkAddress);
        SetStatus(Loc.Get("status.linkInserted"));
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
        _modeLabel.Text = _editorCommandStatus.SourceMode
            ? Loc.Get("statusBar.mode.source")
            : Loc.Get("statusBar.mode.visual");
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
        _settings.Workspace.LastFile = _document?.FilePath;
        _settings.Workspace.RecentFolders = _settings.Workspace.RecentFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.Workspace.RecentFiles = _settings.Workspace.RecentFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.MarkdownStyle = _markdownStyle;
        _settings.ColorTheme = _colorTheme;
        _settings.Appearance.ZoomPercent = _zoomPercent;

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

internal sealed class SolidStatusBarRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
}
