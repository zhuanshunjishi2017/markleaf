using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using MarkLeaf.Workspace;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private SearchResultsView _searchResultsView = default!;
    private Panel _searchResultsHost = default!;
    private CancellationTokenSource? _searchCancellation;

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
        _sidebarTabBar.SearchTextChanged += OnSidebarSearchTextChanged;
        _sidebarTabBar.SearchModeChanged += OnSidebarSearchModeChanged;
        panel.Controls.Add(_sidebarTabBar);

        _searchResultsView = new SearchResultsView();
        _searchResultsView.ResultActivated += OnSearchResultActivated;
        _searchResultsHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Visible = false,
        };
        _searchResultsHost.Controls.Add(_searchResultsView);
        panel.Controls.Add(_searchResultsHost);

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

        ShowSidebarView(outline: _settings.MainWindow.SidebarActiveOutline);
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
        _workspaceTree.NodeMoved += (_, args) =>
            _ = MoveWorkspaceEntryAsync(args.SourcePath, args.TargetDirectory);
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
            MinimumSize = new Size(0, this.ScaleForDpi(26)),
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
        _modeButton.Click += (_, _) => _editorHost?.ExecuteCommand("toggleSourceMode");
        strip.Items.Add(_modeButton);
        _zoomLabel.Text = $"{_zoomPercent}%";
        strip.Items.Add(_zoomLabel);
        return strip;
    }

    private void ApplySidebarAutoHideScrollbar()
    {
        var enabled = _settings.Appearance.AutoHideScrollbars;
        _workspaceTree.AutoHideScrollbar = enabled;
        _workspaceDocumentList.AutoHideScrollbar = enabled;
        _outlineTree.AutoHideScrollbar = enabled;
        _searchResultsView.AutoHideScrollbar = enabled;
    }

    private void OnSidebarCollapseClicked(object? sender, EventArgs e)
    {
        ToggleSidebarWithWindowResize();
    }

    private async void OnSearchResultActivated(object? sender, string path)
    {
        _sidebarTabBar.ExitSearchMode();
        _searchCancellation?.Cancel();
        _searchResultsHost.Visible = false;
        await ActivateWorkspaceDocumentAsync(path);
        await RevealPathInTreeAsync(path);
    }

    private async void OnSidebarSearchTextChanged(object? sender, string text)
    {
        if (_sidebarActiveOutline)
        {
            ApplyOutlineSearch(text);
            return;
        }

        if (string.IsNullOrWhiteSpace(text) || _workspaceRoot is null)
        {
            _searchCancellation?.Cancel();
            _searchResultsHost.Visible = false;
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        var query = text.Trim();

        try
        {
            var results = await _workspaceService.SearchAsync(_workspaceRoot, query, token);
            if (token.IsCancellationRequested)
            {
                return;
            }
            _searchResultsView.SetResults(results);
            _searchResultsHost.Visible = true;
            _searchResultsHost.BringToFront();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Workspace search failed: {exception.GetType().Name}.");
        }
    }

    private void OnSidebarSearchModeChanged(object? sender, bool active)
    {
        if (active)
        {
            return;
        }

        if (_sidebarActiveOutline)
        {
            ExitOutlineSearch();
        }
        else
        {
            _searchCancellation?.Cancel();
            _searchResultsHost.Visible = false;
        }
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
        _sidebarTabBar.ExitSearchMode();
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

        // 切换到大纲时退出搜索并隐藏结果容器
        if (outline)
        {
            _searchCancellation?.Cancel();
            _searchResultsHost.Visible = false;
        }

        if (_workspaceRoot is null)
            _openFolderPrompt.BringToFront();

        _menuService.RefreshStates();
    }
}
