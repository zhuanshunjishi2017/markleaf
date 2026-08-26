import AppKit

/// 主窗口控制器：侧边栏（工作区/大纲）+ WKWebView 编辑器 + 原生状态栏。
/// 对应 Windows 端 MainForm（含 SidebarTabBar + WorkspaceTreeView + OutlineTreeView）。
final class EditorWindowController: NSWindowController, NSWindowDelegate {
    let session: EditorSession
    private let viewToggleButton = NSButton()
    private let statusLabel = NSTextField(labelWithString: L10n.t("就绪"))
    private let characterCountButton = NSButton()
    private let blockTypeLabel = NSTextField(labelWithString: "")
    private let positionLabel = NSTextField(labelWithString: "")
    private let encodingButton = NSButton()
    private let newLineButton = NSButton()
    private let modeButton = NSButton()
    private let zoomButton = NSButton()
    private var sidebarView: SidebarView?
    private var sidebarContainerView: NSView?
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
    private var sidebarAnimationTimer: Timer?
    private var lastAppliedSidebarVisible: Bool?
    private var statusClearTimer: Timer?

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
        session.workspaceListMode = SettingsService.shared.settings.workspaceListMode
        session.workspaceSortOrder = SettingsService.shared.settings.workspaceSortOrder
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
        statusClearTimer?.invalidate()
        sidebarAnimationTimer?.invalidate()
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
            guard let self else { return }
            self.applyViewState()
            // 窗口首次出现时把焦点交给编辑器，避免状态栏按钮成为 first responder 并显示蓝色 focus ring。
            if let editorView = self.editorView {
                self.window?.makeFirstResponder(editorView.webView)
            }
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
        self.sidebarContainerView = sidebarContainer
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

        configureStatusLabel(statusLabel)
        configureStatusLabel(blockTypeLabel)
        configureStatusLabel(positionLabel)
        configureStatusButton(encodingButton, title: "")
        encodingButton.target = self
        encodingButton.action = #selector(showEncodingMenu)
        encodingButton.setContentHuggingPriority(.required, for: .horizontal)
        encodingButton.setContentCompressionResistancePriority(.required, for: .horizontal)
        configureStatusButton(newLineButton, title: "")
        newLineButton.target = self
        newLineButton.action = #selector(showNewLineMenu)
        newLineButton.setContentHuggingPriority(.required, for: .horizontal)
        newLineButton.setContentCompressionResistancePriority(.required, for: .horizontal)
        configureStatusButton(zoomButton, title: "100%")
        zoomButton.target = self
        zoomButton.action = #selector(showZoomMenu)
        zoomButton.setContentHuggingPriority(.required, for: .horizontal)
        zoomButton.setContentCompressionResistancePriority(.required, for: .horizontal)

        configureStatusButton(viewToggleButton, title: "")
        viewToggleButton.image = NSImage(
            systemSymbolName: "sidebar.left",
            accessibilityDescription: L10n.t("显示侧栏")
        )
        viewToggleButton.imagePosition = .imageOnly
        viewToggleButton.toolTip = L10n.t("显示侧栏")
        viewToggleButton.target = self
        viewToggleButton.action = #selector(toggleSidebarFromStatusBar)
        viewToggleButton.setContentHuggingPriority(.required, for: .horizontal)
        viewToggleButton.setContentCompressionResistancePriority(.required, for: .horizontal)
        viewToggleButton.widthAnchor.constraint(equalToConstant: 28).isActive = true

        configureStatusButton(characterCountButton, title: "")
        characterCountButton.target = self
        characterCountButton.action = #selector(showStatistics)
        characterCountButton.setContentHuggingPriority(.required, for: .horizontal)
        characterCountButton.setContentCompressionResistancePriority(.required, for: .horizontal)

        configureStatusButton(modeButton, title: "")
        modeButton.target = self
        modeButton.action = #selector(showEditorModeMenu)
        modeButton.setContentHuggingPriority(.required, for: .horizontal)
        modeButton.setContentCompressionResistancePriority(.required, for: .horizontal)

        let divider = NSBox()
        divider.boxType = .separator

        statusBar.addView(viewToggleButton, in: .leading)
        statusBar.addView(statusLabel, in: .leading)
        statusBar.addView(characterCountButton, in: .trailing)
        statusBar.addView(blockTypeLabel, in: .trailing)
        statusBar.addView(positionLabel, in: .trailing)
        statusBar.addView(encodingButton, in: .trailing)
        statusBar.addView(newLineButton, in: .trailing)
        statusBar.addView(modeButton, in: .trailing)
        statusBar.addView(zoomButton, in: .trailing)

