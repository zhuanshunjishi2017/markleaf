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

/// Windows 1.7.2：只有原本没有工作区且侧栏收起时，显式打开工作区才自动展开侧栏并切回文件树。
extension WorkspaceOpenRoutingPolicy {
    static func shouldRevealSidebarOnWorkspaceOpen(hadWorkspace: Bool, sidebarWasVisible: Bool) -> Bool {
        !hadWorkspace && !sidebarWasVisible
    }
}
