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
        let action: String = DocumentCoreRuntime.shared.require("encodingChange", [
            "current": current.rawValue, "target": target.rawValue, "hasFile": hasFileURL, "readOnly": isReadOnly
        ])
        switch action {
        case "readOnly": return .rejectReadOnly
        case "none": return .noOp
        case "set": return .updateUnsavedDocumentEncoding(target)
        default: return .prompt(target: target, warnsAboutUnsavedChanges: isDirty)
        }
    }
}
