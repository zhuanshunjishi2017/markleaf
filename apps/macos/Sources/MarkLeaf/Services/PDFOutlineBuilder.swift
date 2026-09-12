import Foundation
import PDFKit

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

        // A page with no extractable text still occupies its original PDF page index.
        let normalizedPages = (0..<document.pageCount).map { index in
            document.page(at: index)?.string.map(normalize)
        }
        // Rebuild a writable PDF before assigning destinations to its pages.
        let writableDocument = PDFDocument()
        for index in 0..<document.pageCount {
            guard let page = document.page(at: index) else { return nil }
            writableDocument.insert(page, at: writableDocument.pageCount)
        }
        let root = PDFOutline()
        var addedHeadings = 0
        var searchPage = 0
        var cursor: String.Index?

        for title in titles {
            let target = normalize(title)
            guard !target.isEmpty else { continue }
            var page = searchPage
            var location: Range<String.Index>?
            while page < normalizedPages.count {
                guard let text = normalizedPages[page] else {
                    page += 1
                    continue
                }
                let start = page == searchPage ? (cursor ?? text.startIndex) : text.startIndex
                if let range = text.range(of: target, options: [.caseInsensitive, .diacriticInsensitive], range: start..<text.endIndex) {
                    location = range
                    break
                }
                page += 1
            }
            guard let found = location, let pdfPage = writableDocument.page(at: page) else { continue }

            // Keep each title and its actual destination together. Unmatched titles
            // have no bookmark and never consume another title's page mapping.
            let outline = PDFOutline()
            outline.label = title
            outline.destination = PDFDestination(
                page: pdfPage,
                at: NSPoint(x: 0, y: pdfPage.bounds(for: .mediaBox).height)
            )
            root.insertChild(outline, at: root.numberOfChildren)
            addedHeadings += 1
            searchPage = page
            cursor = found.upperBound
        }
        guard addedHeadings > 0 else { return nil }
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
