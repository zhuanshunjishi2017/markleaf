import Foundation

/// 工作区文件打开路由：空标签窗口必须能为“当前标签”模式恢复出一个目标。
enum WorkspaceOpenRoutingPolicy {
    enum Route: Equatable {
        case newTab
        case currentTab
    }

    static func route(hasActiveTab: Bool, prefersNewTab: Bool) -> Route {
        guard hasActiveTab else { return .newTab }
        return prefersNewTab ? .newTab : .currentTab
    }
}
