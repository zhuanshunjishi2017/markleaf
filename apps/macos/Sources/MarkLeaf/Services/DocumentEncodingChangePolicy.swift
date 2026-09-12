import Foundation

enum DocumentEncodingChangeChoice: Equatable {
    case directRead
    case convertEncoding
    case cancel
}

enum DocumentEncodingChangeAction: Equatable {
    case noOp
    case updateUnsavedDocumentEncoding(DocumentEncodingPolicy)
    case prompt(target: DocumentEncodingPolicy, warnsAboutUnsavedChanges: Bool)
    case rejectReadOnly
}

enum DocumentEncodingChangePolicy {
    static func action(
        current: DocumentEncodingPolicy,
        target: DocumentEncodingPolicy,
        hasFileURL: Bool,
        isDirty: Bool,
        isReadOnly: Bool
    ) -> DocumentEncodingChangeAction {
        if isReadOnly { return .rejectReadOnly }
        if current == target { return .noOp }
        if !hasFileURL { return .updateUnsavedDocumentEncoding(target) }
        return .prompt(target: target, warnsAboutUnsavedChanges: isDirty)
    }
}
