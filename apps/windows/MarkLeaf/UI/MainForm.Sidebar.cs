using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
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
        _sidebarTabBar.OpenFolderClicked += OnSidebarOpenFolderClicked;
        _sidebarSearchBar.SearchTextChanged += OnSidebarSearchTextChanged;

        _searchResultsView = new SearchResultsView();
        _searchResultsView.ResultActivated += OnSearchResultActivated;
        _searchResultsHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Visible = false,
        };
        _searchResultsHost.Controls.Add(_searchResultsView);

        _workspacePanelHost = CreateWorkspacePanel();
        _workspacePanelHost.Dock = DockStyle.Fill;
        _outlinePanelHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
        };
        _outlinePanelHost.Controls.Add(CreateOutlineTree());

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.White,
        };
        _sidebarContentHost = contentHost;
        contentHost.Controls.Add(_searchResultsHost);
        contentHost.Controls.Add(_outlinePanelHost);
        contentHost.Controls.Add(_workspacePanelHost);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
        };
        _sidebarLayout = layout;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, _sidebarTabBar.Height));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, _sidebarSearchBar.Height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.Controls.Add(_sidebarTabBar, 0, 0);
        layout.Controls.Add(_sidebarSearchBar, 0, 1);
        layout.Controls.Add(contentHost, 0, 2);
        panel.Controls.Add(layout);

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
            _viewToggleButton.Text = SystemIconProvider.CollapseSidebarIcon;
            _viewToggleButton.ToolTipText = Loc.Get("tooltip.collapseSidebar");
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
        _viewToggleButton.Click += (_, _) => ToggleSidebarWithWindowResize();
        UpdateViewToggleIcon();
        strip.Items.Add(_viewToggleButton);
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        strip.Items.Add(_statusLabel);
        _characterCountButton.Click += (_, _) => ShowDocumentStatisticsDialog();
        strip.Items.Add(_characterCountButton);
        strip.Items.Add(_blockTypeLabel);
        strip.Items.Add(_positionLabel);
        strip.Items.Add(_encodingLabel);
        strip.Items.Add(_newLineLabel);
        _modeButton.Click += (_, _) => _editorHost?.ExecuteCommand("toggleSourceMode");
        strip.Items.Add(_modeButton);
        _zoomLabel.Text = $"{_zoomPercent}%";
        strip.Items.Add(_zoomLabel);
        ApplyStatusBarTextStyle();
        ApplyStatusBarItemVisibility();
        return strip;
    }

    private void ApplyStatusBarTextStyle()
    {
        foreach (ToolStripItem item in new ToolStripItem[]
        {
            _statusLabel,
            _characterCountButton,
            _blockTypeLabel,
            _positionLabel,
            _encodingLabel,
            _newLineLabel,
            _modeButton,
            _zoomLabel,
        })
        {
            item.Font = _statusBarTextFont;
        }
    }

    private void ApplyStatusBarItemVisibility()
    {
        var statusBar = _settings.Appearance.StatusBar;
        _viewToggleButton.Visible = statusBar.SidebarToggleVisible;
        _statusLabel.Visible = true;
        if (statusBar.CommandDisplayMode == StatusBarCommandDisplayMode.Hidden)
        {
            _statusMessageTimer.Stop();
            _statusLabel.Text = string.Empty;
        }
        else if (statusBar.CommandDisplayMode == StatusBarCommandDisplayMode.Temporary
            && !string.IsNullOrEmpty(_statusLabel.Text))
        {
            _statusMessageTimer.Stop();
            _statusMessageTimer.Start();
        }
        else
        {
            _statusMessageTimer.Stop();
        }
        _characterCountButton.Visible = statusBar.WordCountVisible;
        _blockTypeLabel.Visible = statusBar.BlockTypeVisible;
        _positionLabel.Visible = statusBar.PositionVisible;
        _encodingLabel.Visible = statusBar.EncodingVisible;
        _newLineLabel.Visible = statusBar.NewLineVisible;
        _modeButton.Visible = statusBar.ModeToggleVisible;
        _zoomLabel.Visible = statusBar.ZoomVisible;
    }

    private void ApplySidebarAutoHideScrollbar()
    {
        var enabled = _settings.Appearance.AutoHideScrollbars;
        _workspaceTree.AutoHideScrollbar = enabled;
        _workspaceDocumentList.AutoHideScrollbar = enabled;
        _outlineTree.AutoHideScrollbar = enabled;
        _searchResultsView.AutoHideScrollbar = enabled;
    }

    private void OnSidebarOpenFolderClicked(object? sender, EventArgs e)
    {
        _ = SelectWorkspaceFolderAsync();
    }

    private async void OnSearchResultActivated(object? sender, string path)
    {
        _sidebarSearchBar.ClearSearch();
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
        _sidebarSearchBar.ClearSearch();
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
        _sidebarSearchBar.OutlineMode = outline;
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

        // 切换到大纲时隐藏全文搜索结果；常驻搜索框会按当前标签重新过滤。
        if (outline)
        {
            _searchCancellation?.Cancel();
            _searchResultsHost.Visible = false;
        }
        if (!string.IsNullOrWhiteSpace(_sidebarSearchBar.SearchText))
        {
            OnSidebarSearchTextChanged(_sidebarSearchBar, _sidebarSearchBar.SearchText);
        }

        if (_workspaceRoot is null)
            _openFolderPrompt.BringToFront();

        UpdateSidebarSearchEnabled();
        _menuService.RefreshStates();
    }

    /// <summary>
    /// 与 macOS 一致：无工作区且在工作区标签时禁用搜索框（大纲搜索不依赖工作区）。
    /// </summary>
    private void UpdateSidebarSearchEnabled()
    {
        _sidebarSearchBar.Enabled = _sidebarActiveOutline || _workspaceRoot is not null;
    }

    private void UpdateSidebarHeaderRowHeights()
    {
        if (_sidebarLayout.RowStyles.Count < 2) return;
        _sidebarLayout.RowStyles[0].Height = _sidebarTabBar.Height;
        _sidebarLayout.RowStyles[1].Height = _sidebarSearchBar.Height;
        _sidebarLayout.PerformLayout();
    }
}
