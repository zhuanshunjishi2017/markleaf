import AppKit
import Foundation

/// 窗口共享工作区：根目录、文件树、列表模式、排序、扫描与监听。
/// 不持有活动文档内容，也不负责标签切换；每个窗口一个实例，所有标签共用。
final class WorkspaceContext {
    private(set) var root: String?
    private(set) var tree: [WorkspaceEntry] = []
    private(set) var documents: [WorkspaceEntry] = []
    var listMode = false
    var sortOrder = WorkspaceSortOrder.modifiedTimeDescending

    var onChanged: (() -> Void)?
    var onEntryCreated: ((URL) -> Void)?
    var onEntryMoved: ((String, String) -> Void)?

    /// 对话框宿主窗口（由窗口层注入）。
    var windowProvider: () -> NSWindow? = { nil }
    /// 打开文档的请求钩子（由窗口层注入：进入标签去重流程）。
    var openDocumentRequest: ((URL) -> Void)?

    private var scanner: WorkspaceScanner?
    private var watcher: WorkspaceWatcher?

    func load(_ path: String) {
        let fm = FileManager.default
        var isDirectory: ObjCBool = false
        guard fm.fileExists(atPath: path, isDirectory: &isDirectory), isDirectory.boolValue else { return }

        root = path

        rescan()

        // 自动监听工作区变化（删除刷新按钮）
        let watcher = WorkspaceWatcher()
        watcher.start(watching: path) { [weak self] in
            self?.rescan()
        }
        self.watcher = watcher
    }

    /// 重新扫描当前工作区（自动刷新 / 手动刷新共用）。
    func rescan() {
        guard let root else { return }
        scanner?.cancel()
        tree = []
        onChanged?()
        let scanner = WorkspaceScanner(root: root) { [weak self] entries in
            self?.tree = entries
            self?.onChanged?()
        }
        self.scanner = scanner
        scanner.scan()
        if listMode {
            scanDocuments()
        }
    }

    func close() {
        scanner?.cancel()
        scanner = nil
        watcher?.stop()
        watcher = nil
        root = nil
        tree = []
        onChanged?()
    }

    /// 仅停止扫描与监听，不清空状态（多标签下窗口关闭才真正 `close()`）。
    func closeForSessionTeardown() {
        scanner?.cancel()
        scanner = nil
        watcher?.stop()
        watcher = nil
    }

    func setListMode(_ listMode: Bool) {
        guard self.listMode != listMode else { return }
        self.listMode = listMode
        if listMode, root != nil, documents.isEmpty {
            scanDocuments()
        }
    }

    func scanDocuments() {
        guard let root else { return }
        // 存入属性保持 scanner 存活（局部变量会提前释放导致异步扫描不回调）。
        scanner?.cancel()
        let scanner = WorkspaceScanner(root: root) { _ in }
        self.scanner = scanner
        scanner.scanDocuments { [weak self] docs in
            guard let self else { return }
            self.documents = self.sortedDocuments(docs)
            self.onChanged?()
        }
    }

    /// 按用户选择的字段/方向对文档列表排序（对齐 Windows MainForm.Workspace.Sort）。
    private func sortedDocuments(_ docs: [WorkspaceEntry]) -> [WorkspaceEntry] {
        let fm = FileManager.default
        func modificationDate(_ path: String) -> Date {
            (try? fm.attributesOfItem(atPath: path)[.modificationDate] as? Date) ?? .distantPast
        }
        switch sortOrder {
        case .fileNameAscending:
            return docs.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        case .fileNameDescending:
            return docs.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedDescending }
        case .modifiedTimeAscending:
            return docs.sorted { modificationDate($0.path) < modificationDate($1.path) }
        case .modifiedTimeDescending:
            return docs.sorted { modificationDate($0.path) > modificationDate($1.path) }
        }
    }

    func setSortOrder(_ order: WorkspaceSortOrder) {
        guard sortOrder != order else { return }
        sortOrder = order
        if listMode {
            documents = sortedDocuments(documents)
        }
        onChanged?()
    }
}
