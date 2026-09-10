import Foundation

struct DocumentTabID: Hashable {
    let rawValue: String

    init(_ rawValue: String = UUID().uuidString.lowercased()) {
        self.rawValue = rawValue
    }
}

/// 标签轻量模型：只保存可持久化与可共享的状态；编辑器实例由窗口层按 `tabID` 另行注册。
final class DocumentTab {
    let tabID: DocumentTabID
    var fileIdentity: FileIdentity?
    var path: String?
    var title: String
    var untitledSequence: Int?
    var isDirty = false
    var isReadOnly = false
    var hasPendingExternalChange = false
    var contentRevision: Int64 = 0
    var encoding: String
    var newLine: String
    var fingerprintModificationSeconds: Int64?
    var fingerprintSize: Int64?
    var cursorPosition: Int?
    var selectionAnchor: Int?
    var selectionHead: Int?
    var visualSelectionFrom: Int?
    var visualSelectionTo: Int?
    var sourceSelectionFrom: Int?
    var sourceSelectionTo: Int?
    var scrollTop: Double?
    var snapshotFileName: String?
    var lastActivatedAt = Date()
    var isSuspended = false
    var recoveryUnavailable = false
    var lastError: String?

    init(
        tabID: DocumentTabID = DocumentTabID(),
        path: String?,
        title: String,
        encoding: String,
        newLine: String,
        untitledSequence: Int? = nil
    ) {
        self.tabID = tabID
        self.path = path
        self.fileIdentity = path.map { FileIdentityPolicy.identity(forPath: $0) }
        self.title = title
        self.encoding = encoding
        self.newLine = newLine
        self.untitledSequence = untitledSequence
    }
}

enum TabSelectionPolicy {
    /// 关闭 `index`（关闭前坐标）后应激活的下标；`count` 为关闭前标签数。
    /// 规则：优先右侧相邻标签；没有右侧时激活左侧。返回 nil 表示无剩余标签。
    static func nextActiveIndex(closing index: Int, count: Int) -> Int? {
        let remaining = count - 1
        guard remaining > 0 else { return nil }
        return min(index, remaining - 1)
    }
}

final class TabStore {
    private(set) var tabs: [DocumentTab] = []
    private(set) var activeTabID: DocumentTabID?
    private var untitledCounter = 0

    var activeTab: DocumentTab? {
        tabs.first { $0.tabID == activeTabID }
    }

    func tab(withID id: DocumentTabID) -> DocumentTab? {
        tabs.first { $0.tabID == id }
    }

    func tab(withIdentity identity: FileIdentity) -> DocumentTab? {
        tabs.first { $0.fileIdentity == identity }
    }

    @discardableResult
    func append(_ tab: DocumentTab, activate: Bool = true) -> DocumentTab {
        tabs.append(tab)
        if activate {
            self.activate(tab.tabID)
        }
        return tab
    }

    /// 在指定下标插入标签；跨窗口拖拽并入到别的窗口时使用。
    @discardableResult
    func insert(_ tab: DocumentTab, at index: Int, activate: Bool = true) -> DocumentTab {
        tabs.insert(tab, at: max(0, min(index, tabs.count)))
        if activate {
            self.activate(tab.tabID)
        }
        return tab
    }

    func activate(_ id: DocumentTabID) {
        guard let tab = tab(withID: id) else { return }
        activeTabID = id
        tab.lastActivatedAt = Date()
    }

    /// 关闭标签；返回关闭后应激活的标签（nil = 无剩余，窗口应关闭）。
    @discardableResult
    func close(_ id: DocumentTabID) -> DocumentTabID? {
        guard let index = tabs.firstIndex(where: { $0.tabID == id }) else {
            return activeTabID
        }
        let wasActive = activeTabID == id
        tabs.remove(at: index)
        guard let targetIndex = TabSelectionPolicy.nextActiveIndex(closing: index, count: tabs.count + 1) else {
            activeTabID = nil
            return nil
        }
        let next = tabs[targetIndex]
        if wasActive {
            activate(next.tabID)
        }
        return next.tabID
    }

    /// 将 `from` 位置的标签移动到 `to` 位置（均为移动前坐标）。
    func move(from source: Int, to destination: Int) {
        guard tabs.indices.contains(source), destination >= 0, destination <= tabs.count else { return }
        let tab = tabs.remove(at: source)
        let target = source < destination ? destination - 1 : destination
        tabs.insert(tab, at: min(target, tabs.count))
    }

    func nextUntitledSequence() -> Int {
        untitledCounter += 1
        return untitledCounter
    }
}
