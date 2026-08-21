import Foundation

/// 原生 AppKit 文本框的编辑命令路由与可用性规则。
/// 主编辑器使用自己的会话命令；查找/替换、偏好设置等原生文本框则交给字段编辑器。
enum NativeTextEditingPolicy {
    private static let routedCommands: Set<String> = [
        "undo", "redo", "cut", "copy", "paste", "pastePlainText", "selectAll",
    ]

    static func shouldRoute(command: String, toNativeTextFieldEditor: Bool) -> Bool {
        toNativeTextFieldEditor && routedCommands.contains(command)
    }

    static func isEnabled(
        command: String,
        editable: Bool,
        hasSelection: Bool,
        hasClipboard: Bool
    ) -> Bool {
        switch command {
        case "undo", "redo":
            return true
        case "cut":
            return editable && hasSelection
        case "copy":
            return hasSelection
        case "paste", "pastePlainText":
            return editable && hasClipboard
        case "selectAll":
            return true
        default:
            return false
        }
    }
}
