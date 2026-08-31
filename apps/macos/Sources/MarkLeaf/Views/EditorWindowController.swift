import AppKit
import WebKit

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
    private var detachedOutlineView: DetachedOutlineView?
    private var detachedOutlineContainerView: NSView?
    private var editorHostView: EditorHostView?
    private var tabBarController: TabBarController?
    private weak var rightColumnView: NSView?
    private var editorHostTopConstraint: NSLayoutConstraint?
    private var splitView: NSSplitView?
    private var outerSplitView: NSSplitView?
    private var statusBar: NSStackView?
    private var statusDivider: NSBox?
    private var statusBarHeightConstraint: NSLayoutConstraint?
    private var isAnimatingSidebar = false
    private var isAnimatingOutline = false
    private var workspaceDividerPosition: CGFloat = 240
    private var sidebarVisibleBeforeFocus = true
    private var outlineDetachedBeforeFocus = false
    private var statusBarVisibleBeforeFocus = true
    private var presentationOptionsBeforeFocus: NSApplication.PresentationOptions = []
    private var keyEventMonitor: Any?
    private var sidebarAnimationTimer: Timer?
    private var outlineAnimationTimer: Timer?
    private var lastAppliedSidebarVisible: Bool?
    private var lastAppliedOutlineDetached: Bool?
    private var statusClearTimer: Timer?

    private(set) var isFocusMode = false
    private var allowsNextClose = false
    private var pendingCloseAfterSheetEnds = false

    var onWindowClose: ((EditorWindowController) -> Void)?
    var windowSession: WindowSession? {
        didSet {
            guard windowSession != nil, tabBarController == nil else { return }
            installMultiTabUI()
        }
    }

    /// 当前活动标签会话（多标签下跟随活动标签；单标签回退到初始会话）。
    private var activeSession: EditorSession {
        windowSession?.activeTabSession ?? session
    }

    /// 注入 `windowSession` 后搭建标签栏并把初始标签挂到编辑器宿主。
    private func installMultiTabUI() {
        guard let windowSession, let rightColumn = rightColumnView, let editorHost = editorHostView else { return }
        let tabBar = TabBarController(tabStore: windowSession.tabStore)
        tabBar.onActivate = { [weak self] id in self?.activateTab(id, animated: true) }
        tabBar.onClose = { [weak self] id in self?.closeTab(id, reason: .closeTab) }
        tabBar.onNewTab = { [weak self] in self?.newUntitledTab() }
        tabBar.onContextAction = { [weak self] action, id in
            self?.handleTabContextAction(action, for: id)
        }
        tabBar.onReorder = { [weak self] from, to in
            guard let self else { return }
            self.windowSession?.tabStore.move(from: from, to: to)
            self.tabBarController?.reload()
        }
        self.tabBarController = tabBar
        tabBar.translatesAutoresizingMaskIntoConstraints = false
        rightColumn.addSubview(tabBar)
        editorHostTopConstraint?.isActive = false
        NSLayoutConstraint.activate([
            tabBar.topAnchor.constraint(equalTo: rightColumn.topAnchor),
            tabBar.leadingAnchor.constraint(equalTo: rightColumn.leadingAnchor),
            tabBar.trailingAnchor.constraint(equalTo: rightColumn.trailingAnchor),
            editorHost.topAnchor.constraint(equalTo: tabBar.bottomAnchor),
        ])

        // 把窗口首个（初始）标签的编辑器挂到宿主并展示。
        if let initialTab = windowSession.tabStore.activeTab ?? windowSession.tabStore.tabs.first {
            _ = ensureEditor(for: initialTab)
            editorHost.show(tabID: initialTab.tabID, animated: false, reduceMotion: true)
        }
        tabBar.reload()
    }

    /// 为标签创建（或复用）会话与编辑器视图；懒加载的唯一入口。
    private func ensureEditor(for tab: DocumentTab, prepared: PreparedDocument? = nil) -> EditorSession {
        if let existing = windowSession?.session(for: tab.tabID) {
            if let windowSession {
                configureTabSession(existing, in: windowSession)
            }
            return existing
        }
        guard let windowSession else { fatalError("windowSession must exist before creating editors") }

        let session = EditorSession(workspace: windowSession.workspace)
        configureTabSession(session, in: windowSession)
        windowSession.attach(session: session, to: tab.tabID)

        let container = EditorWebContainerView(session: session)
        editorHostView?.attach(tabID: tab.tabID, view: container)
        if let prepared {
            session.openInitialDocument(prepared: prepared)
        }
        return session
    }

    func restoreInitialTabIfNeeded() {
        guard let windowSession, let tab = windowSession.tabStore.activeTab ?? windowSession.tabStore.tabs.first else { return }
        let session = ensureEditor(for: tab)
        if let snapshot = tab.snapshotFileName, let markdown = SessionSnapshotIO.read(fileName: snapshot) {
            session.loadDocument(markdown: markdown, fileURL: tab.path.map { URL(fileURLWithPath: $0) }, encoding: tab.encoding, initialDirty: tab.isDirty)
        } else if let path = tab.path, let prepared = try? PreparedDocument.read(from: URL(fileURLWithPath: path)) {
            session.openInitialDocument(prepared: prepared)
        } else {
            session.newDocument()
        }
        editorHostView?.show(tabID: tab.tabID, animated: false, reduceMotion: true)
        tabBarController?.reload()
    }

    /// 打开文件为标签：去重命中则激活，未命中则建标签并加载。
    func openFileInTab(_ url: URL) {
        guard let windowSession else { return }
        let resolution = TabOpenResolution.resolve(
            store: windowSession.tabStore,
            url: url,
            untitledLabel: L10n.t("未命名")
        )
        handleOpenResolution(resolution, url: url)
    }

    /// 使用窗口去重结果打开文件（避免二次去重造成已建标签不加载文档）。
    func handleOpenResolution(_ resolution: TabOpenResolution.Result, url: URL) {
        guard let windowSession else { return }
        let prepared: PreparedDocument
        do {
            prepared = try PreparedDocument.read(from: url)
        } catch {
            windowSession.activeTabSession?.presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
            return
        }
        switch resolution {
        case .activateExisting(let id):
            activateTab(id, animated: true)
        case .created(let tab):
            _ = ensureEditor(for: tab, prepared: prepared)
            activateTab(tab.tabID, animated: true)
        }
        tabBarController?.reload()
    }

    func newUntitledTab(kind: NewDocumentKind = .markdown) {
        guard let windowSession else { return }
        let settings = SettingsService.shared.settings
        let tab = DocumentTab(
            path: nil,
            title: "",
            encoding: settings.defaultEncoding,
            newLine: DocumentNewLinePolicy.style(from: settings.newLineStyle).rawValue
        )
        tab.untitledSequence = windowSession.tabStore.nextUntitledSequence()
        tab.title = "\(L10n.t("未命名")) \(tab.untitledSequence ?? 1)"
        windowSession.tabStore.append(tab)
        let session = ensureEditor(for: tab)
        session.newDocument(kind: kind)
        activateTab(tab.tabID, animated: true)
        tabBarController?.reload()
    }

    func activateTab(_ id: DocumentTabID, animated: Bool) {
        guard let windowSession, windowSession.tabStore.tab(withID: id) != nil else { return }
        let previous = windowSession.tabStore.activeTabID
        if previous != id {
            performTabSwitchSave(of: previous)
            windowSession.tabStore.activate(id)
        }
        if windowSession.session(for: id) == nil {
            if let tab = windowSession.tabStore.tab(withID: id) {
                rebuildSuspendedTab(tab)
            }
        }
        editorHostView?.show(
            tabID: id,
            animated: animated,
            reduceMotion: NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        )
        rebindActiveSessionUI()
        tabBarController?.reload()
        applyStatusBarContents()
    }

    func suspendBackgroundTabsIfNeeded() {
        guard let windowSession else { return }
        let order = TabMemoryPressurePolicy.suspensionOrder(
            tabs: windowSession.tabStore.tabs,
            activeTabID: windowSession.tabStore.activeTabID
        )
        for id in order {
            guard let tab = windowSession.tabStore.tab(withID: id), !tab.isDirty,
                  let session = windowSession.session(for: id) else { continue }
            session.cleanupForClose()
            windowSession.detach(id)
            editorHostView?.detach(tabID: id)
            tab.isSuspended = true
        }
        tabBarController?.reload()
    }

    private func rebuildSuspendedTab(_ tab: DocumentTab) {
        let session = ensureEditor(for: tab)
        tab.isSuspended = false
        if let path = tab.path {
            session.openDocument(at: URL(fileURLWithPath: path))
        } else {
            session.newDocument()
        }
    }

    enum TabCloseReason {
        case closeTab
        case closeWindow
        case terminate
    }

    func closeTab(_ id: DocumentTabID, reason: TabCloseReason) {
        guard let windowSession, windowSession.tabStore.tab(withID: id) != nil else { return }
        let session = windowSession.session(for: id)
        let finish: (DocumentDispositionResult) -> Void = { [weak self] result in
            guard result == .proceed, let self, let windowSession = self.windowSession else { return }
            let next = windowSession.tabStore.close(id)
            windowSession.detach(id)
            self.editorHostView?.detach(tabID: id)
            if TabShortcutPolicy.closesWindow(tabCount: windowSession.tabStore.tabs.count + 1) ||
                (reason == .closeWindow && next == nil) {
                self.closeWindowForReal()
            } else if let next {
                self.activateTab(next, animated: true)
            }
            self.tabBarController?.reload()
        }
        if let session {
            // 关闭标签与关闭窗口都必须走显式保存确认；不能套用“切换文档”的自动保存规则。
            _ = session.requestDisposition(for: .closeWindow, completion: finish)
        } else {
            finish(.proceed)
        }
    }

    private func handleTabContextAction(_ action: TabContextAction, for id: DocumentTabID) {
        guard let windowSession, let index = windowSession.tabStore.tabs.firstIndex(where: { $0.tabID == id }) else { return }
        switch action {
        case .close:
            closeTab(id, reason: .closeTab)
        case .closeOthers:
            closeOtherTabs(keeping: id)
        case .closeToRight:
            closeTabsToRight(of: index)
        }
    }

    private func closeOtherTabs(keeping id: DocumentTabID) {
        guard let windowSession else { return }
        closeTabs(windowSession.tabStore.tabs.map(\.tabID).filter { $0 != id })
    }

    private func closeTabsToRight(of index: Int) {
        guard let windowSession else { return }
        let ids = windowSession.tabStore.tabs.dropFirst(index + 1).map(\.tabID)
        closeTabs(ids)
    }

    private func closeTabs(_ ids: [DocumentTabID]) {
        guard let windowSession, !ids.isEmpty else { return }
        let requests: [SequentialDocumentDispositionQueue.Request] = ids.compactMap { id in
            guard let session = windowSession.session(for: id) else { return nil }
            return { completion in
                _ = session.requestDisposition(for: .closeWindow, completion: completion)
            }
        }
        SequentialDocumentDispositionQueue.run(requests) { [weak self] result in
            guard result == .proceed, let self, let windowSession = self.windowSession else { return }
            for id in ids where windowSession.tabStore.tab(withID: id) != nil {
                _ = windowSession.tabStore.close(id)
                windowSession.detach(id)
                self.editorHostView?.detach(tabID: id)
            }
            if let active = windowSession.tabStore.activeTabID {
                self.activateTab(active, animated: true)
            }
            self.tabBarController?.reload()
        }
    }

    /// 切换前处理旧标签；切换不弹保存确认，失败只在标签上留痕。
    private func performTabSwitchSave(of tabID: DocumentTabID?) {
        guard let windowSession, let tabID,
              let tab = windowSession.tabStore.tab(withID: tabID),
              let session = windowSession.session(for: tabID) else { return }
        let action = TabSwitchSavePolicy.action(
            isDirty: session.isDirty,
            hasPath: session.documentURL != nil,
            autoSaveOnSwitch: SettingsService.shared.settings.saveOnDocumentSwitch
        )
        switch action {
        case .nothing:
            break
        case .save:
            tab.lastError = nil
            session.saveDocument { [weak self, weak tab] success in
                guard let self, let tab else { return }
                if !success {
                    tab.lastError = L10n.t("自动保存失败")
                    self.tabBarController?.reload()
                }
            }
        case .snapshotOnly:
            session.flushRecoverySnapshotNow()
        }
    }

    /// 状态栏、侧栏、大纲、查找面板全部跟随活动标签会话。
    private func rebindActiveSessionUI() {
        guard let session = windowSession?.activeTabSession else { return }
        if let windowSession {
            configureTabSession(session, in: windowSession)
        }
        bindSessionCallbacks(session)
        sidebarView?.rebind(to: session)
        detachedOutlineView?.rebind(to: session)
        applyStatusBarContents()
        window?.title = session.windowTitle
        window?.isDocumentEdited = session.isDirty
        if let findPanel = AppWindowManager.shared.currentFindPanel {
            findPanel.updateSession(session)
        }
    }

    private func configureTabSession(_ session: EditorSession, in windowSession: WindowSession) {
        session.openViaWindow = { [weak windowSession] url in windowSession?.requestOpenFile(url) }
        session.newTabRequest = { [weak self] kind in self?.newUntitledTab(kind: kind) }
    }

    /// 把会话的观察回调绑定到窗口 UI（状态/大纲/视图状态），并同步到标签模型。
    private func bindSessionCallbacks(_ session: EditorSession) {
        session.onStateChanged = { [weak self] in
            guard let self, let window = self.window else { return }
            window.title = session.windowTitle
            window.isDocumentEdited = session.isDirty
            self.applyStatusBarContents()
            self.windowSession?.syncActiveTab(from: session, untitledLabel: L10n.t("未命名"))
            self.tabBarController?.reload()
        }
        session.onViewStateChanged = { [weak self] in
            DispatchQueue.main.async { self?.applyViewState() }
        }
        session.onOutlineChanged = { [weak self] in
            DispatchQueue.main.async {
                self?.sidebarView?.outlineChanged()
                self?.detachedOutlineView?.reload()
            }
        }
        session.onOutlineSelectionChanged = { [weak self] in
            DispatchQueue.main.async {
                self?.sidebarView?.outlineSelectionChanged()
                self?.detachedOutlineView?.synchronizeSelection()
            }
        }
    }

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
        session.outlineDetached = SettingsService.shared.settings.outlineDetached
        if session.outlineDetached, session.sidebarTabIndex == 1 { session.sidebarTabIndex = 0 }
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
        outlineAnimationTimer?.invalidate()
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
            if let webView = self.activeEditorWebView {
                self.window?.makeFirstResponder(webView)
            }
        }
    }

    private var activeEditorWebView: WKWebView? {
        guard let host = editorHostView,
              let id = windowSession?.tabStore.activeTabID,
              let container = host.attachedView(for: id) else { return nil }
        return container.webView
    }

    private func buildContent() {
        guard let window else { return }

        let rootView = NSView()
        let sidebarView = SidebarView(session: session)
        let detachedOutlineView = DetachedOutlineView(session: session)
        self.sidebarView = sidebarView
        self.detachedOutlineView = detachedOutlineView

        // 布局：左栏全高毛玻璃侧边栏 | 右栏（编辑器 + 底部状态栏）
        let splitView = NSSplitView()
        splitView.isVertical = true
        splitView.dividerStyle = .thin
        splitView.delegate = self

        let outerSplitView = NSSplitView()
        outerSplitView.isVertical = true
        outerSplitView.dividerStyle = .thin
        outerSplitView.delegate = self

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

        let detachedOutlineContainer = NSVisualEffectView()
        detachedOutlineContainer.material = .sidebar
        detachedOutlineContainer.blendingMode = .behindWindow
        detachedOutlineContainer.state = .active
        detachedOutlineView.translatesAutoresizingMaskIntoConstraints = false
        detachedOutlineContainer.addSubview(detachedOutlineView)
        NSLayoutConstraint.activate([
            detachedOutlineView.leadingAnchor.constraint(equalTo: detachedOutlineContainer.leadingAnchor),
            detachedOutlineView.trailingAnchor.constraint(equalTo: detachedOutlineContainer.trailingAnchor),
            detachedOutlineView.topAnchor.constraint(equalTo: detachedOutlineContainer.topAnchor),
            detachedOutlineView.bottomAnchor.constraint(equalTo: detachedOutlineContainer.bottomAnchor),
        ])
        self.detachedOutlineContainerView = detachedOutlineContainer

        // 右栏：编辑器 + 状态栏
        let rightColumn = NSView()
        self.rightColumnView = rightColumn
        let editorHost = EditorHostView()
        self.editorHostView = editorHost
        editorHost.translatesAutoresizingMaskIntoConstraints = false

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

        outerSplitView.addArrangedSubview(splitView)
        if session.outlineDetached {
            outerSplitView.addArrangedSubview(detachedOutlineContainer)
        }
        self.splitView = splitView
        self.outerSplitView = outerSplitView
        self.statusBar = statusBar
        self.statusDivider = divider
        outerSplitView.translatesAutoresizingMaskIntoConstraints = false
        rightColumn.translatesAutoresizingMaskIntoConstraints = false
        editorHost.translatesAutoresizingMaskIntoConstraints = false
        divider.translatesAutoresizingMaskIntoConstraints = false
        statusBar.translatesAutoresizingMaskIntoConstraints = false

        rootView.addSubview(outerSplitView)
        rightColumn.addSubview(editorHost)
        rightColumn.addSubview(divider)
        rightColumn.addSubview(statusBar)
        let editorHostTop = editorHost.topAnchor.constraint(equalTo: rightColumn.topAnchor)
        self.editorHostTopConstraint = editorHostTop
        NSLayoutConstraint.activate([
            outerSplitView.topAnchor.constraint(equalTo: rootView.topAnchor),
            outerSplitView.leadingAnchor.constraint(equalTo: rootView.leadingAnchor),
            outerSplitView.trailingAnchor.constraint(equalTo: rootView.trailingAnchor),
            outerSplitView.bottomAnchor.constraint(equalTo: rootView.bottomAnchor),

            editorHostTop,
            editorHost.leadingAnchor.constraint(equalTo: rightColumn.leadingAnchor),
            editorHost.trailingAnchor.constraint(equalTo: rightColumn.trailingAnchor),
            divider.topAnchor.constraint(equalTo: editorHost.bottomAnchor),
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
        // 默认编辑器内容宽 1000，并夹到当前屏幕可视区，避免默认窗口过大。
        let baseEditorWidth: CGFloat = 1000
        let initialWidth = session.sidebarVisible
            ? baseEditorWidth + sidebarWidth - 240
            : baseEditorWidth
        let outlineWidth = SidebarLayout.clampedWorkspaceWidth(SettingsService.shared.settings.outlineWidth)
        let visibleFrame = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let desiredContentWidth = initialWidth + (session.outlineDetached ? outlineWidth : 0)
        let contentWidth = max(SidebarLayout.minimumOutlineSplitWidth, min(desiredContentWidth, visibleFrame.width - 80))
        let contentHeight = min(760, visibleFrame.height - 80)
        window.setContentSize(NSSize(width: contentWidth, height: contentHeight))
        workspaceDividerPosition = sidebarWidth
    }

    /// 界面语言切换：刷新状态栏与侧边栏文案。
    func applyLanguage() {
        session.applyLanguage()
        applyStatusBarContents()
        sidebarView?.applyLanguage()
        detachedOutlineView?.applyLanguage()
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
        bindSessionCallbacks(session)
        session.onStateChanged?()
    }

    private func applyStatusBarContents() {
        let session = activeSession
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
        modeButton.title = L10n.t(StatusBarModePolicy.title(isSourceMode: session.isSourceMode))
        modeButton.toolTip = L10n.t("切换编辑模式")
        modeButton.isHidden = !status.modeToggleVisible
        modeButton.isEnabled = EditorMenuPolicy.isModeToggleEnabled(isPlainText: session.isPlainText)
        zoomButton.title = "\(session.zoomPercent)%"
        zoomButton.toolTip = L10n.t("设置缩放")
        zoomButton.isHidden = !status.zoomVisible
        viewToggleButton.isHidden = !status.sidebarToggleVisible
    }

    private func scheduleStatusClearIfNeeded() {
        let session = activeSession
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
        outlineDetachedBeforeFocus = session.outlineDetached
        statusBarVisibleBeforeFocus = session.statusBarVisible
        presentationOptionsBeforeFocus = NSApp.presentationOptions
        isFocusMode = true
        session.sidebarVisible = false
        session.outlineDetached = false
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
        session.outlineDetached = outlineDetachedBeforeFocus
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
            return self.handleTabCycleKey(event: event) || self.handleFocusModeKey(keyCode: event.keyCode) ? nil : event
        }
    }

    /// Control-Tab / Control-Shift-Tab 在当前窗口内循环切换标签。
    @discardableResult
    func handleTabCycleKey(event: NSEvent) -> Bool {
        guard event.modifierFlags.contains(.control), event.keyCode == 48,
              let windowSession,
              let target = TabShortcutPolicy.cycleTarget(
                in: windowSession.tabStore,
                reverse: event.modifierFlags.contains(.shift)
              ) else {
            return false
        }
        activateTab(target, animated: true)
        return true
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
                } else if !isAnimatingSidebar {
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
                if !isAnimatingSidebar {
                    sidebar.isHidden = true
                }
            }
        }

        sidebarView.selectTab(session.sidebarTabIndex, persist: false)
        sidebarView.setWorkspaceMode(listMode: session.workspaceListMode)
        applyDetachedOutlineState()

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

    private func applyDetachedOutlineState() {
        guard let outerSplitView, let container = detachedOutlineContainerView else { return }
        let shouldAnimate = lastAppliedOutlineDetached != nil
            && lastAppliedOutlineDetached != session.outlineDetached
        lastAppliedOutlineDetached = session.outlineDetached
        let isArranged = outerSplitView.arrangedSubviews.contains(container)

        if session.outlineDetached {
            if !isArranged {
                // 插入前就标记为动画态，允许分隔线先收齐到右缘（右侧宽度为 0），
                // 再从 0 宽展开到保存宽度，等效于侧边栏的 reveal 动画。
                isAnimatingOutline = true
                outerSplitView.insertArrangedSubview(container, at: 1)
                outerSplitView.layoutSubtreeIfNeeded()
                outerSplitView.setPosition(outerSplitView.bounds.width, ofDividerAt: 0)
                outerSplitView.layoutSubtreeIfNeeded()
            }
            container.isHidden = false
            outerSplitView.layoutSubtreeIfNeeded()
            let width = SidebarLayout.clampedWorkspaceWidth(SettingsService.shared.settings.outlineWidth)
            let target = max(SidebarLayout.minimumOutlineSplitWidth, outerSplitView.bounds.width - width)
            if shouldAnimate {
                let start = isArranged
                    ? (outerSplitView.arrangedSubviews.first?.frame.width ?? target)
                    : outerSplitView.bounds.width
                animateOutline(from: start, to: target) { [weak self] in
                    self?.isAnimatingOutline = false
                }
            } else if !isAnimatingOutline {
                outerSplitView.setPosition(target, ofDividerAt: 0)
                isAnimatingOutline = false
            }
            detachedOutlineView?.reload()
        } else if isArranged {
            let remove = {
                outerSplitView.removeArrangedSubview(container)
                container.removeFromSuperview()
                container.isHidden = true
            }
            if shouldAnimate {
                let start = outerSplitView.arrangedSubviews.first?.frame.width ?? outerSplitView.bounds.width
                animateOutline(from: start, to: outerSplitView.bounds.width) {
                    if !self.session.outlineDetached { remove() }
                }
            } else if !isAnimatingOutline {
                remove()
            }
        }
    }

    /// 手动插值动画右侧大纲分隔线位置（与 animateSidebar 共用同一套缓动/时长/回调机制）。
    private func animateOutline(
        from explicitStart: CGFloat? = nil,
        to target: CGFloat,
        completion: @escaping () -> Void
    ) {
        guard let outerSplitView else { completion(); return }
        outlineAnimationTimer?.invalidate()
        // 以右侧栏当前 frame 宽度对应的分隔线位置作为动画起点。
        let start = explicitStart ?? outerSplitView.arrangedSubviews.first?.frame.width ?? 0
        guard abs(start - target) > 1 else {
            outerSplitView.setPosition(target, ofDividerAt: 0)
            completion()
            return
        }
        isAnimatingOutline = true
        let duration = 0.28
        let startTime = CACurrentMediaTime()
        let timer = Timer(timeInterval: 1.0 / 60.0, repeats: true) { [weak self] timer in
            guard let self, let outerSplitView = self.outerSplitView else {
                timer.invalidate()
                completion()
                return
            }
            let progress = min(1, (CACurrentMediaTime() - startTime) / duration)
            let eased = progress < 0.5
                ? 2 * progress * progress
                : 1 - pow(-2 * progress + 2, 2) / 2
            outerSplitView.setPosition(start + (target - start) * eased, ofDividerAt: 0)
            if progress >= 1 {
                timer.invalidate()
                self.isAnimatingOutline = false
                self.outlineAnimationTimer = nil
                completion()
            }
        }
        outlineAnimationTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    // MARK: - NSWindowDelegate

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        if allowsNextClose {
            allowsNextClose = false
            return true
        }
        guard let windowSession, !windowSession.tabStore.tabs.isEmpty else { return true }
        DispatchQueue.main.async { [weak self] in
            guard let self, let windowSession = self.windowSession else { return }
            if TabShortcutPolicy.closesWindow(tabCount: windowSession.tabStore.tabs.count),
               let only = windowSession.tabStore.tabs.first {
                self.closeTab(only.tabID, reason: .closeWindow)
            } else if let active = windowSession.tabStore.activeTab {
                self.closeTab(active.tabID, reason: .closeTab)
            }
        }
        return false
    }

    private func closeWindowForReal() {
        if window?.attachedSheet != nil {
            pendingCloseAfterSheetEnds = true
        } else {
            allowsNextClose = true
            window?.performClose(nil)
        }
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
        if TerminationTransactionPolicy.windowCloseMayMutateSession(isTerminationCommitted: AppWindowManager.shared.isTerminationCommitted) {
            do { try SessionStore.shared.commit(manifest: AppWindowManager.shared.buildSessionManifest()) }
            catch { AppLog.warning("关窗会话提交失败: \(error.localizedDescription)") }
        }
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
        if session.outlineDetached,
           let outerSplitView,
           outerSplitView.arrangedSubviews.count == 2 {
            let width = outerSplitView.arrangedSubviews[1].frame.width
            if width >= SidebarLayout.minimumWidth {
                SettingsService.shared.update { $0.outlineWidth = Int(width) }
            }
        }
    }
}

extension EditorWindowController: NSSplitViewDelegate {
    func splitView(_ splitView: NSSplitView, constrainMinCoordinate proposedMinimumPosition: CGFloat, ofSubviewAt dividerIndex: Int) -> CGFloat {
        if splitView === outerSplitView {
            return max(proposedMinimumPosition, SidebarLayout.minimumOutlineSplitWidth)
        }
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
        if splitView === outerSplitView {
            // 右侧大纲滑入/滑出动画期间允许分隔线推到最右，使右栏宽度收到 0。
            if isAnimatingOutline {
                return min(proposedMaximumPosition, splitView.bounds.width)
            }
            return min(proposedMaximumPosition, splitView.bounds.width - SidebarLayout.minimumWidth)
        }
        return min(
            proposedMaximumPosition,
            SidebarLayout.maximumSidebarWidth(totalWidth: splitView.bounds.width)
        )
    }

    func splitView(_ splitView: NSSplitView, shouldAdjustSizeOfSubview view: NSView) -> Bool {
        if splitView === outerSplitView {
            return view === splitView.arrangedSubviews.first
        }
        // 编辑器侧可伸缩，侧边栏固定
        return view !== splitView.arrangedSubviews.first
    }
}
