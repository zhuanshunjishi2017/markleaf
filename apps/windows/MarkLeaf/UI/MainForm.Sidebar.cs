using MarkLeaf.Documents;
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
    private string? _pendingWorkspaceSearchQuery;

    private LiveSplitContainer CreateSidebarSplit(int sidebarWidth, int outlineWidth)
    {
        _sidebarExpandedWidth = sidebarWidth;
        var split = new LiveSplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1280, 740),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 1,
            Panel1MinSize = 160,
            Panel2MinSize = 500,
        };
        _sidebarMinimumWidth = split.Panel1MinSize;
        split.Panel1.Controls.Add(CreateSidebarPanel());
        split.Panel2.Controls.Add(CreateOutlineSplit(outlineWidth));
        split.HandleCreated += (_, _) => SetSplitterDistanceSafely(split, sidebarWidth, FixedPanel.Panel1);
        split.LiveSplitterMoved += OnSidebarSplitterMoved;
        split.LiveSplitterDragCompleted += OnSidebarSplitterMoved;
        return split;
    }

    private LiveSplitContainer CreateOutlineSplit(int outlineWidth)
    {
        _detachedOutlineWidth = Math.Max(outlineWidth, this.ScaleForDpi(160));
        var split = new LiveSplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1000, 740),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 1,
            Panel1MinSize = 500,
            Panel2MinSize = this.ScaleForDpi(160),
        };
        _detachedOutlineMinimumWidth = split.Panel2MinSize;
        split.Panel1.Controls.Add(_editorPanel);
        split.Panel2.Controls.Add(CreateDetachedOutlinePanel());
        split.Panel2.Resize += (_, _) =>
        {
            if (_outlineDetached && _outlineAnimationTargetDetached is null)
            {
                PositionDetachedOutline();
            }
        };
        split.HandleCreated += (_, _) => SetSplitterDistanceSafely(
            split,
            _detachedOutlineWidth,
            FixedPanel.Panel2);
        split.LiveSplitterMoved += OnOutlineSplitterMoved;
        split.LiveSplitterDragCompleted += OnOutlineSplitterMoved;
        split.Panel2Collapsed = true;
        return _outlineSplit = split;
    }

    private Control CreateDetachedOutlinePanel()
    {
        _detachedOutlineTabBar.Mode = SidebarTabBarMode.OutlineOnly;
        _detachedOutlineTabBar.MergeClicked += (_, _) => MergeOutlineSidebar();
        _detachedOutlineSearchBar.OutlineMode = true;
        _detachedOutlineSearchBar.SearchTextChanged += (_, text) => ApplyOutlineSearch(text);

        _detachedOutlineContentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        _detachedOutlineLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
        };
        _detachedOutlineLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _detachedOutlineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _detachedOutlineTabBar.Height));
        _detachedOutlineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, _detachedOutlineSearchBar.Height));
        _detachedOutlineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _detachedOutlineLayout.Controls.Add(_detachedOutlineTabBar, 0, 0);
        _detachedOutlineLayout.Controls.Add(_detachedOutlineSearchBar, 0, 1);
        _detachedOutlineLayout.Controls.Add(_detachedOutlineContentHost, 0, 2);

        _detachedOutlinePanel = new Panel { Dock = DockStyle.Fill };
        _detachedOutlinePanel.Controls.Add(_detachedOutlineLayout);
        return _detachedOutlinePanel;
    }

    private void OnOutlineSplitterMoved(object? sender, EventArgs eventArgs)
    {
        if (sender is SplitContainer split && !split.Panel2Collapsed)
        {
            _detachedOutlineWidth = Math.Max(
                _detachedOutlineMinimumWidth,
                split.ClientSize.Width - split.SplitterDistance - split.SplitterWidth);
            PositionDetachedOutline();
        }
    }

    private void OnSidebarSplitterMoved(object? sender, EventArgs eventArgs)
    {
        if (sender is SplitContainer split && !split.Panel1Collapsed)
        {
            _sidebarExpandedWidth = split.SplitterDistance;
        }
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
            if (index == 0)
                ToggleWorkspaceView();
            else if (index == 1)
                DetachOutlineSidebar();
        };
        _sidebarTabBar.NewMarkdownClicked += OnSidebarNewMarkdownClicked;
        _sidebarTabBar.DetachClicked += (_, _) => DetachOutlineSidebar();
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
        _workspaceTree.RenameRequested += OnWorkspaceRenameRequested;
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
        _workspaceDocumentList.RenameRequested += OnWorkspaceRenameRequested;
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
        _outlineTree.ContextMenuRequested += (_, screenPoint) => ShowOutlineContextMenu(screenPoint);
        return _outlineTree;
    }

    private void UpdateViewToggleIcon()
    {
        var collapsed = _sidebarAnimationTargetCollapsed ?? _sidebarSplit.Panel1Collapsed;
        if (collapsed)
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
            MinimumSize = new Size(0, this.ScaleForDpi(26) + 5),
            Renderer = new SolidStatusBarRenderer(),
        };
        _viewToggleButton.Click += (_, _) => ToggleSidebarWithWindowResize();
        UpdateViewToggleIcon();
        strip.Items.Add(_viewToggleButton);
        _modeButton.Click += (_, _) => _editorHost?.ExecuteCommand("toggleSourceMode");
        strip.Items.Add(_modeButton);
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        strip.Items.Add(_statusLabel);
        _characterCountButton.Click += (_, _) => ShowDocumentStatisticsDialog();
        strip.Items.Add(_characterCountButton);
        strip.Items.Add(_blockTypeLabel);
        strip.Items.Add(_positionLabel);
        _encodingLabel.Click += (_, _) => ShowEncodingMenu();
        strip.Items.Add(_encodingLabel);
        _newLineLabel.Click += (_, _) => ShowNewLineMenu();
        strip.Items.Add(_newLineLabel);
        _zoomLabel.Text = $"{_zoomPercent}%";
        _zoomLabel.Click += (_, _) => ShowZoomMenu();
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

    private uint ShowStatusBarPopupMenu(nint menu, ToolStripItem item)
    {
        var owner = item.GetCurrentParent();
        if (owner is null)
        {
            return 0;
        }

        var bounds = item.Bounds;
        var screenPoint = owner.PointToScreen(new Point(bounds.Left, bounds.Bottom));
        NativeMethods.SetForegroundWindow(Handle);
        var selected = NativeMethods.TrackPopupMenuEx(
            menu,
            NativeMethods.TpmLeftButton | NativeMethods.TpmReturnCommand,
            screenPoint.X,
            screenPoint.Y,
            Handle,
            0);
        NativeMethods.PostMessage(Handle, NativeMethods.WmNull, 0, 0);
        return selected;
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

    private void OnSidebarNewMarkdownClicked(object? sender, EventArgs e)
    {
        _ = _workspaceRoot is null
            ? NewDocumentAsync(NewDocumentKind.Markdown)
            : CreateUntitledWorkspaceDocumentAsync(
                GetNewWorkspaceDocumentDirectory(),
                NewDocumentKind.Markdown);
    }

    private async void OnSearchResultActivated(object? sender, SearchResult result)
    {
        var wasCurrentDocument = PathEquals(_document?.FilePath, result.FullPath);
        _sidebarSearchBar.ClearSearch();
        _searchCancellation?.Cancel();
        _searchResultsHost.Visible = false;
        _pendingWorkspaceSearchQuery = result.IsContentMatch && !wasCurrentDocument
            ? result.Query
            : null;
        await ActivateWorkspaceDocumentAsync(result.FullPath);
        if (!PathEquals(_document?.FilePath, result.FullPath))
        {
            _pendingWorkspaceSearchQuery = null;
            return;
        }
        await RevealPathInTreeAsync(result.FullPath);
        if (result.IsContentMatch && wasCurrentDocument)
        {
            OpenFindReplaceDialog(replace: false, result.Query);
        }
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
        if (_outlineAnimationTargetDetached is not null) return;

        var collapsed = _sidebarAnimationTargetCollapsed ?? _sidebarSplit.Panel1Collapsed;
        if (collapsed)
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
        if (_sidebarAnimationTargetCollapsed == true
            || (_sidebarAnimationTargetCollapsed is null && _sidebarSplit.Panel1Collapsed)) return;
        _sidebarSearchBar.ClearSearch();
        AnimateSidebar(collapsed: true);
        _settings.MainWindow.SidebarCollapsed = true;
        UpdateViewToggleIcon();
        if (_workspaceRoot is null)
            _openFolderPrompt.Visible = true;
        SetStatus(Loc.Get("status.sidebarCollapsed"));
    }

    private void ExpandSidebar()
    {
        if (_sidebarAnimationTargetCollapsed == false
            || (_sidebarAnimationTargetCollapsed is null && !_sidebarSplit.Panel1Collapsed)) return;
        AnimateSidebar(collapsed: false);
        _settings.MainWindow.SidebarCollapsed = false;
        UpdateViewToggleIcon();
        if (_workspaceRoot is null) ShowNoWorkspacePlaceholder();
        SetStatus(Loc.Get("status.sidebarExpanded"));
    }

    private void AnimateSidebar(bool collapsed)
    {
        _sidebarSplit.SetLiveDraggingEnabled(false);
        var wasCollapsed = _sidebarSplit.Panel1Collapsed;
        var startVisibleWidth = wasCollapsed ? 0 : _sidebarSplit.SplitterDistance;
        var editorWidth = _sidebarSplit.Panel2.ClientSize.Width;
        _sidebarAnimationPreservesEditorWidth = WindowState == FormWindowState.Normal;
        if (_sidebarAnimationPreservesEditorWidth)
        {
            _sidebarAnimationEditorBounds = _editorPanel.Bounds;
            _editorPanel.Dock = DockStyle.None;
            _editorPanel.Bounds = _sidebarAnimationEditorBounds;
        }
        if (_sidebarSplit.Panel1MinSize > 0)
        {
            _sidebarMinimumWidth = _sidebarSplit.Panel1MinSize;
        }

        if (!collapsed && wasCollapsed)
        {
            SuspendLayout();
            _sidebarSplit.SuspendLayout();
            try
            {
                ResizeWindowForSidebarExtent(_sidebarSplit.SplitterWidth);
                _sidebarSplit.Panel1MinSize = 0;
                _sidebarSplit.Panel1Collapsed = false;
                _sidebarSplit.SplitterDistance = _sidebarAnimationPreservesEditorWidth
                    ? Math.Max(
                        0,
                        _sidebarSplit.ClientSize.Width - editorWidth - _sidebarSplit.SplitterWidth)
                    : 0;
            }
            finally
            {
                _sidebarSplit.ResumeLayout(performLayout: true);
                ResumeLayout(performLayout: true);
            }
        }

        if (collapsed)
        {
            if (_sidebarAnimationTargetCollapsed is null)
            {
                _expandedWindowMinimumSize = MinimumSize;
            }
            _sidebarExpandedWidth = Math.Max(
                _sidebarExpandedWidth,
                Math.Max(startVisibleWidth, _sidebarMinimumWidth));
            var sidebarExpandedExtent = startVisibleWidth + _sidebarSplit.SplitterWidth;
            MinimumSize = new Size(
                Math.Max(1, _expandedWindowMinimumSize.Width - sidebarExpandedExtent),
                _expandedWindowMinimumSize.Height);
        }
        else if (_sidebarExpandedWidth <= 0)
        {
            _sidebarExpandedWidth = Math.Max(_settings.MainWindow.WorkspaceWidth, _sidebarMinimumWidth);
        }

        _sidebarAnimationTargetCollapsed = collapsed;
        _sidebarAnimationStartWidth = startVisibleWidth;
        _sidebarAnimationTargetWidth = collapsed
            ? 0
            : Math.Max(_sidebarExpandedWidth, _sidebarMinimumWidth);
        _sidebarAnimationEditorWidth = editorWidth;
        _sidebarSplit.Panel1MinSize = 0;
        _sidebarPanel.Dock = DockStyle.None;
        PositionAnimatedSidebar(startVisibleWidth);
        _sidebarAnimationStartBounds = Bounds;
        _sidebarAnimationTargetBounds = CalculateSidebarAnimationTargetBounds(
            _sidebarAnimationStartBounds,
            _sidebarAnimationTargetWidth - _sidebarAnimationStartWidth);
        _sidebarAnimationStartedAt = Environment.TickCount64;

        UpdateViewToggleIcon();
        _sidebarAnimationTimer.Stop();
        _sidebarAnimationTimer.Start();
    }

    private Rectangle CalculateSidebarAnimationTargetBounds(Rectangle startBounds, int sidebarWidthDelta)
    {
        if (WindowState != FormWindowState.Normal)
        {
            return startBounds;
        }

        var workingArea = Screen.FromControl(this).WorkingArea;
        var targetWidth = Math.Clamp(startBounds.Width + sidebarWidthDelta, MinimumSize.Width, workingArea.Width);
        var targetLeft = Math.Clamp(startBounds.Left, workingArea.Left, workingArea.Right - targetWidth);
        return new Rectangle(targetLeft, startBounds.Top, targetWidth, startBounds.Height);
    }

    private void OnSidebarAnimationTick(object? sender, EventArgs eventArgs)
    {
        const double durationMilliseconds = 180.0;
        var progress = Math.Clamp((Environment.TickCount64 - _sidebarAnimationStartedAt) / durationMilliseconds, 0.0, 1.0);
        var eased = progress < 0.5
            ? 4.0 * progress * progress * progress
            : 1.0 - Math.Pow(-2.0 * progress + 2.0, 3.0) / 2.0;

        var sidebarWidth = Interpolate(_sidebarAnimationStartWidth, _sidebarAnimationTargetWidth, eased);
        var bounds = new Rectangle(
            Interpolate(_sidebarAnimationStartBounds.Left, _sidebarAnimationTargetBounds.Left, eased),
            Interpolate(_sidebarAnimationStartBounds.Top, _sidebarAnimationTargetBounds.Top, eased),
            Interpolate(_sidebarAnimationStartBounds.Width, _sidebarAnimationTargetBounds.Width, eased),
            Interpolate(_sidebarAnimationStartBounds.Height, _sidebarAnimationTargetBounds.Height, eased));

        if (_sidebarAnimationPreservesEditorWidth)
        {
            SuspendLayout();
            try
            {
                if (WindowState == FormWindowState.Normal)
                {
                    Bounds = bounds;
                }
                _sidebarSplit.SplitterDistance = Math.Clamp(
                    _sidebarSplit.ClientSize.Width - _sidebarAnimationEditorWidth - _sidebarSplit.SplitterWidth,
                    0,
                    Math.Max(0, _sidebarSplit.Width - _sidebarSplit.Panel2MinSize - _sidebarSplit.SplitterWidth));
                PositionAnimatedSidebar(_sidebarSplit.SplitterDistance);
                _editorPanel.Bounds = _sidebarAnimationEditorBounds;
            }
            finally
            {
                ResumeLayout(performLayout: true);
            }
        }
        else
        {
            SuspendLayout();
            _sidebarSplit.SuspendLayout();
            _sidebarSplit.Panel2.SuspendLayout();
            try
            {
                _sidebarSplit.SplitterDistance = Math.Clamp(
                    sidebarWidth,
                    0,
                    Math.Max(0, _sidebarSplit.Width - _sidebarSplit.Panel2MinSize - _sidebarSplit.SplitterWidth));
                PositionAnimatedSidebar(_sidebarSplit.SplitterDistance);
            }
            finally
            {
                _sidebarSplit.Panel2.ResumeLayout(performLayout: true);
                _sidebarSplit.ResumeLayout(performLayout: true);
                ResumeLayout(performLayout: true);
            }
        }

        if (progress >= 1.0)
        {
            CompleteSidebarAnimation();
        }
    }

    private void CompleteSidebarAnimation()
    {
        _sidebarAnimationTimer.Stop();
        var collapsed = _sidebarAnimationTargetCollapsed == true;
        if (collapsed)
        {
            SuspendLayout();
            _sidebarSplit.SuspendLayout();
            try
            {
                _sidebarSplit.Panel1Collapsed = true;
                ResizeWindowForSidebarExtent(-_sidebarSplit.SplitterWidth);
                _sidebarPanel.Dock = DockStyle.Fill;
                _sidebarPanel.Location = Point.Empty;
            }
            finally
            {
                _sidebarSplit.ResumeLayout(performLayout: true);
                ResumeLayout(performLayout: true);
            }
        }
        else
        {
            _sidebarSplit.Panel1Collapsed = false;
            _sidebarSplit.SplitterDistance = _sidebarAnimationTargetWidth;
            _sidebarPanel.Dock = DockStyle.Fill;
            _sidebarPanel.Location = Point.Empty;
            MinimumSize = _expandedWindowMinimumSize;
        }
        _sidebarSplit.Panel1MinSize = _sidebarMinimumWidth;
        _sidebarAnimationTargetCollapsed = null;
        _sidebarSplit.PerformLayout();
        _editorPanel.Dock = DockStyle.Fill;
        _sidebarSplit.SetLiveDraggingEnabled(true);
        UpdateViewToggleIcon();
        if (!collapsed && _restoreDetachedOutlineAfterSidebarExpand)
        {
            _restoreDetachedOutlineAfterSidebarExpand = false;
            DetachOutlineSidebar();
        }
        if (collapsed)
        {
            SaveWindowState();
        }
    }

    private void ResizeWindowForSidebarExtent(int widthDelta)
    {
        if (widthDelta == 0 || WindowState != FormWindowState.Normal)
        {
            return;
        }

        var workingArea = Screen.FromControl(this).WorkingArea;
        var targetWidth = Math.Clamp(Width + widthDelta, MinimumSize.Width, workingArea.Width);
        var targetLeft = Math.Clamp(Left, workingArea.Left, workingArea.Right - targetWidth);
        Bounds = new Rectangle(targetLeft, Top, targetWidth, Height);
    }

    private void PositionAnimatedSidebar(int visibleWidth)
    {
        // Keep the sidebar at full width and slide it left through Panel1's clipping viewport.
        _sidebarPanel.Bounds = new Rectangle(
            visibleWidth - _sidebarExpandedWidth,
            0,
            _sidebarExpandedWidth,
            _sidebarSplit.Panel1.ClientSize.Height);
    }

    private static int Interpolate(int start, int end, double progress) =>
        (int)Math.Round(start + (end - start) * progress);

    private void DetachOutlineSidebar(bool resizeWindow = true)
    {
        if (_outlineDetached
            || _outlineAnimationTargetDetached is not null
            || _sidebarAnimationTargetCollapsed is not null) return;

        _sidebarSearchBar.ClearSearch();
        _detachedOutlineSearchBar.ClearSearch();
        if (resizeWindow)
        {
            _detachedOutlineWidth = Math.Max(
                _detachedOutlineMinimumWidth,
                _sidebarSplit.Panel1Collapsed
                    ? _sidebarExpandedWidth
                    : _sidebarSplit.SplitterDistance);
        }

        _outlineDetached = true;
        _sidebarTabBar.Mode = SidebarTabBarMode.WorkspaceOnly;
        ShowSidebarView(outline: false);
        _outlinePanelHost.Parent = _detachedOutlineContentHost;
        _outlinePanelHost.Dock = DockStyle.Fill;
        _outlinePanelHost.Visible = true;
        _outlinePanelHost.BringToFront();

        if (resizeWindow)
        {
            AnimateOutlineSidebar(detached: true);
            return;
        }

        SuspendLayout();
        _sidebarSplit.SuspendLayout();
        _outlineSplit.SuspendLayout();
        try
        {
            _outlineSplit.Panel2Collapsed = false;
            SetSplitterDistanceSafely(_outlineSplit, _detachedOutlineWidth, FixedPanel.Panel2);
            _detachedOutlinePanel.Dock = DockStyle.None;
            PositionDetachedOutline();
        }
        finally
        {
            _outlineSplit.ResumeLayout(performLayout: true);
            _sidebarSplit.ResumeLayout(performLayout: true);
            ResumeLayout(performLayout: true);
        }
    }

    private void MergeOutlineSidebar()
    {
        if (!_outlineDetached
            || _outlineAnimationTargetDetached is not null
            || _sidebarAnimationTargetCollapsed is not null) return;

        _detachedOutlineSearchBar.ClearSearch();
        AnimateOutlineSidebar(detached: false);
    }

    private void MergeOutlineSidebarImmediately()
    {
        if (!_outlineDetached)
            return;

        _outlineAnimationTimer.Stop();
        _outlineAnimationTargetDetached = null;
        var visibleWidth = _outlineSplit.Panel2Collapsed
            ? 0
            : Math.Max(
                0,
                _outlineSplit.ClientSize.Width
                    - _outlineSplit.SplitterDistance
                    - _outlineSplit.SplitterWidth);
        SuspendLayout();
        _outlineSplit.SuspendLayout();
        try
        {
            _outlineSplit.Panel2Collapsed = true;
            _outlineSplit.Panel2MinSize = _detachedOutlineMinimumWidth;
            _outlinePanelHost.Parent = _sidebarContentHost;
            _outlinePanelHost.Dock = DockStyle.Fill;
            _sidebarTabBar.Mode = SidebarTabBarMode.Combined;
            _outlineDetached = false;
            ShowSidebarView(outline: false);
            _outlineSplit.Dock = DockStyle.Fill;
            _detachedOutlinePanel.Dock = DockStyle.Fill;
            _detachedOutlinePanel.Location = Point.Empty;
            if (WindowState == FormWindowState.Normal && visibleWidth > 0)
            {
                ResizeWindowForSidebarExtent(-(visibleWidth + _outlineSplit.SplitterWidth));
            }
        }
        finally
        {
            _outlineSplit.ResumeLayout(performLayout: true);
            ResumeLayout(performLayout: true);
        }
    }

    private void AnimateOutlineSidebar(bool detached)
    {
        _outlineSplit.SetLiveDraggingEnabled(false);
        var startWidth = _outlineSplit.Panel2Collapsed
            ? 0
            : Math.Max(
                0,
                _outlineSplit.ClientSize.Width
                    - _outlineSplit.SplitterDistance
                    - _outlineSplit.SplitterWidth);
        var targetWidth = detached ? _detachedOutlineWidth : 0;
        var editorWidth = _outlineSplit.Panel1.ClientSize.Width;
        _outlineAnimationUsesWindowBounds = WindowState == FormWindowState.Normal;

        _detachedOutlinePanel.Dock = DockStyle.None;
        if (_outlineAnimationUsesWindowBounds)
        {
            _outlineSplit.Dock = DockStyle.None;
            _outlineSplit.Bounds = new Rectangle(
                0,
                0,
                editorWidth + _outlineSplit.SplitterWidth + _detachedOutlineWidth,
                _sidebarSplit.Panel2.ClientSize.Height);
            _outlineSplit.Panel2MinSize = _detachedOutlineMinimumWidth;
            _outlineSplit.Panel2Collapsed = false;
            _outlineSplit.SplitterDistance = editorWidth;
            PositionDetachedOutline();
        }
        else
        {
            _outlineSplit.Panel2MinSize = 0;
            if (_outlineSplit.Panel2Collapsed)
            {
                _outlineSplit.Panel2Collapsed = false;
                _outlineSplit.SplitterDistance = Math.Max(
                    _outlineSplit.Panel1MinSize,
                    _outlineSplit.ClientSize.Width - _outlineSplit.SplitterWidth);
            }
            PositionMaximizedAnimatedOutline(startWidth);
        }
        _outlineAnimationTargetDetached = detached;
        _outlineAnimationStartWidth = startWidth;
        _outlineAnimationTargetWidth = targetWidth;
        _outlineAnimationStartBounds = Bounds;
        _outlineAnimationTargetBounds = CalculateOutlineAnimationTargetBounds(
            _outlineAnimationStartBounds,
            targetWidth - startWidth);
        _outlineAnimationStartedAt = Environment.TickCount64;

        _outlineAnimationTimer.Stop();
        _outlineAnimationTimer.Start();
    }

    private void OnOutlineAnimationTick(object? sender, EventArgs eventArgs)
    {
        const double durationMilliseconds = 180.0;
        var progress = Math.Clamp(
            (Environment.TickCount64 - _outlineAnimationStartedAt) / durationMilliseconds,
            0.0,
            1.0);
        var eased = progress < 0.5
            ? 4.0 * progress * progress * progress
            : 1.0 - Math.Pow(-2.0 * progress + 2.0, 3.0) / 2.0;
        var bounds = new Rectangle(
            Interpolate(_outlineAnimationStartBounds.Left, _outlineAnimationTargetBounds.Left, eased),
            Interpolate(_outlineAnimationStartBounds.Top, _outlineAnimationTargetBounds.Top, eased),
            Interpolate(_outlineAnimationStartBounds.Width, _outlineAnimationTargetBounds.Width, eased),
            Interpolate(_outlineAnimationStartBounds.Height, _outlineAnimationTargetBounds.Height, eased));

        if (_outlineAnimationUsesWindowBounds)
        {
            Bounds = bounds;
        }
        else
        {
            var outlineWidth = Interpolate(
                _outlineAnimationStartWidth,
                _outlineAnimationTargetWidth,
                eased);
            var minimumDistance = _outlineSplit.Panel1MinSize;
            var maximumDistance = Math.Max(
                minimumDistance,
                _outlineSplit.ClientSize.Width - _outlineSplit.SplitterWidth);
            _outlineSplit.SplitterDistance = Math.Clamp(
                _outlineSplit.ClientSize.Width - outlineWidth - _outlineSplit.SplitterWidth,
                minimumDistance,
                maximumDistance);
            var visibleWidth = Math.Max(
                0,
                _outlineSplit.ClientSize.Width
                    - _outlineSplit.SplitterDistance
                    - _outlineSplit.SplitterWidth);
            PositionMaximizedAnimatedOutline(visibleWidth);
        }

        if (progress >= 1.0)
        {
            CompleteOutlineAnimation();
        }
    }

    private void CompleteOutlineAnimation()
    {
        _outlineAnimationTimer.Stop();
        var detached = _outlineAnimationTargetDetached == true;

        SuspendLayout();
        _sidebarSplit.SuspendLayout();
        _outlineSplit.SuspendLayout();
        try
        {
            if (detached)
            {
                _outlineSplit.Panel2MinSize = _detachedOutlineMinimumWidth;
                _outlineSplit.Panel2Collapsed = false;
                SetSplitterDistanceSafely(
                    _outlineSplit,
                    _detachedOutlineWidth,
                    FixedPanel.Panel2);
                _outlineSplit.Dock = DockStyle.Fill;
                PositionDetachedOutline();
            }
            else
            {
                _outlineSplit.Panel2Collapsed = true;
                _outlineSplit.Panel2MinSize = _detachedOutlineMinimumWidth;
                _outlinePanelHost.Parent = _sidebarContentHost;
                _outlinePanelHost.Dock = DockStyle.Fill;
                _sidebarTabBar.Mode = SidebarTabBarMode.Combined;
                _outlineDetached = false;
                ShowSidebarView(outline: false);
                _outlineSplit.Dock = DockStyle.Fill;
                _detachedOutlinePanel.Dock = DockStyle.Fill;
                _detachedOutlinePanel.Location = Point.Empty;
            }
        }
        finally
        {
            _outlineSplit.ResumeLayout(performLayout: true);
            _sidebarSplit.ResumeLayout(performLayout: true);
            ResumeLayout(performLayout: true);
        }

        _outlineAnimationTargetDetached = null;
        _outlineSplit.SetLiveDraggingEnabled(true);
    }

    private Rectangle CalculateOutlineAnimationTargetBounds(
        Rectangle startBounds,
        int outlineWidthDelta)
    {
        if (WindowState != FormWindowState.Normal)
        {
            return startBounds;
        }

        var targetWidth = Math.Max(
            MinimumSize.Width,
            startBounds.Width + outlineWidthDelta);
        return new Rectangle(
            startBounds.Left,
            startBounds.Top,
            targetWidth,
            startBounds.Height);
    }

    private void PositionDetachedOutline()
    {
        _detachedOutlinePanel.Bounds = new Rectangle(
            0,
            0,
            _detachedOutlineWidth,
            _outlineSplit.Panel2.ClientSize.Height);
    }

    private void PositionMaximizedAnimatedOutline(int visibleWidth)
    {
        _detachedOutlinePanel.Bounds = new Rectangle(
            visibleWidth - _detachedOutlineWidth,
            0,
            _detachedOutlineWidth,
            _outlineSplit.Panel2.ClientSize.Height);
    }

    private void ShowSidebarView(bool outline)
    {
        if (_outlineDetached)
        {
            outline = false;
        }
        _sidebarActiveOutline = outline;
        _sidebarTabBar.SetSelectedIndexSilently(outline ? 1 : 0);
        _sidebarSearchBar.OutlineMode = outline;
        _workspacePanelHost.Visible = !outline;
        _outlinePanelHost.Visible = _outlineDetached || outline;
        if (_outlineDetached || outline)
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
        if (_detachedOutlineLayout.RowStyles.Count >= 2)
        {
            _detachedOutlineLayout.RowStyles[0].Height = _detachedOutlineTabBar.Height;
            _detachedOutlineLayout.RowStyles[1].Height = _detachedOutlineSearchBar.Height;
            _detachedOutlineLayout.PerformLayout();
        }
    }
}
