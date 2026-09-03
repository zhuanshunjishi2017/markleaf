import AppKit

/// 单个原生窗口的会话边界：标签集合、编辑器注册表、回调守卫。
/// 阶段 1 中等价于“只有一个标签的窗口”；不直接承担编辑器内部逻辑。
final class WindowSession {
    let windowID: String
    let tabStore = TabStore()
    let callbackGuard = TabCallbackGuard()
    let workspace: WorkspaceContext
    weak var controller: EditorWindowController?
    private var sessionByTab: [DocumentTabID: EditorSession] = [:]

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
            let migrated = TabPathMigration.applyRename(store: self.tabStore, from: oldPath, to: newPath)
            for id in migrated { self.session(for: id)?.adoptRenamedFile(from: oldPath, to: newPath) }
            self.controller?.reloadTabBar()
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
    }

    func detach(_ tabID: DocumentTabID) {
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
            revision: session.currentRevision,
            encoding: session.documentEncoding,
            newLine: session.documentNewLine,
            untitledLabel: untitledLabel
        )
    }
}
