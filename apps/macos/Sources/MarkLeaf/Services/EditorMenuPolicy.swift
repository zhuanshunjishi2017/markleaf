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
}
