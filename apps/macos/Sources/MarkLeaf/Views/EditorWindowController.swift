import AppKit
import UniformTypeIdentifiers
import WebKit

/// 主窗口控制器：侧边栏（工作区/大纲）+ WKWebView 编辑器 + 原生状态栏。
/// 对应 Windows 端 MainForm（含 SidebarTabBar + WorkspaceTreeView + OutlineTreeView）。
final class EditorWindowController: NSWindowController, NSWindowDelegate {
    let session: EditorSession
    private let viewToggleButton = NSButton()
    private let statusLabel = NSTextField(labelWithString: L10n.t("就绪"))
    /// 无文档时仍保留窗口级命令执行状态（Windows 1.7.3）。
    private var lastDocumentIndependentStatus = L10n.t("就绪")
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
    /// 供跨窗口标签拖拽做命中测试（AppWindowManager）。
    private(set) var tabBarController: TabBarController?
    private var isAnimatingTabBar = false
    private weak var rightColumnView: NSView?
    private var editorHostTopConstraint: NSLayoutConstraint?
    private var splitView: NSSplitView?
    private var outerSplitView: NSSplitView?
    private var statusBar: NSStackView?
    private var statusSpacer: NSView?
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
        tabBar.statusProvider = { [weak self] id in
            guard let self,
                  let session = self.windowSession?.session(for: id) else {
                return (false, false)
            }
            return (session.isReadOnly, session.hasPendingExternalChange)
        }
        tabBar.windowController = self
        tabBar.onTearOff = { [weak self] id, origin in
            self?.tearOffTab(id, windowOrigin: origin)
        }
        tabBar.onTransfer = { [weak self] id, target, index in
            self?.transferTab(id, to: target, at: index)
        }
        tabBar.onContextAction = { [weak self] action, id in
            self?.handleTabContextAction(action, for: id)
        }
        tabBar.workspaceRootProvider = { [weak self] in self?.session.workspaceRoot }
        tabBar.contentProvider = { [weak self] id in
            self?.windowSession?.session(for: id)?.hasContent ?? false
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
        applyMultiTabMode(animated: false)
    }

    func applyMultiTabMode(animated: Bool) {
        guard let tabBar = tabBarController, !isAnimatingTabBar else { return }
        let enabled = SettingsService.shared.settings.multiTabEnabled
        isAnimatingTabBar = animated
        tabBar.setVisible(
            MultiTabModePolicy.showsTabBar(isEnabled: enabled),
            animated: animated
        ) { [weak self] in
            self?.isAnimatingTabBar = false
        }
        applyWindowTitle()
        reloadTabBar()
    }

    private func applyWindowTitle() {
        guard SettingsService.shared.settings.multiTabEnabled else {
            window?.title = activeSession.documentURL?.lastPathComponent ?? L10n.t("未命名")
            return
        }
        window?.title = "MarkLeaf"
    }

    /// 为标签创建（或复用）会话与编辑器视图；懒加载的唯一入口。
    private func ensureEditor(for tab: DocumentTab, prepared: PreparedDocument? = nil) -> EditorSession {
        guard let windowSession else { fatalError("windowSession must exist before creating editors") }

        let session: EditorSession
        if let existing = windowSession.session(for: tab.tabID) {
            session = existing
            configureTabSession(existing, in: windowSession)
        } else {
            session = EditorSession(workspace: windowSession.workspace)
            configureTabSession(session, in: windowSession)
            windowSession.attach(session: session, to: tab.tabID)
        }

        // The initial tab is registered before the tab bar is installed. In that
        // path the session exists, but its WebView container does not yet exist.
        // Always reconcile the session/container pair before returning.
        if EditorAttachmentPolicy.needsContainer(
            existingSession: windowSession.session(for: tab.tabID) != nil,
            hasAttachedContainer: editorHostView?.attachedView(for: tab.tabID) != nil
        ) {
            let container = EditorWebContainerView(session: session)
            editorHostView?.attach(tabID: tab.tabID, view: container)
        }
        if let prepared, windowSession.session(for: tab.tabID) === session,
           editorHostView?.attachedView(for: tab.tabID) != nil,
           session.documentURL == nil {
            session.openInitialDocument(prepared: prepared)
        }
        return session
    }

    func reloadTabBar() { tabBarController?.reload() }

