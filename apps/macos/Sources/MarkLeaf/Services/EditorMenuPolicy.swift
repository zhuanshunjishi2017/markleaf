import Foundation

enum EditorSemanticContext: Equatable {
    case footnoteDefinition
    case table
    case mermaid
    case image
    case math
    case codeBlock
    case ordinaryBlock
}

enum EditorNativeCommand: Equatable {
    case insertMermaid
    case editMermaid
    case rerenderMermaid
    case rerenderAllMermaid
    case deleteMermaid
    case declareCodeLanguage
    case copyCodeBlock
    case goToFootnoteReference
    case resetFootnoteNumber
    case clearFootnoteReferences
    case deleteFootnote
    case tableCaption
    case tableRows
    case tableColumns
    case tableAlignment
    case deleteTable
    case toggleCodeHighlight
}

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
        "toggleOrderedList", "toggleTaskList", "indentListItem", "outdentListItem", "clearFormat",
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
        isReadOnly: Bool,
        inTable: Bool = false
    ) -> Bool {
        paragraphCommands.contains(command) && !isSourceMode && !isReadOnly && !inTable
    }

    static func isHeadingLevelCommandEnabled(command: String, headingLevel: Int?) -> Bool {
        guard let headingLevel else { return false }
        switch command {
        case "promoteHeading": return headingLevel > 1
        case "demoteHeading": return headingLevel < 6
        default: return false
        }
    }

    /// 状态栏模式按钮仅在 Markdown 文档中可用；纯文本固定为可视化编辑器的源码内容。
    static func isModeToggleEnabled(isPlainText: Bool) -> Bool {
        !isPlainText
    }

    static func semanticContext(for state: EditorContextMenuState) -> EditorSemanticContext {
        guard !state.isSourceMode else { return .ordinaryBlock }
        if state.footnoteDefinitionLabel?.isEmpty == false { return .footnoteDefinition }
        if state.inTable { return .table }
        if state.mermaidSelected { return .mermaid }
        if state.imageSelected { return .image }
        if state.mathInline || state.mathBlock { return .math }
        if state.codeBlock { return .codeBlock }
        return .ordinaryBlock
    }

    static func allows(_ command: EditorNativeCommand, state: EditorContextMenuState) -> Bool {
        let visualMode = !state.isSourceMode
        let writableMarkdown = !state.isReadOnly && !state.isPlainText
        let footnoteDefinition = state.footnoteDefinitionLabel?.isEmpty == false

        switch command {
        case .insertMermaid:
            return writableMarkdown
        case .editMermaid, .deleteMermaid:
            return writableMarkdown && visualMode && state.mermaidSelected
        case .rerenderMermaid:
            return !state.isReadOnly && visualMode && !state.isPlainText && state.mermaidSelected
        case .rerenderAllMermaid:
            return visualMode && !state.isPlainText && state.mermaidCount > 0
        case .declareCodeLanguage:
            return writableMarkdown && visualMode && state.codeBlock
        case .copyCodeBlock:
            return visualMode && state.codeBlock && state.codeBlockText != nil
        case .goToFootnoteReference, .resetFootnoteNumber, .clearFootnoteReferences, .deleteFootnote:
            return writableMarkdown && visualMode && footnoteDefinition
        case .tableCaption, .tableRows, .tableColumns, .tableAlignment, .deleteTable:
            return !state.isReadOnly && visualMode && state.inTable
        case .toggleCodeHighlight:
            return true
        }
    }

    static func nativeCommand(for command: String) -> EditorNativeCommand? {
        switch command {
        case "insertMermaid": return .insertMermaid
        case "editMermaid": return .editMermaid
        case "rerenderMermaid": return .rerenderMermaid
        case "rerenderAllMermaid": return .rerenderAllMermaid
        case "deleteMermaid": return .deleteMermaid
        case "declareCodeLanguage": return .declareCodeLanguage
        case "copyCodeBlock": return .copyCodeBlock
        case "goToFootnoteReference": return .goToFootnoteReference
        case "resetFootnoteLabel": return .resetFootnoteNumber
        case "clearFootnoteReferences": return .clearFootnoteReferences
        case "deleteFootnote": return .deleteFootnote
        case "toggleCodeHighlight": return .toggleCodeHighlight
        default: return nil
        }
    }
}
