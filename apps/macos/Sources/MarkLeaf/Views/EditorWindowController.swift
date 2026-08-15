import AppKit

/// 主窗口控制器：侧边栏（工作区/大纲）+ WKWebView 编辑器 + 原生状态栏。
/// 对应 Windows 端 MainForm（含 SidebarTabBar + WorkspaceTreeView + OutlineTreeView）。
final class EditorWindowController: NSWindowController, NSWindowDelegate {
    let session: EditorSession
    private let statusLabel = NSTextField(labelWithString: L10n.t("就绪"))
    private let zoomLabel = NSTextField(labelWithString: "100%")
    private var sidebarView: SidebarView?
    private var editorView: EditorWebContainerView?
    private var splitView: NSSplitView?
    private var statusBar: NSStackView?
    private var statusDivider: NSBox?
    private var statusBarHeightConstraint: NSLayoutConstraint?
    private var isAnimatingSidebar = false
    private var workspaceDividerPosition: CGFloat = 240
    private var sidebarVisibleBeforeFocus = true
    private var statusBarVisibleBeforeFocus = true
    private var presentationOptionsBeforeFocus: NSApplication.PresentationOptions = []
    private var keyEventMonitor: Any?

    private(set) var isFocusMode = false
    private var allowsNextClose = false
    private var pendingCloseAfterSheetEnds = false

    var onWindowClose: ((EditorWindowController) -> Void)?