    @discardableResult
    func selectNextTab(reverse: Bool = false) -> Bool {
        guard let windowSession,
              let target = TabShortcutPolicy.cycleTarget(in: windowSession.tabStore, reverse: reverse) else { return false }
        activateTab(target, animated: true)
        return true
    }

    /// 拖离标签栏条带：在光标处撕成新窗口（浏览器行为）。
    func tearOffTab(_ id: DocumentTabID, windowOrigin: NSPoint) {
        transferDocument(for: id) { [weak self] document in
            guard let self, let document else {
                self?.tabBarController?.reload()
                return
            }
            let newController = AppWindowManager.shared.newWindow(
                detachedDocument: document,
                at: windowOrigin
            )
            newController.window?.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            self.removeDetachedTab(id)
        }
    }

    /// 拖到别的窗口标签栏上：把标签并入目标窗口的指定位置。
    func transferTab(_ id: DocumentTabID, to target: EditorWindowController, at index: Int) {
        guard target !== self else { return }
        transferDocument(for: id) { [weak self, weak target] document in
            guard let self, let target else { return }
            guard let document else {
                self.tabBarController?.reload()
                return
            }
            target.window?.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            target.attachTransferredDocument(document, at: index)
            self.removeDetachedTab(id)
        }
    }

    /// 把来源标签打包成可搬运的文档快照：保留未保存内容、脏状态、只读状态与双模式选区。
    func transferDocument(
        for id: DocumentTabID,
        completion: @escaping (DetachedTabDocument?) -> Void
    ) {
        guard let windowSession,
              let tab = windowSession.tabStore.tab(withID: id),
              let session = windowSession.session(for: id) else {
            completion(nil)
            return
        }
        session.requestSnapshot { result in
            guard case .success(let markdown) = result else {
                completion(nil)
                return
            }
            let selection = PendingDocumentSelection(
                visualFrom: session.visualSelectionFrom,
                visualTo: session.visualSelectionTo,
                sourceFrom: session.sourceSelectionFrom,
                sourceTo: session.sourceSelectionTo
            )
            completion(DetachedTabDocument(
                markdown: markdown,
                fileURL: session.documentURL,
                title: tab.title,
                encoding: tab.encoding,
                newLine: tab.newLine,
                isDirty: session.isDirty,
                isReadOnly: session.isReadOnly,
                untitledSequence: tab.untitledSequence,
                documentKind: session.isPlainText ? .plainText : .markdown,
                selection: selection
            ))
        }
    }

    /// 接收从别的窗口拖来的标签：按目标下标建标签并装载内容，复用原有会话/编辑器装配路径。
    @discardableResult
    func attachTransferredDocument(_ document: DetachedTabDocument, at index: Int) -> DocumentTabID? {
        guard let windowSession else { return nil }
        let tab = DocumentTab(
            path: document.fileURL?.path,
            title: document.title,
            encoding: document.encoding,
            newLine: document.newLine,
            untitledSequence: document.untitledSequence
        )
        tab.isDirty = document.isDirty
        tab.isReadOnly = document.isReadOnly
        tab.visualSelectionFrom = document.selection?.visualFrom
        tab.visualSelectionTo = document.selection?.visualTo
        tab.sourceSelectionFrom = document.selection?.sourceFrom
        tab.sourceSelectionTo = document.selection?.sourceTo
        windowSession.tabStore.insert(tab, at: index)

        let session = ensureEditor(for: tab)
        session.openInitialDocument(
            markdown: document.markdown,
            fileURL: document.fileURL,
            readOnly: document.isReadOnly,
            encoding: document.encoding,
            documentKind: document.documentKind,
            initialDirty: document.isDirty,
            selection: document.selection
        )
        activateTab(tab.tabID, animated: false)
        return tab.tabID
    }

    /// 撕下的新窗口贴着光标出现：沿用来源窗口里光标的相对位置，让标签仍在鼠标下方。
    func placeWindow(origin: NSPoint) {
        guard let window else { return }
        var frame = window.frame
        frame.origin = origin
        let onScreen = window.constrainFrameRect(frame, to: window.screen)
        window.setFrame(onScreen, display: true)
    }

    private func removeDetachedTab(_ id: DocumentTabID) {
        guard let windowSession else { return }
        if let session = windowSession.session(for: id) {
            session.cleanupForClose()
        }
        _ = windowSession.tabStore.close(id)
        windowSession.detach(id)
        editorHostView?.detach(tabID: id)
        if let active = windowSession.tabStore.activeTabID {
            activateTab(active, animated: true)
        }
        tabBarController?.reload()
    }

