import Foundation

/// 红绿灯与标签页两种关闭动作的窗口语义。
enum WindowClosePolicy {
    /// 红绿灯关闭 = 关闭窗口本身；窗口内标签先走保存确认队列。
    static let closesWindowOnTrafficLight = true

    static let keepsWindowAfterClosingAllTabs = true
}
