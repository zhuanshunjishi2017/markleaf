import Foundation

enum DocumentNewLineStyle: String, Equatable {
    case lf = "LF"
    case crlf = "CRLF"
    case cr = "CR"
    case mixed = "Mixed"
}

enum DocumentNewLinePolicy {
    static func detect(_ text: String) -> DocumentNewLineStyle {
        let value: String = DocumentCoreRuntime.shared.require("newLine", ["text": text])
        return DocumentNewLineStyle(rawValue: value)!
    }
    static func normalize(_ text: String, to style: DocumentNewLineStyle) -> String {
        DocumentCoreRuntime.shared.require("normalizeNewLines", ["text": text, "style": style.rawValue])
    }
    static func style(from value: String) -> DocumentNewLineStyle {
        DocumentNewLineStyle(rawValue: value.uppercased() == "MIXED" ? "Mixed" : value.uppercased()) ?? .lf
    }
}
