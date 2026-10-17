import AppKit

/// 单个原生窗口的会话边界：标签集合、编辑器注册表、回调守卫。
/// 阶段 1 中等价于“只有一个标签的窗口”；不直接承担编辑器内部逻辑。
final class WindowSession {
    let windowID: String
    let tabStore = TabStore()
    let callbackGuard = TabCallbackGuard()
    let closedTabHistory = ClosedTabHistory()
    let workspace: WorkspaceContext
    weak var controller: EditorWindowController?
    private var sessionByTab: [DocumentTabID: EditorSession] = [:]
    private(set) var zoomPercent = SettingsService.shared.settings.restoreZoomOnOpen ? SettingsService.shared.settings.zoomPercent : 100
    private(set) var editorFocusMode = false
    private(set) var typewriterMode = false
    private var synchronizingViewState = false

    init(
        windowID: String = UUID().uuidString.lowercased(),
        workspace: WorkspaceContext = WorkspaceContext()
    ) {
        self.windowID = windowID
        self.workspace = workspace
        workspace.openDocumentRequest = { [weak self] url in
            self?.openWorkspaceDocument(url)
        }
        workspace.onEntryMoved = { [weak self] oldPath, newPath in
            guard let self else { return }
            for (_, window) in AppWindowManager.shared.windowSessions {
                let migrated = TabPathMigration.applyRename(store: window.tabStore, from: oldPath, to: newPath)
                for id in migrated { window.session(for: id)?.adoptRenamedFile(from: oldPath, to: newPath) }
                window.controller?.reloadTabBar()
            }
        }
    }

    /// 窗口内打开文件的回调：去重命中激活，未命中则由窗口层创建编辑器。
    var onOpenFile: ((TabOpenResolution.Result, URL) -> Void)?

    func requestOpenFile(_ url: URL) {
        let resolution = TabOpenResolution.resolve(
            store: tabStore,
            url: url,
            untitledLabel: L10n.t("未命名")
        )
        onOpenFile?(resolution, url)
    }

    /// 工作区请求统一从窗口层路由；“当前标签”在无标签可替换时回退为新标签。
    func openWorkspaceDocument(_ url: URL) {
        if let existing = tabStore.tab(withIdentity: FileIdentityPolicy.identity(for: url)) {
            controller?.activateTab(existing.tabID, animated: true)
            return
        }
        let route = WorkspaceOpenRoutingPolicy.route(
            hasActiveTab: activeTabSession != nil,
            prefersNewTab: MultiTabModePolicy.workspacePrefersNewTab(
                workspacePreference: SettingsService.shared.settings.workspaceOpenInNewTab,
                multiTabEnabled: SettingsService.shared.settings.multiTabEnabled
            )
        )
        switch route {
        case .newTab:
            requestOpenFile(url)
        case .currentTab:
            activeTabSession?.openDocumentBypassingRouter(at: url)
        }
    }

    func attach(session: EditorSession, to tabID: DocumentTabID) {
        sessionByTab[tabID] = session
        session.windowSession = self
        synchronizingViewState = true
        session.applyWindowViewState(zoom: zoomPercent, focus: editorFocusMode, typewriter: typewriterMode)
        synchronizingViewState = false
    }

    func updateViewState(from source: EditorSession) {
        guard !synchronizingViewState else { return }
        zoomPercent = source.zoomPercent
        editorFocusMode = source.isEditorFocusMode
        typewriterMode = source.isTypewriterMode
        synchronizingViewState = true
        defer { synchronizingViewState = false }
        for session in sessionByTab.values where session !== source {
            session.applyWindowViewState(zoom: zoomPercent, focus: editorFocusMode, typewriter: typewriterMode)
        }
    }

    func detach(_ tabID: DocumentTabID) {
        sessionByTab[tabID]?.windowSession = nil
        sessionByTab.removeValue(forKey: tabID)
        callbackGuard.remove(tabID)
    }

    func session(for tabID: DocumentTabID) -> EditorSession? {
        sessionByTab[tabID]
    }

    var activeTabSession: EditorSession? {
        guard let id = tabStore.activeTabID else { return nil }
        return sessionByTab[id]
    }

    /// 会话状态变化时把活动标签的轻量状态同步到模型。
    func syncActiveTab(from session: EditorSession, untitledLabel: String) {
        guard let tab = tabStore.activeTab, sessionByTab[tab.tabID] === session else { return }
        TabStateSync.apply(
            tab: tab,
            fileName: session.documentURL?.path,
            isDirty: session.isDirty,
            isReadOnly: session.isReadOnly,
            isReadOnlyLocked: session.isReadOnlyLocked,
            hasPendingExternalChange: session.hasPendingExternalChange,
            scrollTop: session.scrollTop,
            readingAnchor: session.readingAnchor,
            revision: session.currentRevision,
            encoding: session.documentEncoding,
            newLine: session.documentNewLine,
            visualSelectionFrom: session.visualSelectionFrom,
            visualSelectionTo: session.visualSelectionTo,
            sourceSelectionFrom: session.sourceSelectionFrom,
            sourceSelectionTo: session.sourceSelectionTo,
            untitledLabel: untitledLabel
        )
    }
}
