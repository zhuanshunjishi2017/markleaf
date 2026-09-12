import Foundation

/// 文档换行风格。`mixed` 表示文件中同时出现了 LF 与 CRLF。
enum DocumentNewLineStyle: String, Equatable {
    case lf = "LF"
    case crlf = "CRLF"
    case mixed = "Mixed"
}

enum DocumentNewLinePolicy {
    static func detect(_ text: String) -> DocumentNewLineStyle {
        var hasLF = false
        var hasCRLF = false
        var hasBareCR = false
        let scalars = Array(text.unicodeScalars)
        var index = 0

        while index < scalars.count {
            let character = scalars[index]
            if character == "\r" {
                let next = index + 1
                if next < scalars.count, scalars[next] == "\n" {
                    hasCRLF = true
                    index = next + 1
                    continue
                }
                hasBareCR = true
            } else if character == "\n" {
                hasLF = true
            }
            index += 1
        }

        if hasCRLF && !hasLF && !hasBareCR { return .crlf }
        if hasLF && !hasCRLF && !hasBareCR { return .lf }
        if !hasLF && !hasCRLF && !hasBareCR { return .lf }
        return .mixed
    }

    /// 将文本转换为指定换行风格；mixed 表示保留调用方传入的换行序列。
    static func normalize(_ text: String, to style: DocumentNewLineStyle) -> String {
        guard style != .mixed else { return text }
        let canonical = text
            .replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n")
        return style == .crlf
            ? canonical.replacingOccurrences(of: "\n", with: "\r\n")
            : canonical
    }

    static func style(from value: String) -> DocumentNewLineStyle {
        value.lowercased() == "crlf" ? .crlf : .lf
    }
}
