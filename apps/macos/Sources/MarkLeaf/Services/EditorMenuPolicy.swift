import Foundation

enum EditorSemanticContext: String, Equatable {
    case footnoteDefinition
    case frontMatter
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

// Native identifiers are translated here; command rules are supplied by editor-core.
enum EditorMenuPolicy {
    static func semanticContext(for state: EditorContextMenuState) -> EditorSemanticContext { state.semanticContext }

    static func allows(_ command: EditorNativeCommand, state: EditorContextMenuState) -> Bool {
        let identifier: String
        switch command {
        case .insertMermaid: identifier = "insertMermaid"
        case .editMermaid: identifier = "editMermaid"
        case .rerenderMermaid: identifier = "rerenderMermaid"
        case .rerenderAllMermaid: identifier = "rerenderAllMermaid"
        case .deleteMermaid: identifier = "deleteMermaid"
        case .declareCodeLanguage: identifier = "setCodeBlockLanguage"
        case .copyCodeBlock: identifier = "copyCodeBlock"
        case .goToFootnoteReference: identifier = "goToFootnoteReference"
        case .resetFootnoteNumber: identifier = "resetFootnoteLabel"
        case .clearFootnoteReferences: identifier = "clearFootnoteReferences"
        case .deleteFootnote: identifier = "deleteFootnote"
        case .tableCaption: identifier = "setTableCaption"
        case .tableRows: identifier = "addRowAfter"
        case .tableColumns: identifier = "addColumnAfter"
        case .tableAlignment: identifier = "alignTableLeft"
        case .deleteTable: identifier = "deleteTable"
        case .toggleCodeHighlight: identifier = "setCodeHighlightVisible"
        }
        return state.actions[identifier]?.enabled == true
    }
}
