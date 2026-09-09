import Foundation

enum ExportLocalImageIssueReason: Equatable {
    case missingBaseURL
    case missing
    case unsupportedType
    case unreadable
    case malformed
    case unsupportedSource
}

struct ExportLocalImageIssue: Equatable {
    let source: String
    let reason: ExportLocalImageIssueReason
}

struct ExportLocalImageEmbeddingResult: Equatable {
    let html: String
    let embeddedCount: Int
    let issues: [ExportLocalImageIssue]

    var unresolvedCount: Int { issues.count }
}

/// Makes generated export HTML self-contained without fetching network resources.
///
/// The scanner deliberately handles only real `img` start tags and their `src`
/// attribute. It leaves all other markup intact, including script/style contents.
enum ExportLocalImageEmbedder {
    private enum Resolution {
        case preserved
        case local(URL)
        case issue(ExportLocalImageIssueReason)
    }

    private static let mimeTypes: [String: String] = [
        "png": "image/png",
        "jpg": "image/jpeg",
        "jpeg": "image/jpeg",
        "gif": "image/gif",
        "webp": "image/webp",
        "bmp": "image/bmp",
    ]

    static func embed(in html: String, documentURL: URL?) -> ExportLocalImageEmbeddingResult {
        var output = ""
        var cursor = html.startIndex
        var embeddedCount = 0
        var issues: [ExportLocalImageIssue] = []

        while cursor < html.endIndex,
              let opening = html[cursor...].firstIndex(of: "<") {
            output += html[cursor..<opening]

            if html[opening...].hasPrefix("<!--") {
                guard let end = html[opening...].range(of: "-->")?.upperBound else {
                    output += html[opening...]
                    cursor = html.endIndex
                    break
                }
                output += html[opening..<end]
                cursor = end
                continue
            }

            guard let tagEnd = endOfTag(in: html, startingAt: opening) else {
                output += html[opening...]
                cursor = html.endIndex
                break
            }
            let afterTag = html.index(after: tagEnd)
            let tag = String(html[opening..<afterTag])
            let name = tagName(in: tag)

            if let name, (name == "script" || name == "style") {
                let closingPrefix = "</\(name)"
                if let closingStart = html[afterTag...].range(
                    of: closingPrefix,
                    options: [.caseInsensitive]
                )?.lowerBound,
                   let closingEnd = endOfTag(in: html, startingAt: closingStart) {
                    let end = html.index(after: closingEnd)
                    output += html[opening..<end]
                    cursor = end
                } else {
                    output += html[opening...]
                    cursor = html.endIndex
                }
                continue
            }

            if name == "img" {
                let transformed = transformImageTag(tag, documentURL: documentURL)
                output += transformed.tag
                embeddedCount += transformed.embedded ? 1 : 0
                if let issue = transformed.issue { issues.append(issue) }
            } else {
                output += tag
            }
            cursor = afterTag
        }

        if cursor < html.endIndex { output += html[cursor...] }
        return ExportLocalImageEmbeddingResult(
            html: output,
            embeddedCount: embeddedCount,
            issues: issues
        )
    }

