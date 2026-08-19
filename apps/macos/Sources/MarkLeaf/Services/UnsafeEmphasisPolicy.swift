import Foundation

enum UnsafeEmphasisKind: String, Equatable {
    case bold
    case italic
}

enum UnsafeEmphasisAction: String, Codable, Equatable {
    case literal
    case html
}

struct UnsafeEmphasisPrompt: Equatable {
    let kind: UnsafeEmphasisKind
    let titleKey: String
    let messageKey: String
}

struct UnsafeEmphasisResponse: Equatable {
    let requestID: String
    let action: UnsafeEmphasisAction
}

enum UnsafeEmphasisPolicy {
    static func prompt(for rawKind: String) -> UnsafeEmphasisPrompt {
        if rawKind == UnsafeEmphasisKind.italic.rawValue {
            return UnsafeEmphasisPrompt(
                kind: .italic,
                titleKey: "Markdown 斜体标记可能不安全",
                messageKey: "这段斜体标记可能与相邻字符连在一起。请选择保留 Markdown 字面量，或转换为 HTML 标签。"
            )
        }
        return UnsafeEmphasisPrompt(
            kind: .bold,
            titleKey: "Markdown 粗体标记可能不安全",
            messageKey: "这段粗体标记可能与相邻字符连在一起。请选择保留 Markdown 字面量，或转换为 HTML 标签。"
        )
    }

    static func savedAction(value: String, suppressPrompt: Bool) -> UnsafeEmphasisAction? {
        guard suppressPrompt else { return nil }
        return UnsafeEmphasisAction(rawValue: value)
    }

    static func response(requestID: String, action: UnsafeEmphasisAction) -> UnsafeEmphasisResponse {
        UnsafeEmphasisResponse(requestID: requestID, action: action)
    }
}
