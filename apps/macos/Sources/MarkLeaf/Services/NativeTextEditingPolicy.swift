import AppKit
import WebKit

/// 原生 AppKit 文本框的编辑命令路由与可用性规则。
/// 主编辑器使用自己的会话命令；原生字段编辑器及多行输入则处理自身的编辑命令。
enum NativeTextEditingPolicy {
    private static let routedCommands: Set<String> = [
        "undo", "redo", "cut", "copy", "paste", "pastePlainText", "selectAll",
    ]

    static func textEditor(for responder: NSResponder?) -> NSTextView? {
        guard let editor = responder as? NSTextView else { return nil }
        var ancestor: NSView? = editor
        while let view = ancestor {
            if view is WKWebView { return nil }
            ancestor = view.superview
        }
        return editor
    }

    static func perform(_ command: String, responder: NSResponder?) -> Bool {
        guard routedCommands.contains(command), let editor = textEditor(for: responder) else { return false }
        switch command {
        case "undo": if editor.isEditable { editor.undoManager?.undo() }
        case "redo": if editor.isEditable { editor.undoManager?.redo() }
        case "cut": editor.cut(nil)
        case "copy": editor.copy(nil)
        case "paste": editor.paste(nil)
        case "pastePlainText": editor.pasteAsPlainText(nil)
        case "selectAll": editor.selectAll(nil)
        default: return false
        }
        return true
    }

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