    func closeCurrentTab() {
        guard let id = windowSession?.tabStore.activeTabID else { return }
        closeTab(id, reason: .closeTab)
    }

    func closeOtherActiveTabs() {
        guard let id = windowSession?.tabStore.activeTabID else { return }
        closeOtherTabs(keeping: id)
    }

    func revealActiveTabInWorkspace() {
        guard let id = windowSession?.tabStore.activeTabID else { return }
        revealTabInWorkspace(id)
    }

    func copyActiveTabPath() {
        guard let id = windowSession?.tabStore.activeTabID else { return }
        copyTabPath(id)
    }

    func revealActiveTabInFinder() {
        guard let id = windowSession?.tabStore.activeTabID else { return }
        revealTabInFinder(id)
    }

    func copyActiveFileContents() {
        guard let id = windowSession?.tabStore.activeTabID else { return }
        copyTabContents(id)
    }

    func shareActiveTab() {
        guard let id = windowSession?.tabStore.activeTabID else { return }
        shareTab(id)
    }

    private func revealTabInWorkspace(_ id: DocumentTabID) {
        guard let windowSession,
              let tab = windowSession.tabStore.tab(withID: id),
              let path = tab.path,
              let root = session.workspaceRoot,
              URL(fileURLWithPath: path).standardizedFileURL.path.hasPrefix(
                URL(fileURLWithPath: root).standardizedFileURL.path + "/"
              ) else { return }
        session.sidebarVisible = true
        session.showWorkspaceTab()
        session.setWorkspaceListMode(false)
        applyViewState()
        sidebarView?.revealWorkspacePath(path)
    }

    private func copyTabPath(_ id: DocumentTabID) {
        guard let path = windowSession?.tabStore.tab(withID: id)?.path else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(path, forType: .string)
    }

    private func copyTabContents(_ id: DocumentTabID) {
        guard let session = windowSession?.session(for: id) else { return }
        session.requestSnapshot { result in
            guard case .success(let markdown) = result else { return }
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(markdown, forType: .string)
            session.statusText = L10n.t("已复制")
        }
    }