        // 侧边栏默认隐藏时不让它参与 splitView 布局，避免窗口出现时先显示再播放收起动画。
        if session.sidebarVisible {
            splitView.addArrangedSubview(sidebarContainer)
        } else {
            sidebarContainer.isHidden = true
        }
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
        // 侧边栏隐藏时不要用含侧边栏的初始宽度布局，避免启动瞬间先出现“展开一帧”。
        let initialWidth = session.sidebarVisible
            ? 1100 + sidebarWidth - 240
            : 1100
        window.setContentSize(NSSize(width: initialWidth, height: 760))
        workspaceDividerPosition = sidebarWidth
    }

    /// 界面语言切换：刷新状态栏与侧边栏文案。
    func applyLanguage() {
        session.applyLanguage()
        applyStatusBarContents()
        sidebarView?.applyLanguage()
    }

    private func configureStatusLabel(_ label: NSTextField) {
        label.font = .systemFont(ofSize: 11)
        label.textColor = .secondaryLabelColor
        label.lineBreakMode = .byTruncatingTail
    }

    private func configureStatusButton(_ button: NSButton, title: String) {
        button.title = title
        button.bezelStyle = .inline
        button.controlSize = .small
        button.font = .systemFont(ofSize: 11)
        button.setButtonType(.momentaryPushIn)
        // 状态栏按钮不应在窗口首次出现时抢走键盘焦点（否则会出现蓝色 focus ring）。
        button.refusesFirstResponder = true
    }

    private func bindState() {
        session.onStateChanged = { [weak self] in
            guard let self, let window = self.window else { return }
            window.title = self.session.windowTitle
            window.isDocumentEdited = self.session.isDirty
            self.applyStatusBarContents()
        }
        session.onStateChanged?()
        session.onViewStateChanged = { [weak self] in
            DispatchQueue.main.async { self?.applyViewState() }
        }
    }

    private func applyStatusBarContents() {
        let settings = SettingsService.shared.settings
        let status = settings.statusBar
        let stats = session.documentStatistics
        statusLabel.stringValue = session.statusText
        let zoomStatus = L10n.f("缩放 %d%%", session.zoomPercent)
        let showCommandStatus = StatusBarDisplayPolicy.shouldShowCommandStatus(
            commandStatus: session.statusText,
            zoomVisible: status.zoomVisible,
            zoomStatus: zoomStatus
        )
        switch status.commandDisplayMode {
        case .always:
            statusLabel.isHidden = !status.commandStatusVisible || !showCommandStatus
        case .temporary:
            statusLabel.isHidden = !status.commandStatusVisible || !showCommandStatus
            scheduleStatusClearIfNeeded()
        case .hidden:
            statusLabel.isHidden = true
            statusClearTimer?.invalidate()
        }
        characterCountButton.title = L10n.f("%d 字符", stats.characterCount)
        characterCountButton.isHidden = !status.wordCountVisible
        blockTypeLabel.stringValue = EditorSession.blockTypeDisplayName(stats.blockType)
        blockTypeLabel.isHidden = !status.blockTypeVisible
        positionLabel.stringValue = L10n.f("行 %d 列 %d", stats.line, stats.column)
        positionLabel.isHidden = !status.positionVisible
        encodingButton.title = session.documentEncoding
        encodingButton.toolTip = L10n.t("切换编码")
        encodingButton.isHidden = !status.encodingVisible
        newLineButton.title = session.documentNewLine == DocumentNewLineStyle.mixed.rawValue
            ? L10n.t("混合")
            : session.documentNewLine
        newLineButton.toolTip = L10n.t("切换换行符")
        newLineButton.isHidden = !status.newLineVisible
        modeButton.title = session.isSourceMode ? L10n.t("源码") : L10n.t("可视化")
        modeButton.toolTip = L10n.t("切换编辑模式")
        modeButton.isHidden = !status.modeToggleVisible
        zoomButton.title = "\(session.zoomPercent)%"
        zoomButton.toolTip = L10n.t("设置缩放")
        zoomButton.isHidden = !status.zoomVisible
        viewToggleButton.isHidden = !status.sidebarToggleVisible
    }

    private func scheduleStatusClearIfNeeded() {
        guard SettingsService.shared.settings.statusBar.commandDisplayMode == .temporary,
              !session.statusText.isEmpty else { return }
        statusClearTimer?.invalidate()
        statusClearTimer = Timer.scheduledTimer(withTimeInterval: 5.0, repeats: false) { [weak self] _ in
            guard let self,
                  SettingsService.shared.settings.statusBar.commandDisplayMode == .temporary else { return }
            self.statusLabel.stringValue = ""
        }
    }

    @objc private func toggleSidebarFromStatusBar() {
        session.toggleSidebar()
    }

    @objc private func showStatistics() {
        session.showDocumentStatistics()
    }

    @objc private func showEditorModeMenu() {
        let menu = NSMenu(title: L10n.t("编辑模式"))
        let visualItem = NSMenuItem(title: L10n.t("可视化"), action: #selector(selectEditorMode(_:)), keyEquivalent: "")
        visualItem.target = self
        visualItem.representedObject = "visual"
        visualItem.state = session.isSourceMode ? .off : .on
        menu.addItem(visualItem)

        let sourceItem = NSMenuItem(title: L10n.t("源码"), action: #selector(selectEditorMode(_:)), keyEquivalent: "")
        sourceItem.target = self
        sourceItem.representedObject = "source"
        sourceItem.state = session.isSourceMode ? .on : .off
        sourceItem.isEnabled = !session.isPlainText
        menu.addItem(sourceItem)
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: modeButton.bounds.height), in: modeButton)
    }

    @objc private func selectEditorMode(_ sender: NSMenuItem) {
        guard let mode = sender.representedObject as? String else { return }
        let wantsSource = mode == "source"
        guard wantsSource != session.isSourceMode, !(wantsSource && session.isPlainText) else { return }
        session.toggleSourceMode()
    }

    @objc private func showZoomMenu() {
        let menu = NSMenu(title: L10n.t("设置缩放"))
        let current = session.zoomPercent
        for percent in NativeMenuBuilder.zoomOptions {
            let item = NSMenuItem(title: "\(percent)%", action: #selector(selectZoom(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = percent
            item.state = percent == current ? .on : .off
            menu.addItem(item)
        }
        menu.addItem(.separator())
        let resetItem = NSMenuItem(title: L10n.t("重置为100%"), action: #selector(selectZoom(_:)), keyEquivalent: "")
        resetItem.target = self
        resetItem.representedObject = 100
        resetItem.state = current == 100 ? .on : .off
        menu.addItem(resetItem)
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: zoomButton.bounds.height), in: zoomButton)
    }

    @objc private func selectZoom(_ sender: NSMenuItem) {
        guard let percent = sender.representedObject as? Int else { return }
        session.setZoom(percent)
    }

    @objc private func showNewLineMenu() {
        let menu = NSMenu(title: L10n.t("换行符"))
        for style in [DocumentNewLineStyle.lf, .crlf] {
            let item = NSMenuItem(title: style.rawValue, action: #selector(selectNewLineStyle(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = style.rawValue
            item.state = session.documentNewLine == style.rawValue ? .on : .off
            item.isEnabled = !session.isReadOnly
            menu.addItem(item)
        }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: newLineButton.bounds.height), in: newLineButton)
    }

    @objc private func showEncodingMenu() {
        let menu = NSMenu(title: L10n.t("编码"))
        for encoding in DocumentEncodingPolicy.allCases {
            let item = NSMenuItem(
                title: encoding.rawValue,
                action: #selector(selectEncoding(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = encoding.rawValue
            item.state = session.documentEncoding == encoding.rawValue ? .on : .off
            item.isEnabled = !session.isReadOnly
            menu.addItem(item)
        }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: encodingButton.bounds.height), in: encodingButton)
    }

    @objc private func selectEncoding(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String else { return }
        session.requestDocumentEncodingChange(rawValue)
    }

    @objc private func selectNewLineStyle(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let style = DocumentNewLineStyle(rawValue: rawValue) else { return }
        session.setDocumentNewLine(style)
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
    private func animateSidebar(
        from explicitStart: CGFloat? = nil,
        to target: CGFloat,
        completion: @escaping () -> Void
    ) {
        guard let splitView else { completion(); return }
        sidebarAnimationTimer?.invalidate()
        // 统一以侧栏当前 frame 宽度作为动画起点；首次插入侧栏时该宽度
        // 已在调用方归一化为 0，因此与普通切换共用同一条动画路径。
        let start = explicitStart ?? splitView.arrangedSubviews.first?.frame.width ?? 0
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
                self.sidebarAnimationTimer = nil
                completion()
            }
        }
        sidebarAnimationTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    func applyViewState() {
        guard let splitView, let sidebarView, let sidebar = sidebarContainerView else { return }

        // 动画只在“侧边栏状态发生变化”时播放（启动/打开文件时状态未变，直接对齐，避免闪烁和重复收起动画）。
        let shouldAnimate = lastAppliedSidebarVisible != nil && lastAppliedSidebarVisible != session.sidebarVisible
        lastAppliedSidebarVisible = session.sidebarVisible

        let saved = SidebarLayout.clampedWorkspaceWidth(
            SettingsService.shared.settings.workspaceWidth
        )
        let isArranged = splitView.arrangedSubviews.contains(sidebar)
        let currentWidth = sidebar.frame.width

        if session.sidebarVisible {
            if !isArranged {
                // 插入前就把动画标记置位：NSSplitView 的 constrainMinCoordinate 在
                // “可见 + 非动画”时会强制最小宽度 200，把后面的 setPosition(0) 夹成 200，
                // 导致展开动画开始前先闪出完整宽度。置为动画中即可让整个 reveal
                // 准备阶段允许 0 宽，动画结束后恢复。
                isAnimatingSidebar = true
                splitView.insertArrangedSubview(sidebar, at: 0)
                // 保持隐藏直到分栏已经完成 0 宽布局，避免插入瞬间以默认宽度闪现。
                // 启动时侧栏未加入 arrangedSubviews：先完成布局并归一化到 0 宽，
                // 再从有效的 0 宽起点展开，避免内容错位同时保留显示动画。
                splitView.layoutSubtreeIfNeeded()
                splitView.setPosition(0, ofDividerAt: 0)
                splitView.layoutSubtreeIfNeeded()
                sidebar.isHidden = false
                // 解除隐藏可能让 NSSplitView 恢复旧 frame；再次归零并显式传入
                // 起点，确保首帧一定从 0 宽开始。
                splitView.setPosition(0, ofDividerAt: 0)
                splitView.layoutSubtreeIfNeeded()
                if SidebarPresentationPolicy.shouldAnimateReveal(
                    wasArranged: false,
                    visibilityChanged: shouldAnimate
                ) {
                    animateSidebar(from: 0, to: saved) {}
                } else {
                    splitView.setPosition(saved, ofDividerAt: 0)
                    isAnimatingSidebar = false
                }
            } else {
                sidebar.isHidden = false
                if SidebarPresentationPolicy.shouldAnimateReveal(
                    wasArranged: true,
                    visibilityChanged: shouldAnimate
                ) && abs(currentWidth - saved) > 1 {
                    animateSidebar(to: saved) {}
                } else {
                    splitView.setPosition(saved, ofDividerAt: 0)
                }
            }
        } else {
            switch SidebarPresentationPolicy.hiddenLayoutAction(
                isArranged: isArranged,
                visibilityChanged: shouldAnimate,
                currentWidth: currentWidth
            ) {
            case .animateCollapse:
                sidebar.isHidden = false
                animateSidebar(to: 0) { [weak self] in
                    sidebar.isHidden = true
                    self?.isAnimatingSidebar = false
                }
            case .keepHiddenWithoutDividerMutation:
                sidebar.isHidden = true
            }
        }

        sidebarView.selectTab(session.sidebarTabIndex)
        sidebarView.setWorkspaceMode(listMode: session.workspaceListMode)

        // 状态栏：高度平滑过渡
        let showStatusBar = session.statusBarVisible
        if showStatusBar {
            statusBar?.isHidden = false
            statusDivider?.isHidden = false
            applyStatusBarContents()
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
        // 折叠动画期间或侧边栏隐藏时允许收起到 0，避免窗口 resize 把隐藏侧边栏撑开；
        // 否则保持可用最小宽度。
        let minimum = SidebarPresentationPolicy.minimumDividerCoordinate(
            isSidebarVisible: session.sidebarVisible,
            isAnimating: isAnimatingSidebar,
            minimumWidth: SidebarLayout.minimumWidth
        )
        return max(proposedMinimumPosition, minimum)
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
