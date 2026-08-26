import Foundation

struct EditorCommandStatePayload: Equatable {
    let sourceMode: Bool
    let readOnly: Bool
    let codeBlock: Bool
    let codeBlockLanguage: String?
    let codeBlockText: String?
    let mermaidSelected: Bool
    let mermaidSource: String?
    let mermaidCount: Int

    static func decode(_ payload: [String: Any]?) -> Self {
        let decodedMermaidCount = payload?["mermaidCount"] as? Int ?? 0
        return Self(
            sourceMode: payload?["sourceMode"] as? Bool ?? false,
            readOnly: payload?["readOnly"] as? Bool ?? false,
            codeBlock: payload?["codeBlock"] as? Bool ?? false,
            codeBlockLanguage: payload?["codeBlockLanguage"] as? String,
            codeBlockText: payload?["codeBlockText"] as? String,
            mermaidSelected: payload?["mermaidSelected"] as? Bool ?? false,
            mermaidSource: payload?["mermaidSource"] as? String,
            mermaidCount: max(0, decodedMermaidCount)
        )
    }
}
