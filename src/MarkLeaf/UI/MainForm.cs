using System.Text.Json;
using MarkLeaf.App;
using MarkLeaf.Commands;
using MarkLeaf.Documents;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Logging;
using MarkLeaf.Services.Recovery;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
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
        ColorThemeService.DefaultLightThemeId = settings.Appearance.DefaultLightThemeId;
        ColorThemeService.DefaultDarkThemeId = settings.Appearance.DefaultDarkThemeId;
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
        _viewToggleButton.Width = this.ScaleForDpi(18);
        _viewToggleButton.Margin = new Padding(this.ScaleForDpi(1), 0, this.ScaleForDpi(2), 0);
        _commandRouter = new CommandRouter(GetCommandState, ExecuteCommand);
        _menuService = new NativeMenuService(_commandRouter, GetRecentWorkspaces, GetRecentFiles, () => _markdownStyle, () => _zoomPercent, () => _colorTheme, () => _settings.Appearance.FollowSystemColorMode);
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
                if (!_settings.Appearance.FollowSystemColorMode)
                {
                    SetColorTheme(colorThemeId);
                }
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

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
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
}

internal sealed class SolidStatusBarRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }
}
