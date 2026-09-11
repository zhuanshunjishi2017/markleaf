import Foundation
import PDFKit

/// PDF 导出大纲条目：页码由已生成 PDF 的文本流定位。
struct PDFHeading: Equatable {
    let level: Int
    let text: String
    let page: Int
}

enum PDFOutlineBuilder {
    static func headings(in html: String) -> [String] {
        let pattern = #"<h([1-6])(?:\s[^>]*)?>(.*?)</h\1>"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.dotMatchesLineSeparators]) else { return [] }
        let range = NSRange(html.startIndex..., in: html)

        func heading(from match: NSTextCheckingResult) -> String? {
            guard let textRange = Range(match.range(at: 2), in: html) else { return nil }
            return html[textRange]
                .replacingOccurrences(of: "<[^>]+>", with: " ", options: .regularExpression)
                .replacingOccurrences(of: "&amp;", with: "&")
                .replacingOccurrences(of: "&lt;", with: "<")
                .replacingOccurrences(of: "&gt;", with: ">")
                .replacingOccurrences(of: "&quot;", with: "\"")
                .replacingOccurrences(of: "&#39;", with: "'")
                .replacingOccurrences(of: "&nbsp;", with: " ")
                .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
                .trimmingCharacters(in: .whitespacesAndNewlines)
        }

        return regex.matches(in: html, options: [], range: range).compactMap(heading(from:)).filter { !$0.isEmpty }
    }

    static func addOutline(to data: Data, html: String) -> Data? {
        let titles = headings(in: html)
        guard !titles.isEmpty,
              let document = PDFDocument(data: data),
              document.pageCount > 0 else { return nil }

        let normalizedPages = (0..<document.pageCount).compactMap { index in
            document.page(at: index)?.string.map(normalize)
        }
        var searchPage = 0
        var headings: [PDFHeading] = []
        var cursor = normalizedPages.first.map { $0.startIndex }

        for title in titles {
            let target = normalize(title)
            var page = searchPage
            var location: String.Index?
            while page < normalizedPages.count {
                let text = normalizedPages[page]
                let start = page == searchPage ? (cursor ?? text.startIndex) : text.startIndex
                if let range = text.range(of: target, options: [.caseInsensitive, .diacriticInsensitive], range: start..<text.endIndex) {
                    location = range.lowerBound
                    break
                }
                page += 1
                cursor = nil
            }
            guard let found = location else { continue }
            headings.append(PDFHeading(level: 1, text: title, page: page + 1))
            searchPage = page
            cursor = found
        }

        guard !headings.isEmpty else { return nil }
        let writableDocument = PDFDocument()
        for index in 0..<document.pageCount {
            if let page = document.page(at: index) {
                writableDocument.insert(page, at: writableDocument.pageCount)
            }
        }

        let root = writableDocument.outlineRoot ?? PDFOutline()
        var stack: [(outline: PDFOutline, level: Int)] = []

        for heading in headings {
            let outline = PDFOutline()
            outline.label = heading.text
            if let page = writableDocument.page(at: max(0, heading.page - 1)) {
                outline.destination = PDFDestination(
                    page: page,
                    at: NSPoint(x: 0, y: page.bounds(for: .mediaBox).height)
                )
            }
            while let last = stack.last, last.level >= 1 {
                stack.removeLast()
            }
            root.insertChild(outline, at: max(0, root.numberOfChildren))
            stack.append((outline, 1))
        }
        writableDocument.outlineRoot = root
        return writableDocument.dataRepresentation()
    }

    private static func normalize(_ value: String) -> String {
        value
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .folding(options: [.caseInsensitive, .diacriticInsensitive], locale: nil)
    }
}
