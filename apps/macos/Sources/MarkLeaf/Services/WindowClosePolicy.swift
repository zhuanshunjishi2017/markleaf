import Foundation

/// 红绿灯/关闭窗口动作的标签页语义：关闭文档标签，但保留原生窗口容器。
enum WindowClosePolicy {
    static func shouldCloseAllTabs(tabCount: Int) -> Bool {
        tabCount > 0
    }

    static let keepsWindowAfterClosingAllTabs = true
}