    private static func transformImageTag(
        _ tag: String,
        documentURL: URL?
    ) -> (tag: String, embedded: Bool, issue: ExportLocalImageIssue?) {
        guard let valueRange = sourceValueRange(in: tag) else {
            return (tag, false, nil)
        }
        let source = String(tag[valueRange])
        guard !source.isEmpty else {
            return (tag, false, ExportLocalImageIssue(source: source, reason: .malformed))
        }

        switch resolve(source: source, documentURL: documentURL) {
        case .preserved:
            return (tag, false, nil)
        case .issue(let reason):
            return (tag, false, ExportLocalImageIssue(source: source, reason: reason))
        case .local(let url):
            let ext = url.pathExtension.lowercased()
            guard let mime = mimeTypes[ext] else {
                return (tag, false, ExportLocalImageIssue(source: source, reason: .unsupportedType))
            }
            guard FileManager.default.fileExists(atPath: url.path) else {
                return (tag, false, ExportLocalImageIssue(source: source, reason: .missing))
            }
            guard (try? url.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true,
                  let data = try? Data(contentsOf: url) else {
                return (tag, false, ExportLocalImageIssue(source: source, reason: .unreadable))
            }
            let dataURI = "data:\(mime);base64,\(data.base64EncodedString())"
            var rewritten = tag
            rewritten.replaceSubrange(valueRange, with: dataURI)
            return (rewritten, true, nil)
        }
    }

    private static func resolve(source: String, documentURL: URL?) -> Resolution {
        let value = decodeHTMLEntities(source)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let lower = value.lowercased()
        if lower.hasPrefix("data:") { return .preserved }

        if let virtualPath = virtualImagePath(from: value) {
            return localResolution(path: virtualPath, documentURL: documentURL)
        }
        if lower.hasPrefix("http://") || lower.hasPrefix("https://") {
            return .preserved
        }
        if lower.hasPrefix("file:") {
            guard let url = URL(string: value), url.isFileURL else { return .issue(.malformed) }
            return .local(url.standardizedFileURL)
        }
        if hasURLScheme(value) { return .issue(.unsupportedSource) }
        return localResolution(path: value, documentURL: documentURL)
    }

    private static func localResolution(path: String, documentURL: URL?) -> Resolution {
        let pathWithoutSuffix = path.split(separator: "?", maxSplits: 1, omittingEmptySubsequences: false)[0]
            .split(separator: "#", maxSplits: 1, omittingEmptySubsequences: false)[0]
        let rawPath = String(pathWithoutSuffix)
        let decodedPath = rawPath.removingPercentEncoding ?? rawPath
        guard !decodedPath.isEmpty else { return .issue(.malformed) }
        if decodedPath.hasPrefix("/") {
            return .local(URL(fileURLWithPath: decodedPath).standardizedFileURL)
        }
        guard let documentURL else { return .issue(.missingBaseURL) }
        return .local(
            documentURL.deletingLastPathComponent()
                .appendingPathComponent(decodedPath)
                .standardizedFileURL
        )
    }

    private static func virtualImagePath(from value: String) -> String? {
        guard let schemeEnd = value.range(of: "://")?.upperBound else { return nil }
        let remainder = value[schemeEnd...]
        guard let pathStart = remainder.firstIndex(of: "/") else { return nil }
        let host = remainder[..<pathStart].lowercased()
        guard host == "assets.local" || host == "markleaf-asset" else { return nil }
        let pathAndQuery = remainder[pathStart...]
        guard pathAndQuery.lowercased().hasPrefix("/image?"),
              let queryStart = pathAndQuery.firstIndex(of: "?") else { return nil }
        let query = pathAndQuery[pathAndQuery.index(after: queryStart)...]
        for item in query.split(separator: "&", omittingEmptySubsequences: false) {
            let pair = item.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
            let name = String(pair[0]).removingPercentEncoding ?? String(pair[0])
            if name == "path", pair.count == 2 {
                return String(pair[1])
            }
        }
        return nil
    }

    private static func hasURLScheme(_ value: String) -> Bool {
        guard let colon = value.firstIndex(of: ":"), colon != value.startIndex else { return false }
        let scheme = value[..<colon]
        guard scheme.first?.isLetter == true else { return false }
        return scheme.dropFirst().allSatisfy { $0.isLetter || $0.isNumber || $0 == "+" || $0 == "-" || $0 == "." }
    }

    private static func endOfTag(in html: String, startingAt opening: String.Index) -> String.Index? {
        var index = html.index(after: opening)
        var quote: Character?
        while index < html.endIndex {
            let character = html[index]
            if let currentQuote = quote {
                if character == currentQuote { quote = nil }
            } else if character == "\"" || character == "'" {
                quote = character
            } else if character == ">" {
                return index
            }
            index = html.index(after: index)
        }
        return nil
    }

    private static func tagName(in tag: String) -> String? {
        guard tag.first == "<" else { return nil }
        var index = tag.index(after: tag.startIndex)
        while index < tag.endIndex, tag[index].isWhitespace { index = tag.index(after: index) }
        guard index < tag.endIndex, tag[index] != "/", tag[index] != "!", tag[index] != "?" else { return nil }
        let start = index
        while index < tag.endIndex, tag[index].isLetter || tag[index].isNumber {
            index = tag.index(after: index)
        }
        guard start != index else { return nil }
        return tag[start..<index].lowercased()
    }

    private static func sourceValueRange(in tag: String) -> Range<String.Index>? {
        guard tagName(in: tag) == "img" else { return nil }
        var index = tag.index(after: tag.startIndex)
        while index < tag.endIndex, tag[index].isWhitespace { index = tag.index(after: index) }
        while index < tag.endIndex, tag[index].isLetter || tag[index].isNumber {
            index = tag.index(after: index)
        }

        while index < tag.endIndex {
            while index < tag.endIndex, tag[index].isWhitespace { index = tag.index(after: index) }
            guard index < tag.endIndex, tag[index] != ">", tag[index] != "/" else { return nil }
            let nameStart = index
            while index < tag.endIndex,
                  !tag[index].isWhitespace,
                  tag[index] != "=", tag[index] != ">", tag[index] != "/" {
                index = tag.index(after: index)
            }
            let name = tag[nameStart..<index].lowercased()
            while index < tag.endIndex, tag[index].isWhitespace { index = tag.index(after: index) }
            guard index < tag.endIndex, tag[index] == "=" else {
                if name == "src" { return index..<index }
                continue
            }
            index = tag.index(after: index)
            while index < tag.endIndex, tag[index].isWhitespace { index = tag.index(after: index) }
            guard index < tag.endIndex else { return name == "src" ? index..<index : nil }

            let valueRange: Range<String.Index>
            if tag[index] == "\"" || tag[index] == "'" {
                let quote = tag[index]
                let valueStart = tag.index(after: index)
                guard let end = tag[valueStart...].firstIndex(of: quote) else {
                    return name == "src" ? valueStart..<tag.endIndex : nil
                }
                valueRange = valueStart..<end
                index = tag.index(after: end)
            } else {
                let valueStart = index
                while index < tag.endIndex, !tag[index].isWhitespace, tag[index] != ">" {
                    index = tag.index(after: index)
                }
                valueRange = valueStart..<index
            }
            if name == "src" { return valueRange }
        }
        return nil
    }

    private static func decodeHTMLEntities(_ value: String) -> String {
        var output = ""
        var cursor = value.startIndex
        while cursor < value.endIndex, let ampersand = value[cursor...].firstIndex(of: "&") {
            output += value[cursor..<ampersand]
            guard let semicolon = value[ampersand...].firstIndex(of: ";") else {
                output += value[ampersand...]
                return output
            }
            let entityStart = value.index(after: ampersand)
            let entity = String(value[entityStart..<semicolon])
            if let decoded = decodedEntity(entity) {
                output.append(decoded)
            } else {
                output += value[ampersand...semicolon]
            }
            cursor = value.index(after: semicolon)
        }
        if cursor < value.endIndex { output += value[cursor...] }
        return output
    }

    private static func decodedEntity(_ entity: String) -> Character? {
        switch entity.lowercased() {
        case "amp": return "&"
        case "quot": return "\""
        case "apos", "#39": return "'"
        case "lt": return "<"
        case "gt": return ">"
        default:
            let number: UInt32?
            if entity.lowercased().hasPrefix("#x") {
                number = UInt32(entity.dropFirst(2), radix: 16)
            } else if entity.hasPrefix("#") {
                number = UInt32(entity.dropFirst())
            } else {
                number = nil
            }
            guard let number, let scalar = UnicodeScalar(number) else { return nil }
            return Character(scalar)
        }
    }
}
