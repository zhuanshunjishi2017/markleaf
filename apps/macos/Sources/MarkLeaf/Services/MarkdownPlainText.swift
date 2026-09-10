import Foundation

/// 工作区预览/搜索使用的 Markdown 纯文本投影（对齐 Windows MarkdownPlainText）。
/// 只服务于只读展示和搜索，不会修改文档内容。
enum MarkdownPlainText {
    static func fromDocument(_ source: String, isMarkdown: Bool) -> String {
        guard !source.isEmpty else { return "" }
        let normalized = source
            .replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n")
        var text = isMarkdown ? stripMarkdown(normalized) : normalized
        text = decodeHTMLEntities(text)
        return collapseWhitespace(text)
    }

    private static func stripMarkdown(_ text: String) -> String {
        var value = text
        value = replace(value, pattern: #"---[ \t]*\n.*?\n---[ \t]*(?:\n|\z)"#, options: [.dotMatchesLineSeparators], isAnchored: true)
        value = replace(value, pattern: #"^[ \t]{0,3}(?:`{3,}|~{3,})[^\n]*$"#, options: [.anchorsMatchLines])
        value = replace(value, pattern: #"<[^>]+>"#)
        value = replace(value, pattern: #"!\[([^\]]*)\]\([^\)]*\)"#, template: "$1")
        value = replace(value, pattern: #"\[([^\]]+)\]\([^\)]*\)"#, template: "$1")
        value = replace(value, pattern: #"<((?:https?://|mailto:)[^>]+)>"#, options: [.caseInsensitive], template: "$1")
        value = replace(value, pattern: #"^[ \t]*\[\^[^\]]+\]:[ \t]*(.*)$"#, options: [.anchorsMatchLines], template: "$1")
        value = replace(value, pattern: #"\[\^([^\]]+)\]"#, template: "$1")
        value = replace(value, pattern: #"^[ \t]{0,3}(?:(?:#{1,6}|>)[ \t]+|[-+*][ \t]+\[[ xX]\][ \t]+|(?:[-+*]|\d+[.)])[ \t]+)"#, options: [.anchorsMatchLines])
        value = replace(value, pattern: #"^[ \t]*(?:=+|-+)[ \t]*$"#, options: [.anchorsMatchLines])
        value = replace(value, pattern: #"^[ \t]*\|?[ \t]*:?-{3,}:?[ \t]*(?:\|[ \t]*:?-{3,}:?[ \t]*)+\|?[ \t]*$"#, options: [.anchorsMatchLines])
        value = replace(value, pattern: #"`+([^`]*?)`+"#, template: "$1")
        value = replace(value, pattern: #"\$\$?|\\[()[\]]"#)
        value = replace(value, pattern: #"(?<!\\)(?:\*{1,3}|_{1,3}|~{2}|={2})"#)
        value = replace(value, pattern: #"\|"#, template: " ")
        value = replace(value, pattern: #"\\([\\`*{}\[\]()#+\-.!_>~|])"#, template: "$1")
        return value
    }

    private static func replace(
        _ text: String,
        pattern: String,
        options: NSRegularExpression.Options = [],
        isAnchored: Bool = false,
        template: String = ""
    ) -> String {
        guard let regex = try? NSRegularExpression(pattern: pattern, options: options) else { return text }
        let range = NSRange(text.startIndex..., in: text)
        let matchingOptions: NSRegularExpression.MatchingOptions = isAnchored ? [.anchored] : []
        return regex.stringByReplacingMatches(in: text, options: matchingOptions, range: range, withTemplate: template)
    }

    private static func decodeHTMLEntities(_ text: String) -> String {
        text
            .replacingOccurrences(of: "&amp;", with: "&")
            .replacingOccurrences(of: "&lt;", with: "<")
            .replacingOccurrences(of: "&gt;", with: ">")
            .replacingOccurrences(of: "&quot;", with: "\"")
            .replacingOccurrences(of: "&apos;", with: "'")
            .replacingOccurrences(of: "&#39;", with: "'")
            .replacingOccurrences(of: "&nbsp;", with: " ")
    }

    private static func collapseWhitespace(_ text: String) -> String {
        text
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

/// Windows 搜索片段规则：命中前 2 个字符开始，最多保留 query + 42 个字符。
enum WorkspacePlainTextSnippet {
    static func snippet(_ plainText: String, query: String) -> String? {
        guard !plainText.isEmpty, let range = plainText.range(of: query, options: .caseInsensitive) else {
            return nil
        }
        let index = plainText.distance(from: plainText.startIndex, to: range.lowerBound)
        let start = max(0, index - 2)
        let startIndex = plainText.index(plainText.startIndex, offsetBy: start)
        let maxLength = min(plainText.count - start, query.count + 42)
        let endIndex = plainText.index(startIndex, offsetBy: maxLength, limitedBy: plainText.endIndex) ?? plainText.endIndex
        return String(plainText[startIndex..<endIndex])
    }
}
