/// 记录应用启动阶段的 Finder 文件意图，避免让窗口创建时序成为状态来源。
struct StartupBootstrapState {
    enum Completion: Equatable {
        case createInitialWindow(documentPath: String?)
        case noOp
    }

    private(set) var pendingDocumentPath: String?
    private var isComplete = false

    /// 启动完成前缓存 Finder 文件；完成后由调用方立即路由。
    mutating func cacheIncomingDocumentIfNeeded(_ path: String) -> Bool {
        guard !isComplete else { return false }
        pendingDocumentPath = path
        return true
    }

    /// 仅第一次完成启动时请求创建初始窗口。
    mutating func complete() -> Completion {
        guard !isComplete else { return .noOp }
        isComplete = true
        let documentPath = pendingDocumentPath
        pendingDocumentPath = nil
        return .createInitialWindow(documentPath: documentPath)
    }
}
