import Foundation

struct StatusBarDisplayModel: Equatable {
    var commandStatus: String
    var blockType: String
    var line: Int
    var column: Int
    var characterCount: Int
    var encoding: String
    var newLine: String
    var mode: String
    var zoomPercent: Int
}

enum StatusBarDisplayPolicy {
    /// The dedicated zoom field already carries this feedback, so avoid rendering it twice.
    static func shouldShowCommandStatus(
        commandStatus: String,
        zoomVisible: Bool,
        zoomStatus: String
    ) -> Bool {
        !zoomVisible || commandStatus != zoomStatus
    }
}

enum StatusBarModePolicy {
    /// 状态栏模式按钮文案：按可视/源码状态显示，统一渲染成 </> 会丢失当前编辑模式信息。
    static func title(isSourceMode: Bool) -> String {
        isSourceMode ? "源码" : "可视化"
    }
}
