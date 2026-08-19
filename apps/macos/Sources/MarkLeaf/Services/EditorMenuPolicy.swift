import Foundation

enum EditorMenuPolicy {
    static let readOnlyCommands: Set<String> = [
        "copy", "copyMarkdown", "copyPlain", "selectAll"
    ]

    static let editableCommands: Set<String> = [
        "undo", "redo", "cut", "copy", "copyMarkdown", "copyPlain",
        "paste", "pastePlainText", "selectAll"
    ]

    static func commands(isSourceMode: Bool, isReadOnly: Bool) -> Set<String> {
        if isReadOnly { return readOnlyCommands }
        return editableCommands
    }

    /// 剪贴板命令的启用规则：剪切/拷贝/复制为* 需要选中文本；粘贴类命令需要剪贴板有内容；
    /// 只读文档仅保留拷贝/复制为*（仍需选中）和全选。
    static func isEnabled(command: String, hasSelection: Bool, clipboardHasContent: Bool, isReadOnly: Bool) -> Bool {
        switch command {
        case "cut":
            return !isReadOnly && hasSelection
        case "copy", "copyMarkdown", "copyPlain":
            return hasSelection
        case "paste", "pastePlainText":
            return !isReadOnly && clipboardHasContent
        case "selectAll":
            return true
        default:
            return true
        }
    }

    /// 脚注命令启用规则：插入注释在可编辑文档可用；重设注释编号仅在光标位于脚注定义段落时可用。
    static func isFootnoteCommandEnabled(command: String, hasFootnoteLabel: Bool, isReadOnly: Bool) -> Bool {
        switch command {
        case "insertFootnote":
            return !isReadOnly
        case "resetFootnoteLabel":
            return !isReadOnly && hasFootnoteLabel
        default:
            return true
        }
    }
}
