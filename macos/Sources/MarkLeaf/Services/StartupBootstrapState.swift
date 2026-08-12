/// 记录应用启动阶段的 Finder 文件意图，避免让窗口创建时序成为状态来源。
struct StartupBootstrapState {
    enum Completion: Equatable {
        case createInitialWindow(documentPath: String?, additionalDocumentPaths: [String])
        case noOp
    }

    private var pendingDocumentPaths: [String] = []
    private var isComplete = false

    /// 启动完成前缓存 Finder 文件（保持首次出现顺序并折叠重复）；完成后返回 false。
    mutating func cacheIncomingDocumentsIfNeeded(_ paths: [String]) -> Bool {
        guard !isComplete else { return false }
        for path in paths where !pendingDocumentPaths.contains(path) {
            pendingDocumentPaths.append(path)
        }
        return true
    }

    /// 仅第一次完成启动时请求创建初始窗口；首个路径用于初始窗口，其余作为附加窗口。
    mutating func complete() -> Completion {
        guard !isComplete else { return .noOp }
        isComplete = true
        let paths = pendingDocumentPaths
        pendingDocumentPaths = []
        guard let first = paths.first else {
            return .createInitialWindow(documentPath: nil, additionalDocumentPaths: [])
        }
        return .createInitialWindow(documentPath: first, additionalDocumentPaths: Array(paths.dropFirst()))
    }
}
