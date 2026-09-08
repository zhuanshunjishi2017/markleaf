import Foundation

struct EditorPasteCommand: Equatable {
    let command: String
    let text: String?
    let html: String?
}

enum EditorPasteContentKind {
    case finderFiles
    case bitmapImage
    case textOrHTML
    case none
}

enum EditorPastePolicy {
    static let modifyingCommands: Set<String> = ["pasteMarkdown", "pasteClipboard"]

    static func contentKind(
        hasFinderFiles: Bool,
        hasBitmapImage: Bool,
        plainText: String?,
        html: String?
    ) -> EditorPasteContentKind {
        if hasFinderFiles { return .finderFiles }
        if hasBitmapImage { return .bitmapImage }
        if plainText?.isEmpty == false || html?.isEmpty == false { return .textOrHTML }
        return .none
    }

    static func command(isSourceMode: Bool, plainText: String?, html: String?) -> EditorPasteCommand? {
        let text = plainText.flatMap { $0.isEmpty ? nil : $0 }
        let richHTML = html.flatMap { $0.isEmpty ? nil : $0 }
        if isSourceMode {
            guard let text else { return nil }
            return EditorPasteCommand(command: "pasteText", text: text, html: nil)
        }
        guard text != nil || richHTML != nil else { return nil }
        return EditorPasteCommand(command: "pasteClipboard", text: text, html: richHTML)
    }

    static func payload(command: String, text: String?, html: String?) -> [String: Any] {
        var payload: [String: Any] = ["command": command]
        if let text { payload["text"] = text }
        if let html { payload["html"] = html }
        return payload
    }
}
