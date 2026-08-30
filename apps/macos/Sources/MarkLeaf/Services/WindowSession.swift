import AppKit

/// 单个原生窗口的会话边界：标签集合、编辑器注册表、回调守卫。
/// 阶段 1 中等价于“只有一个标签的窗口”；不直接承担编辑器内部逻辑。
final class WindowSession {
    let windowID: String
    let tabStore = TabStore()
    let callbackGuard = TabCallbackGuard()
    weak var controller: EditorWindowController?
    private var sessionByTab: [DocumentTabID: EditorSession] = [:]

    init(windowID: String = UUID().uuidString.lowercased()) {
        self.windowID = windowID
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
