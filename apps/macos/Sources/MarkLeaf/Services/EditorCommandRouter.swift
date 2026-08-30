import Foundation

enum EditorCommandScope: Equatable {
    case document
    case workspace
    case windowLevel
}

/// 命令域分类：文档级命令路由到活动标签；工作区级命令作用于窗口共享工作区；
/// 窗口级命令作用于窗口视图或应用。集合与 NativeMenuBuilder.performMenuCommand 对齐。
enum EditorCommandRouter {
    static let workspaceCommands: Set<String> = [
        "openFolder", "closeFolder",
        "toggleSidebar", "workspaceTab", "outlineTab", "toggleDetachedOutline",
        "treeView", "listView",
    ]

    static let windowCommands: Set<String> = [
        "newWindow", "showPreferences", "toggleFocusMode", "toggleStatusBar",
        "showShortcuts", "revealThemeFolder", "importTheme",
    ]

    static func scope(for command: String) -> EditorCommandScope {
        if workspaceCommands.contains(command) { return .workspace }
        if windowCommands.contains(command) { return .windowLevel }
        return .document
    }
}