    private func revealTabInFinder(_ id: DocumentTabID) {
        guard let path = windowSession?.tabStore.tab(withID: id)?.path else { return }
        NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)])
    }

    private func shareTab(_ id: DocumentTabID) {
        guard let path = windowSession?.tabStore.tab(withID: id)?.path,
              FileManager.default.fileExists(atPath: path),
              let anchorView = window?.contentView else { return }
        let picker = NSSharingServicePicker(items: [URL(fileURLWithPath: path)])
        picker.show(relativeTo: anchorView.bounds, of: anchorView, preferredEdge: .maxX)
    }

    private func handleEditorHostDrop(_ urls: [URL]) {
        let drop = EditorDropPolicy.classify(urls)
        for url in drop.images {
            activeSession.insertImageFile(at: url)
        }
        for url in drop.documents {
            openFileInTab(url)
        }
    }

    func saveAllTabs() {
        guard let windowSession else { return }
        let targets = SaveAllPolicy.targets(tabs: windowSession.tabStore.tabs)
        func saveNext(_ index: Int) {
            guard index < targets.count else {
                self.reloadTabBar()
                return
            }
            let id = targets[index]
            guard let tab = windowSession.tabStore.tab(withID: id),
                  let session = windowSession.session(for: id) else {
                saveNext(index + 1)
                return
            }
            tab.lastError = nil
            session.saveDocument { [weak self, weak tab] success in
                guard let self else { return }
                if !success {
                    tab?.lastError = L10n.t("保存失败")
                } else if let tab {
                    TabStateSync.apply(
                        tab: tab,
                        fileName: session.documentURL?.path,
                        isDirty: session.isDirty,
                        isReadOnly: session.isReadOnly,
                        hasPendingExternalChange: session.hasPendingExternalChange,
                        revision: session.currentRevision,
                        encoding: session.documentEncoding,
                        newLine: session.documentNewLine,
                        visualSelectionFrom: session.visualSelectionFrom,
                        visualSelectionTo: session.visualSelectionTo,
                        sourceSelectionFrom: session.sourceSelectionFrom,
                        sourceSelectionTo: session.sourceSelectionTo,
                        untitledLabel: L10n.t("未命名")
                    )
                }
                self.reloadTabBar()
                saveNext(index + 1)
            }
        }
        saveNext(0)
    }

    func restoreInitialTabIfNeeded() {
        guard let windowSession, let tab = windowSession.tabStore.activeTab ?? windowSession.tabStore.tabs.first else { return }
        let session = ensureEditor(for: tab)
        if let snapshot = tab.snapshotFileName, let markdown = SessionSnapshotIO.read(fileName: snapshot) {
            session.loadDocument(markdown: markdown, fileURL: tab.path.map { URL(fileURLWithPath: $0) }, encoding: tab.encoding, initialDirty: tab.isDirty, scrollTop: tab.scrollTop ?? 0, visualSelectionFrom: tab.visualSelectionFrom, visualSelectionTo: tab.visualSelectionTo, sourceSelectionFrom: tab.sourceSelectionFrom, sourceSelectionTo: tab.sourceSelectionTo)
        } else if let path = tab.path, let prepared = try? PreparedDocument.read(from: URL(fileURLWithPath: path)) {
            let selection = PendingDocumentSelection(
                visualFrom: tab.visualSelectionFrom,
                visualTo: tab.visualSelectionTo,
                sourceFrom: tab.sourceSelectionFrom,
                sourceTo: tab.sourceSelectionTo
            )
            session.openInitialDocument(prepared: prepared, selection: selection)
            session.pendingRestoreScrollTop = tab.scrollTop
        } else {
            session.newDocument()
        }
        // 恢复首个标签同样走激活流程，绑定状态栏、侧栏和文档状态回调。
        activateTab(tab.tabID, animated: false)
    }

    /// 打开文件为标签：去重命中则激活，未命中则建标签并加载。
    /// 空标签状态下由菜单触发的「打开…」：面板挂在窗口上，选择后按标签去重打开。
    func openDocumentPanel() {
        let panel = NSOpenPanel()
        panel.title = L10n.t("打开 Markdown 文档")
        panel.allowedContentTypes = [.plainText, (UTType(filenameExtension: "md") ?? .plainText)]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        guard let window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.openFileInTab(url)
        }
    }

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
        guard MultiTabModePolicy.allowsTabCreation(isEnabled: SettingsService.shared.settings.multiTabEnabled) else {
            windowSession.activeTabSession?.openDocumentBypassingRouter(at: url)
            return
        }
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
        guard MultiTabModePolicy.allowsTabCreation(isEnabled: SettingsService.shared.settings.multiTabEnabled) else {
            let target = activeSession
            target.requestDisposition(for: .replaceDocument) { result in
                guard result == .proceed else { return }
                target.newDocument(kind: kind)
            }
            return
        }
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
        applyWindowTitle()
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
        if let path = tab.path, let prepared = try? PreparedDocument.read(from: URL(fileURLWithPath: path)) {
            let selection = PendingDocumentSelection(
                visualFrom: tab.visualSelectionFrom,
                visualTo: tab.visualSelectionTo,
                sourceFrom: tab.sourceSelectionFrom,
                sourceTo: tab.sourceSelectionTo
            )
            session.openInitialDocument(prepared: prepared, selection: selection)
            session.pendingRestoreScrollTop = tab.scrollTop
        } else {
            session.newDocument()
        }
    }

    func closeTab(_ id: DocumentTabID, reason: TabCloseReason) {
        guard let windowSession, windowSession.tabStore.tab(withID: id) != nil else { return }
        let session = windowSession.session(for: id)
        let closingSession = windowSession.session(for: id)
        let closingTab = windowSession.tabStore.tab(withID: id)
        let finish: (DocumentDispositionResult) -> Void = { [weak self] result in
            guard result == .proceed, let self, let windowSession = self.windowSession else { return }
            if let closingTab, ClosedTabHistoryPolicy.shouldRecord(closeReason: reason) {
                AppWindowManager.shared.registerClosedTab(ClosedTabRecord(
                    path: closingTab.path,
                    title: closingTab.title,
                    untitledSequence: closingTab.untitledSequence,
                    isDirty: closingTab.isDirty,
                    isReadOnly: closingTab.isReadOnly,
                    encoding: closingTab.encoding,
                    newLine: closingTab.newLine,
                    visualSelectionFrom: closingTab.visualSelectionFrom,
                    visualSelectionTo: closingTab.visualSelectionTo,
                    sourceSelectionFrom: closingTab.sourceSelectionFrom,
                    sourceSelectionTo: closingTab.sourceSelectionTo,
                    scrollTop: closingTab.scrollTop,
                    snapshotFileName: closingTab.snapshotFileName,
                    documentKind: closingSession?.documentKind ?? NewDocumentKind.from(fileExtension: closingTab.path)
                ))
            }
            let next = windowSession.tabStore.close(id)
            windowSession.detach(id)
            self.editorHostView?.detach(tabID: id)
            if let next {
                self.activateTab(next, animated: true)
            } else if WindowClosePolicy.keepsWindowAfterClosingAllTabs {
                self.applyStatusBarContents()
            } else {
                self.closeWindowForReal()
            }
            AppWindowManager.shared.closeFindPanelIfBound(to: [closingSession])
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
        case .locate:
            activateTab(id, animated: true)
            revealTabInWorkspace(id)
        case .copyPath:
            copyTabPath(id)
        case .copyContents:
            copyTabContents(id)
        case .revealInFinder:
            revealTabInFinder(id)
        case .share:
            shareTab(id)
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
        let closedSessions = ids.map { windowSession.session(for: $0) }
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
            AppWindowManager.shared.closeFindPanelIfBound(to: closedSessions)
            if let active = windowSession.tabStore.activeTabID {
                self.activateTab(active, animated: true)
            } else if WindowClosePolicy.keepsWindowAfterClosingAllTabs {
                self.applyStatusBarContents()
            }
            self.tabBarController?.reload()
        }
    }

    func restoreLastClosedTab() {
        guard let windowSession else { return }
        guard let record = AppWindowManager.shared.takeLastClosedTab() else { return }
        guard MultiTabModePolicy.allowsTabCreation(isEnabled: SettingsService.shared.settings.multiTabEnabled) else {
            windowSession.activeTabSession?.requestDisposition(for: .replaceDocument) { [weak self] result in
                guard result == .proceed, let self else { return }
                self.loadRestoredTab(record, in: windowSession)
            }
            return
        }

        let tab = DocumentTab(
            path: record.path,
            title: record.title,
            encoding: record.encoding,
            newLine: record.newLine,
            untitledSequence: record.untitledSequence
        )
        tab.isDirty = record.isDirty
        tab.isReadOnly = record.isReadOnly
        tab.visualSelectionFrom = record.visualSelectionFrom
        tab.visualSelectionTo = record.visualSelectionTo
        tab.sourceSelectionFrom = record.sourceSelectionFrom
        tab.sourceSelectionTo = record.sourceSelectionTo
        tab.scrollTop = record.scrollTop
        tab.snapshotFileName = record.snapshotFileName
        windowSession.tabStore.append(tab)
        _ = ensureEditor(for: tab)
        loadRestoredTab(record, in: windowSession, into: tab)
        activateTab(tab.tabID, animated: true)
        tabBarController?.reload()
    }

    private func loadRestoredTab(_ record: ClosedTabRecord, in windowSession: WindowSession, into tab: DocumentTab? = nil) {
        let selection = PendingDocumentSelection(
            visualFrom: record.visualSelectionFrom,
            visualTo: record.visualSelectionTo,
            sourceFrom: record.sourceSelectionFrom,
            sourceTo: record.sourceSelectionTo
        )
        let target = tab.flatMap { windowSession.session(for: $0.tabID) } ?? windowSession.activeTabSession
        guard let target else { return }

        if let snapshotFileName = record.snapshotFileName,
           let markdown = SessionSnapshotIO.read(fileName: snapshotFileName) {
            target.loadDocument(
                markdown: markdown,
                fileURL: record.path.map { URL(fileURLWithPath: $0) },
                readOnly: record.isReadOnly,
                encoding: record.encoding,
                documentKind: record.documentKind,
                initialDirty: record.isDirty,
                scrollTop: record.scrollTop ?? 0,
                visualSelectionFrom: record.visualSelectionFrom,
                visualSelectionTo: record.visualSelectionTo,
                sourceSelectionFrom: record.sourceSelectionFrom,
                sourceSelectionTo: record.sourceSelectionTo
            )
            return
        }

        if let path = record.path {
            do {
                let detected = try PreparedDocument.read(from: URL(fileURLWithPath: path))
                let prepared = PreparedDocument(
                    url: detected.url,
                    markdown: detected.markdown,
                    encoding: record.encoding,
                    isReadOnly: record.isReadOnly
                )
                target.openInitialDocument(prepared: prepared, selection: selection)
                target.pendingRestoreScrollTop = record.scrollTop
            } catch {
                target.presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
            }
            return
        }

        target.loadDocument(
            markdown: "",
            fileURL: nil,
            readOnly: record.isReadOnly,
            encoding: record.encoding,
            documentKind: record.documentKind,
            initialDirty: false,
            scrollTop: record.scrollTop ?? 0,
            visualSelectionFrom: record.visualSelectionFrom,
            visualSelectionTo: record.visualSelectionTo,
            sourceSelectionFrom: record.sourceSelectionFrom,
            sourceSelectionTo: record.sourceSelectionTo
        )
    }

    /// 切换前处理旧标签；切换不弹保存确认，失败只在标签上留痕。
    private func performTabSwitchSave(of tabID: DocumentTabID?) {
        guard let windowSession, let tabID,
              let tab = windowSession.tabStore.tab(withID: tabID),
              let session = windowSession.session(for: tabID) else { return }
        session.requestScrollStateSnapshot { [weak tab] _ in
            guard let tab else { return }
            tab.scrollTop = session.scrollTop
        }
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
        defer { AppWindowManager.shared.refreshThemeSettings() }
        guard let session = windowSession?.activeTabSession else { return }
        if let windowSession {
            configureTabSession(session, in: windowSession)
        }
        bindSessionCallbacks(session)
        sidebarView?.rebind(to: session)
        detachedOutlineView?.rebind(to: session)
        applyStatusBarContents()
        window?.title = "MarkLeaf"
        window?.isDocumentEdited = session.isDirty
        if let findPanel = AppWindowManager.shared.currentFindPanel {
            findPanel.updateSession(session)
        }
    }

    private func configureTabSession(_ session: EditorSession, in windowSession: WindowSession) {
        session.openViaWindow = { [weak windowSession] url in windowSession?.requestOpenFile(url) }
        session.newTabRequest = { [weak self] kind in self?.newUntitledTab(kind: kind) }
        session.saveAllRequest = { [weak self] in self?.saveAllTabs() }
        session.onAcquiredFileURL = { [weak self, weak windowSession, weak session] url in
            guard let self, let windowSession, let session,
                  let tab = windowSession.tabStore.tabs.first(where: { windowSession.session(for: $0.tabID) === session }) else { return }
            tab.path = url.path
            tab.fileIdentity = FileIdentityPolicy.identity(forPath: url.path)
            tab.title = url.lastPathComponent
            tab.lastError = nil
            self.reloadTabBar()
        }
        session.onRecoveryWriteFailure = { [weak self, weak windowSession, weak session] in
            guard let self, let windowSession, let session,
                  let tab = windowSession.tabStore.tabs.first(where: { windowSession.session(for: $0.tabID) === session }) else { return }
            tab.recoveryUnavailable = true
            self.reloadTabBar()
            if windowSession.tabStore.activeTabID == tab.tabID {
                session.statusText = L10n.t("恢复保护暂时不可用")
            }
        }
        session.onRecoveryWriteSuccess = { [weak self, weak windowSession, weak session] in
            guard let self, let windowSession, let session,
                  let tab = windowSession.tabStore.tabs.first(where: { windowSession.session(for: $0.tabID) === session }) else { return }
            tab.recoveryUnavailable = false
            self.reloadTabBar()
        }
        session.exportLeaseProvider = { [weak self, weak windowSession, weak session] in
            guard let self, let windowSession, let session,
                  let tab = windowSession.tabStore.tabs.first(where: { windowSession.session(for: $0.tabID) === session }) else { return nil }
            let container = self.editorHostView?.attachedView(for: tab.tabID) as? EditorWebContainerView
            return ExportSessionLease(tabID: tab.tabID, session: session, container: container)
        }
    }

    /// 把会话的观察回调绑定到窗口 UI（状态/大纲/视图状态），并同步到标签模型。
    private func bindSessionCallbacks(_ session: EditorSession) {
        session.onSelectionStateChanged = { [weak self] in
            guard let self, let windowSession = self.windowSession else { return }
            windowSession.syncActiveTab(from: session, untitledLabel: L10n.t("未命名"))
        }
        session.onStateChanged = { [weak self] in
            guard let self, let window = self.window else { return }
            applyWindowTitle()
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
    func openInitialDocument(prepared: PreparedDocument, selection: PendingDocumentSelection? = nil) {
        session.openInitialDocument(prepared: prepared, selection: selection)
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
        editorHost.emptyDropTarget.onDropURLs = { [weak self] urls in
            self?.handleEditorHostDrop(urls)
        }
        self.editorHostView = editorHost
        editorHost.translatesAutoresizingMaskIntoConstraints = false

        let statusBar = NSStackView()
        statusBar.orientation = .horizontal
        statusBar.alignment = .centerY
        // .fill + 弹性占位：多余宽度由占位视图吸收；fillProportionally 会在
        // 仅剩单个控件（如空标签时只剩侧栏按钮）时把它拉伸满整条状态栏。
        statusBar.distribution = .fill
        statusBar.spacing = 8
        statusBar.edgeInsets = NSEdgeInsets(top: 0, left: 10, bottom: 0, right: 10)

        let statusSpacer = NSView()
        statusSpacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        statusSpacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        self.statusSpacer = statusSpacer

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
        statusBar.addView(statusSpacer, in: .leading)
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
        // 首帧前完成一次布局，避免状态栏 trailing 重力在首次显示时短暂靠左。
        rootView.layoutSubtreeIfNeeded()
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
        AppWindowManager.shared.refreshThemeSettings()
        let hasActiveTab = windowSession?.activeTabSession != nil
        guard StatusBarEmptyStatePolicy.shouldShowDocumentItems(hasActiveTab: hasActiveTab) else {
            // 全部标签已关闭：清空并隐藏文档相关项，仅保留窗口级控件。
            statusLabel.stringValue = lastDocumentIndependentStatus
            statusLabel.isHidden = lastDocumentIndependentStatus.isEmpty
            statusClearTimer?.invalidate()
            characterCountButton.isHidden = true
            blockTypeLabel.isHidden = true
            positionLabel.isHidden = true
            encodingButton.isHidden = true
            newLineButton.isHidden = true
            modeButton.isHidden = true
            zoomButton.isHidden = true
            viewToggleButton.isHidden = !SettingsService.shared.settings.statusBar.sidebarToggleVisible
            statusBar?.needsLayout = true
            return
        }
        let session = activeSession
        let settings = SettingsService.shared.settings
        let status = settings.statusBar
        let stats = session.documentStatistics
        lastDocumentIndependentStatus = statusLabel.stringValue
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
        session.statusText = L10n.t("最简模式已开启")
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
        session.statusText = L10n.t("最简模式已关闭")
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
        if event.modifierFlags.contains(.option),
           let digit = TabShortcutPolicy.digit(forKeyCode: event.keyCode),
           let windowSession,
           let target = TabShortcutPolicy.numberedTarget(in: windowSession.tabStore, digit: digit) {
            activateTab(target, animated: true)
            return true
        }
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

        // Sidebar content follows the active tab session. The controller's
        // session is the window bootstrap session and may no longer be the
        // session currently bound to the sidebar after a tab switch.
        let sidebarSession = sidebarView.session
        let selectedTabIndex = SidebarStateSourcePolicy.selectedTabIndex(
            activeSessionIndex: sidebarSession.sidebarTabIndex,
            bootstrapSessionIndex: session.sidebarTabIndex
        )
        sidebarView.selectTab(selectedTabIndex, persist: false)
        sidebarView.setWorkspaceMode(listMode: sidebarSession.workspaceListMode)
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
        // 红绿灯 = 关闭窗口本身；先对所有标签走保存确认，再真正关窗。
        guard WindowClosePolicy.closesWindowOnTrafficLight else { return true }
        DispatchQueue.main.async { [weak self] in
            guard let self, let windowSession = self.windowSession else { return }
            let requests: [SequentialDocumentDispositionQueue.Request] = windowSession.tabStore.tabs.compactMap { tab in
                guard let session = windowSession.session(for: tab.tabID) else { return nil }
                return { completion in
                    _ = session.requestDisposition(for: .closeWindow, completion: completion)
                }
            }
            SequentialDocumentDispositionQueue.run(requests) { [weak self] result in
                guard result == .proceed else { return }
                self?.closeWindowForReal()
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
