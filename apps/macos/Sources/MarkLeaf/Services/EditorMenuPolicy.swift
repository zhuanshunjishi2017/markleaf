import Foundation

enum EditorMenuPolicy {
    private static let selectionOnlyInlineFormatCommands: Set<String> = [
        "toggleBold", "toggleItalic", "toggleUnderline", "toggleStrike",
    ]

    private static let paragraphCommands: Set<String> = [
        "setParagraph", "setHeading1", "setHeading2", "setHeading3",
        "setHeading4", "setHeading5", "setHeading6",
        "promoteHeading", "demoteHeading", "toggleBlockquote", "insertMathBlock",
        "toggleCodeBlock", "insertHorizontalRule", "insertFootnote",
        "insertLineBefore", "insertLineAfter", "toggleBulletList",
        "toggleOrderedList", "toggleTaskList", "clearFormat",
    ]
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
        case "copy", "copyAs", "copyMarkdown", "copyPlain":
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
    static func isFootnoteCommandEnabled(
        command: String,
        hasFootnoteLabel: Bool,
        isReadOnly: Bool,
        isSourceMode: Bool = false
    ) -> Bool {
        guard !isSourceMode else { return false }
        switch command {
        case "insertFootnote":
            return !isReadOnly
        case "resetFootnoteLabel":
            return !isReadOnly && hasFootnoteLabel
        default:
            return true
        }
    }

    /// 字符格式的统一规则：常规标记必须有选区；行内代码和行内公式在空选时由输入框完成插入。
    static func isInlineFormatCommandEnabled(
        command: String,
        hasSelection: Bool,
        isSourceMode: Bool,
        isReadOnly: Bool
    ) -> Bool {
        guard !isSourceMode, !isReadOnly else { return false }
        if selectionOnlyInlineFormatCommands.contains(command) {
            return hasSelection
        }
        return command == "toggleCode" || command == "insertMathInline"
    }

    /// 段落格式不依赖字符选区：空选时作用于当前段落，有选区时作用于覆盖到的段落。
    static func isParagraphCommandEnabled(
        command: String,
        isSourceMode: Bool,
        isReadOnly: Bool
    ) -> Bool {
        paragraphCommands.contains(command) && !isSourceMode && !isReadOnly
    }

    static func isHeadingLevelCommandEnabled(command: String, headingLevel: Int?) -> Bool {
        guard let headingLevel else { return false }
        switch command {
        case "promoteHeading": return headingLevel > 1
        case "demoteHeading": return headingLevel < 6
        default: return false
        }
    }
}
