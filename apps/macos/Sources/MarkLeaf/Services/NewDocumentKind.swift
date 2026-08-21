import Foundation

/// 新建文档的类型及其默认保存信息。
enum NewDocumentKind: Equatable {
    case markdown
    case plainText

    var fileExtension: String {
        switch self {
        case .markdown: return "md"
        case .plainText: return "txt"
        }
    }

    var editorDocumentType: String {
        switch self {
        case .markdown: return "markdown"
        case .plainText: return "plainText"
        }
    }

    var defaultFileName: String {
        "未命名.\(fileExtension)"
    }

    static func from(fileExtension: String?) -> NewDocumentKind {
        fileExtension?.lowercased() == "txt" ? .plainText : .markdown
    }
}