    init(session: EditorSession) {
        self.session = session
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 1100, height: 760),
            styleMask: [.titled, .closable, .miniaturizable, .resizable],
            backing: .buffered,
            defer: false)
        window.title = "MarkLeaf"
        window.minSize = NSSize(width: 860, height: 520)
        // 应用已保存的视图状态
        session.sidebarVisible = SettingsService.shared.settings.sidebarVisible
        session.statusBarVisible = SettingsService.shared.settings.statusBarVisible
        session.sidebarTabIndex = SettingsService.shared.settings.sidebarTab == "outline" ? 1 : 0
        window.setFrameAutosaveName("MarkLeafMainWindow")
        window.center()
        super.init(window: window)
        window.delegate = self
        buildContent()
        bindState()
        installFocusModeKeyMonitor()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    deinit {
        if let keyEventMonitor {
            NSEvent.removeMonitor(keyEventMonitor)
        }
    }

    /// 窗口展示后加载初始文档/工作区。
    func openInitialDocument(path: String? = nil) {
        session.openInitialDocument(path: path)
    }

    /// 窗口展示后直接装载已预读的文档。
    func openInitialDocument(prepared: PreparedDocument) {
        session.openInitialDocument(prepared: prepared)
    }

    override func showWindow(_ sender: Any?) {
        super.showWindow(sender)
        // 应用保存的侧边栏宽度与视图状态（NSSplitView 不会自动给宽度；applyViewState 启动时不会自动跑）
        DispatchQueue.main.async { [weak self] in
            guard let self, let splitView = self.splitView else { return }
            let saved = SidebarLayout.clampedWorkspaceWidth(
                SettingsService.shared.settings.workspaceWidth
            )
            splitView.setPosition(saved, ofDividerAt: 0)
            self.applyViewState()
        }
    }

    private func buildContent() {
        guard let window else { return }

        let rootView = NSView()
        let editorView = EditorWebContainerView(session: session)
        let sidebarView = SidebarView(session: session)
        self.editorView = editorView
        self.sidebarView = sidebarView

        // 布局：左栏全高毛玻璃侧边栏 | 右栏（编辑器 + 底部状态栏）
        let splitView = NSSplitView()
        splitView.isVertical = true
        splitView.dividerStyle = .thin
        splitView.delegate = self

        // 侧边栏毛玻璃容器：贯穿整个左栏（含底部），macOS 27 风格
        let sidebarContainer = NSVisualEffectView()
        sidebarContainer.material = .sidebar
        sidebarContainer.blendingMode = .behindWindow
        sidebarContainer.state = .active
        sidebarView.translatesAutoresizingMaskIntoConstraints = false
        sidebarContainer.translatesAutoresizingMaskIntoConstraints = false
        sidebarContainer.addSubview(sidebarView)
        NSLayoutConstraint.activate([
            sidebarView.leadingAnchor.constraint(equalTo: sidebarContainer.leadingAnchor),
            sidebarView.trailingAnchor.constraint(equalTo: sidebarContainer.trailingAnchor),
            sidebarView.topAnchor.constraint(equalTo: sidebarContainer.topAnchor),
            sidebarView.bottomAnchor.constraint(equalTo: sidebarContainer.bottomAnchor),
        ])

        // 右栏：编辑器 + 状态栏
        let rightColumn = NSView()
        editorView.translatesAutoresizingMaskIntoConstraints = false

        let statusBar = NSStackView()
        statusBar.orientation = .horizontal
        statusBar.alignment = .centerY
        statusBar.distribution = .fill
        statusBar.spacing = 8
        statusBar.edgeInsets = NSEdgeInsets(top: 0, left: 10, bottom: 0, right: 10)

        statusLabel.font = .systemFont(ofSize: 11)
        statusLabel.textColor = .secondaryLabelColor
        statusLabel.lineBreakMode = .byTruncatingTail
        statusLabel.setContentHuggingPriority(.defaultLow, for: .horizontal)

        zoomLabel.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
        zoomLabel.textColor = .secondaryLabelColor
        zoomLabel.alignment = .right

        let divider = NSBox()
        divider.boxType = .separator

        statusBar.addView(statusLabel, in: .leading)
        statusBar.addView(zoomLabel, in: .trailing)

        splitView.addArrangedSubview(sidebarContainer)
        splitView.addArrangedSubview(rightColumn)

        self.splitView = splitView
        self.statusBar = statusBar
        self.statusDivider = divider
        splitView.translatesAutoresizingMaskIntoConstraints = false
        rightColumn.translatesAutoresizingMaskIntoConstraints = false
        editorView.translatesAutoresizingMaskIntoConstraints = false
        divider.translatesAutoresizingMaskIntoConstraints = false
        statusBar.translatesAutoresizingMaskIntoConstraints = false

        rootView.addSubview(splitView)
        rightColumn.addSubview(editorView)
        rightColumn.addSubview(divider)
        rightColumn.addSubview(statusBar)
        NSLayoutConstraint.activate([
            splitView.topAnchor.constraint(equalTo: rootView.topAnchor),
            splitView.leadingAnchor.constraint(equalTo: rootView.leadingAnchor),
            splitView.trailingAnchor.constraint(equalTo: rootView.trailingAnchor),
            splitView.bottomAnchor.constraint(equalTo: rootView.bottomAnchor),

            editorView.topAnchor.constraint(equalTo: rightColumn.topAnchor),
            editorView.leadingAnchor.constraint(equalTo: rightColumn.leadingAnchor),
            editorView.trailingAnchor.constraint(equalTo: rightColumn.trailingAnchor),
            divider.topAnchor.constraint(equalTo: editorView.bottomAnchor),
            divider.leadingAnchor.constraint(equalTo: rightColumn.leadingAnchor),
            divider.trailingAnchor.constraint(equalTo: rightColumn.trailingAnchor),
            statusBar.topAnchor.constraint(equalTo: divider.bottomAnchor),
            statusBar.leadingAnchor.constraint(equalTo: rightColumn.leadingAnchor),
            statusBar.trailingAnchor.constraint(equalTo: rightColumn.trailingAnchor),
            statusBar.bottomAnchor.constraint(equalTo: rightColumn.bottomAnchor),
        ])
        let statusBarHeight = statusBar.heightAnchor.constraint(equalToConstant: 26)
        statusBarHeightConstraint = statusBarHeight
        statusBarHeight.isActive = true

        window.contentView = rootView
        let sidebarWidth = SidebarLayout.clampedWorkspaceWidth(
            SettingsService.shared.settings.workspaceWidth
        )
        window.setContentSize(NSSize(width: 1100 + sidebarWidth - 240, height: 760))
        workspaceDividerPosition = sidebarWidth
    }

    /// 界面语言切换：刷新状态栏与侧边栏文案。
    func applyLanguage() {
        session.applyLanguage()
        statusLabel.stringValue = session.statusText
        sidebarView?.applyLanguage()
    }

    private func bindState() {
        session.onStateChanged = { [weak self] in
            guard let self, let window = self.window else { return }
            window.title = self.session.windowTitle
            window.isDocumentEdited = self.session.isDirty
            self.statusLabel.stringValue = self.session.statusText
            self.zoomLabel.stringValue = "\(self.session.zoomPercent)%"
        }
        session.onStateChanged?()
        session.onViewStateChanged = { [weak self] in
            DispatchQueue.main.async { self?.applyViewState() }
        }
    }

    /// F11 进入/退出专注模式；仅临时隐藏界面元素，不覆盖用户保存的视图偏好。
    func toggleFocusMode() {
        if isFocusMode {
            exitFocusMode()
            return
        }

        sidebarVisibleBeforeFocus = session.sidebarVisible
        statusBarVisibleBeforeFocus = session.statusBarVisible
        presentationOptionsBeforeFocus = NSApp.presentationOptions
        isFocusMode = true
        session.sidebarVisible = false
        session.statusBarVisible = false
        session.statusText = L10n.t("专注模式已开启")
        NSApp.presentationOptions.insert(.autoHideMenuBar)
        session.onViewStateChanged?()
        session.onStateChanged?()
        NativeMenuBuilder.refreshIfNeeded()
    }

    /// Esc 或再次按 F11 退出，并恢复进入专注模式前的侧栏和状态栏状态。
    func exitFocusMode() {
        guard isFocusMode else { return }
        isFocusMode = false
        session.sidebarVisible = sidebarVisibleBeforeFocus
        session.statusBarVisible = statusBarVisibleBeforeFocus
        session.statusText = L10n.t("专注模式已关闭")
        NSApp.presentationOptions = presentationOptionsBeforeFocus
        session.onViewStateChanged?()
        session.onStateChanged?()
        NativeMenuBuilder.refreshIfNeeded()
    }

    private func installFocusModeKeyMonitor() {
        keyEventMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.window === self.window else { return event }
            return self.handleFocusModeKey(keyCode: event.keyCode) ? nil : event
        }
    }

    /// 返回是否消费按键。专注模式下 Escape=53 退出。
    @discardableResult
    func handleFocusModeKey(keyCode: UInt16) -> Bool {
        if isFocusMode, keyCode == 53 {
            exitFocusMode()
            return true
        }
        return false
    }

    /// 手动插值动画侧边栏分隔线位置（NSSplitView 的 animator().setPosition 不生效）。
    private func animateSidebar(to target: CGFloat, completion: @escaping () -> Void) {
        guard let splitView else { completion(); return }
        let start = splitView.arrangedSubviews.first?.frame.width ?? 0
        guard abs(start - target) > 1 else {
            splitView.setPosition(target, ofDividerAt: 0)
            completion()
            return
        }
        isAnimatingSidebar = true
        let duration = 0.28
        let startTime = CACurrentMediaTime()
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] timer in
            guard let self, let splitView = self.splitView else {
                timer.invalidate()
                completion()
                return
            }
            let progress = min(1, (CACurrentMediaTime() - startTime) / duration)
            let eased = progress < 0.5
                ? 2 * progress * progress
                : 1 - pow(-2 * progress + 2, 2) / 2
            splitView.setPosition(start + (target - start) * eased, ofDividerAt: 0)
            if progress >= 1 {
                timer.invalidate()
                self.isAnimatingSidebar = false
                completion()
            }
        }
        RunLoop.main.add(timer, forMode: .common)
    }

    private func applyViewState() {
        guard let splitView, let sidebarView else { return }
        let sidebar = splitView.arrangedSubviews.first

        // 侧边栏：平滑展开/收起（手动插值分隔线位置）
        if session.sidebarVisible {
            sidebar?.isHidden = false
            let saved = SidebarLayout.clampedWorkspaceWidth(
                SettingsService.shared.settings.workspaceWidth
            )
            animateSidebar(to: saved) {}
        } else {
            sidebar?.isHidden = false
            animateSidebar(to: 0) { [weak self] in
                sidebar?.isHidden = true
                self?.isAnimatingSidebar = false
            }
        }

        sidebarView.selectTab(session.sidebarTabIndex)
        sidebarView.setWorkspaceMode(listMode: session.workspaceListMode)

        // 状态栏：高度平滑过渡
        let showStatusBar = session.statusBarVisible
        if showStatusBar {
            statusBar?.isHidden = false
            statusDivider?.isHidden = false
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.2
                context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                statusBarHeightConstraint?.animator().constant = 26
            }
        } else {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.2
                context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                statusBarHeightConstraint?.animator().constant = 0
            } completionHandler: { [weak self] in
                self?.statusBar?.isHidden = true
                self?.statusDivider?.isHidden = true
            }
        }
    }

    // MARK: - NSWindowDelegate

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if allowsNextClose {
            allowsNextClose = false
            return true
        }
        guard !session.isDocumentDispositionInProgress else { return false }
        // 延迟到关闭握手之后呈现保存提示，避免 beginSheet 与窗口关闭流程重入冲突。
        DispatchQueue.main.async { [weak self, weak sender] in
            guard let self, let sender else { return }
            _ = self.session.requestDisposition(for: .closeWindow) { result in
                guard result == .proceed else { return }
                if sender.attachedSheet != nil {
                    // NSAlert 的完成回调在 sheet 收起动画结束前触发，此时窗口仍挂着 sheet，
                    // 直接 performClose 会被忽略；等 windowDidEndSheet 后再关闭。
                    self.pendingCloseAfterSheetEnds = true
                } else {
                    self.allowsNextClose = true
                    sender.performClose(nil)
                }
            }
        }
        return false
    }
    func windowDidEndSheet(_ notification: Notification) {
        guard pendingCloseAfterSheetEnds else { return }
        pendingCloseAfterSheetEnds = false
        allowsNextClose = true
        window?.performClose(nil)
    }

    func windowWillClose(_ notification: Notification) {
        exitFocusMode()
        session.cleanupForClose()
        onWindowClose?(self)
    }

    func windowDidResize(_ notification: Notification) {
        // 保存侧边栏宽度到设置
        if let sidebarView,
           let container = sidebarView.superview,
           let splitView = container.superview as? NSSplitView,
           splitView.arrangedSubviews.count == 2 {
            let width = splitView.arrangedSubviews[0].frame.width
            if width >= SidebarLayout.minimumWidth {
                SettingsService.shared.update { $0.workspaceWidth = Int(width) }
            }
        }
    }
}

extension EditorWindowController: NSSplitViewDelegate {
    func splitView(_ splitView: NSSplitView, constrainMinCoordinate proposedMinimumPosition: CGFloat, ofSubviewAt dividerIndex: Int) -> CGFloat {
        // 折叠动画期间允许收起到 0；平时保持可用最小宽度
        if isAnimatingSidebar { return 0 }
        return max(proposedMinimumPosition, SidebarLayout.minimumWidth)
    }

    func splitView(_ splitView: NSSplitView, constrainMaxCoordinate proposedMaximumPosition: CGFloat, ofSubviewAt dividerIndex: Int) -> CGFloat {
        min(
            proposedMaximumPosition,
            SidebarLayout.maximumSidebarWidth(totalWidth: splitView.bounds.width)
        )
    }

    func splitView(_ splitView: NSSplitView, shouldAdjustSizeOfSubview view: NSView) -> Bool {
        // 编辑器侧可伸缩，侧边栏固定
        view !== splitView.arrangedSubviews.first
    }
}
