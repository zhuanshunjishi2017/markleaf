import Foundation

enum QuickLookHTMLRenderer {
    static func document(title: String, markdown: String, isPlainText: Bool) -> String {
        let body = isPlainText
            ? "<pre class=\"plain-text\">\(escapeHTML(markdown))</pre>"
            : renderMarkdown(markdown)
        return page(title: escapeHTML(title), body: body)
    }

    static func errorPage(_ message: String) -> String {
        page(
            title: "Preview unavailable",
            body: "<main class=\"error\"><h1>无法预览</h1><p>\(escapeHTML(message))</p></main>"
        )
    }

    private static func page(title: String, body: String) -> String {
        """
        <!doctype html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'">
        <title>\(title)</title>
        <style>
        :root { color-scheme: light dark; font: 16px/1.65 -apple-system, BlinkMacSystemFont, "PingFang SC", "Hiragino Sans GB", sans-serif; color: #1d1d1f; background: #ffffff; }
        body { margin: 0; }
        main { max-width: 52rem; margin: 0 auto; padding: 2rem 1.25rem 3rem; }
        h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.8em 0 .65em; }
        h1 { font-size: 2em; } h2 { font-size: 1.55em; } h3 { font-size: 1.28em; }
        p { margin: .85em 0; } a { color: inherit; } code { font-family: ui-monospace, SF Mono, Menlo, monospace; font-size: .92em; background: rgba(120, 120, 128, .14); border-radius: 4px; padding: .1em .3em; }
        .math-inline { font-family: "SF Pro Math", "Cambria Math", "Times New Roman", serif; }
        .math-block { display: block; margin: 1rem 0; text-align: center; font-family: "SF Pro Math", "Cambria Math", "Times New Roman", serif; }
        pre { overflow: auto; background: rgba(120, 120, 128, .11); border-radius: 8px; padding: .9rem 1rem; }
        pre code { background: transparent; padding: 0; white-space: pre; }
        blockquote { margin: 1rem 0; padding: .2rem 1rem; color: #59595e; border-left: 3px solid rgba(120, 120, 128, .35); }
        table { width: 100%; border-collapse: collapse; margin: 1rem 0; }
        th, td { border: 1px solid rgba(120, 120, 128, .3); padding: .45rem .6rem; }
        img { max-width: 100%; height: auto; } .image-fallback { color: #77777c; }
        .task-state { display: inline-block; width: 1.2em; font-weight: 700; }
        .plain-text { overflow-wrap: anywhere; white-space: pre-wrap; }
        .error { text-align: center; padding-top: 20vh; }
        @media (prefers-color-scheme: dark) {
          :root { color: #f2f2f7; background: #1c1c1e; }
          blockquote { color: #b9b9bf; }
          code, pre { background: rgba(120, 120, 128, .22); }
          th, td { border-color: #48484a; }
        }
        </style>
        </head>
        <body><main>\(body)</main></body>
        </html>
        """
    }

    private static func escapeHTML(_ text: String) -> String {
        text.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
            .replacingOccurrences(of: "'", with: "&#39;")
    }

    private static func renderMarkdown(_ markdown: String) -> String {
        let lines = markdown.components(separatedBy: .newlines)
        var output = ""
        var index = 0

        while index < lines.count {
            let line = lines[index]
            let trimmed = line.trimmingCharacters(in: .whitespaces)

            if let fence = fenceLanguage(trimmed) {
                index += 1
                var code: [String] = []
                while index < lines.count, !isFenceClose(lines[index].trimmingCharacters(in: .whitespaces), opener: fence.opener) {
                    code.append(lines[index])
                    index += 1
                }
                index += 1
                let language = fence.language.isEmpty ? "" : " class=\"language-\(escapeHTML(fence.language))\""
                output += "<pre><code\(language)>\(escapeHTML(code.joined(separator: "\n")))</code></pre>"
                continue
            }

            if let heading = headingLine(trimmed) {
                output += "<h\(heading.level)>\(renderInline(heading.text))</h\(heading.level)>"
                index += 1
                continue
            }

            let nextContentIndex = (index + 1..<lines.count).first {
                !lines[$0].trimmingCharacters(in: .whitespaces).isEmpty
            }
            if let nextContentIndex,
               isTableRow(line),
               isTableSeparator(lines[nextContentIndex]) {
                let alignments = tableAlignments(lines[nextContentIndex])
                let headerCells = tableCells(line)
                index = nextContentIndex + 1
                var rows: [[String]] = [headerCells]
                while index < lines.count {
                    let current = lines[index].trimmingCharacters(in: .whitespaces)
                    if isTableRow(current) {
                        rows.append(tableCells(lines[index]))
                        index += 1
                        continue
                    }
                    guard current.isEmpty else { break }

                    // MarkLeaf tolerates blank lines between visual table rows.
                    // Quick Look should project that same editing model while
                    // still stopping cleanly when non-table prose follows.
                    var lookahead = index + 1
                    while lookahead < lines.count,
                          lines[lookahead].trimmingCharacters(in: .whitespaces).isEmpty {
                        lookahead += 1
                    }
                    guard lookahead < lines.count, isTableRow(lines[lookahead]) else { break }
                    // A row immediately followed by a separator starts a new
                    // table, so the current table must not absorb it.
                    if lookahead + 1 < lines.count,
                       isTableSeparator(lines[lookahead + 1]) {
                        break
                    }
                    index = lookahead
                }

                let header = rows[0].enumerated().map { offset, cell -> String in
                    let alignment = offset < alignments.count ? alignments[offset] : ""
                    return "<th\(alignment)>\(renderInline(cell))</th>"
                }.joined()
                let bodyRows = rows.dropFirst().map { cells -> String in
                    "<tr>" + cells.map { "<td>\(renderInline($0))</td>" }.joined() + "</tr>"
                }.joined()
                output += "<table><thead><tr>\(header)</tr></thead><tbody>\(bodyRows)</tbody></table>"
                continue
            }

            if let list = listLine(trimmed) {
                var items: [String] = []
                let ordered = list.ordered
                while index < lines.count, let next = listLine(lines[index].trimmingCharacters(in: .whitespaces)), next.ordered == ordered {
                    items.append(next.content)
                    index += 1
                }
                let rendered = items.map { item -> String in
                    if let checked = taskState(item) {
                        let content = item.dropFirst(3).trimmingCharacters(in: .whitespaces)
                        let state = checked ? "✓" : "○"
                        return "<li class=\"task-item\" data-checked=\"\(checked)\"><span class=\"task-state\">\(state)</span>\(renderInline(String(content)))</li>"
                    }
                    return "<li>\(renderInline(item))</li>"
                }
                output += ordered ? "<ol>\(rendered.joined())</ol>" : "<ul>\(rendered.joined())</ul>"
                continue
            }

            if trimmed.hasPrefix(">") {
                var quotes: [String] = []
                while index < lines.count, lines[index].trimmingCharacters(in: .whitespaces).hasPrefix(">") {
                    quotes.append(String(lines[index].trimmingCharacters(in: .whitespaces).dropFirst().dropFirst()))
                    index += 1
                }
                output += "<blockquote>\(renderInline(quotes.joined(separator: " ")))</blockquote>"
                continue
            }

            if trimmed.isEmpty {
                index += 1
                continue
            }

            var paragraph: [String] = []
            while index < lines.count, !lines[index].trimmingCharacters(in: .whitespaces).isEmpty {
                let current = lines[index].trimmingCharacters(in: .whitespaces)
                if current.hasPrefix("```") || current.hasPrefix("~~~") || headingLine(current) != nil || listLine(current) != nil { break }
                paragraph.append(current)
                index += 1
            }
            output += "<p>\(paragraph.map { renderInline($0) }.joined(separator: "<br>"))</p>"
        }
        return output
    }

    private static func renderInline(_ source: String) -> String {
        var codeStore = PlaceholderStore()
        var text = codeStore.replaceMatches(in: source, pattern: "`([^`]+)`") { groups in
            "<code>\(escapeHTML(groups[0]))</code>"
        }

        text = escapeHTML(text)
        // The Quick Look extension is sandboxed to the previewed file only.
        // Do not attempt sibling-image reads; keep deterministic alt-text
        // fallbacks and avoid broad filesystem exceptions.
        text = text.replacingMatches(pattern: "!\\[([^\\]]*)\\]\\(([^)]+)\\)") { groups in
            "<span class=\"image-fallback\">\(groups[0])</span>"
        }
        text = text.replacingMatches(pattern: "\\[([^\\]]+)\\]\\(([^)]+)\\)") { groups in
            let scheme = URL(string: groups[1])?.scheme?.lowercased()
            let inert = scheme == "http" || scheme == "https" || scheme == "mailto"
            return "<span class=\"link-text\"\(inert ? " title=\"\(escapeHTML(groups[1]))\"" : "")>\(groups[0])</span>"
        }
        text = text.replacingMatches(pattern: "\\$\\$([^$]+)\\$\\$") { "<span class=\"math-block\">\(mathHTML($0[0]))</span>" }
        text = text.replacingMatches(pattern: "(?<!\\$)\\$([^$\\n]+)\\$(?!\\$)") { "<span class=\"math-inline\">\(mathHTML($0[0]))</span>" }
        text = text.replacingMatches(pattern: "\\*\\*([^*]+)\\*\\*") { "<strong>\($0[0])</strong>" }
        text = text.replacingMatches(pattern: "__([^_]+)__") { "<strong>\($0[0])</strong>" }
        text = text.replacingMatches(pattern: "(?<!\\*)\\*([^*]+)\\*(?!\\*)") { "<em>\($0[0])</em>" }
        text = text.replacingMatches(pattern: "(?<!_)_([^_]+)_(?!_)") { "<em>\($0[0])</em>" }
        text = text.replacingMatches(pattern: "~~([^~]+)~~") { "<del>\($0[0])</del>" }
        return codeStore.restore(text)
    }

    private static func mathHTML(_ latex: String) -> String {
        let text = latex.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return "..." }

        var result = text
        result = result.replacingMatches(pattern: "\\\\frac\\{([^{}]+)\\}\\{([^{}]+)\\}") { groups in
            "<span class=\"math-fraction\"><span>\(mathHTML(groups[0]))</span><span>\(mathHTML(groups[1]))</span></span>"
        }
        result = result.replacingMatches(pattern: "\\^\\{([^{}]+)\\}") { "<sup>\(mathHTML($0[0]))</sup>" }
        result = result.replacingMatches(pattern: "\\^([^\\s{}])") { "<sup>\($0[0])</sup>" }
        result = result.replacingMatches(pattern: "_\\{([^{}]+)\\}") { "<sub>\(mathHTML($0[0]))</sub>" }
        result = result.replacingMatches(pattern: "_([^\\s{}])") { "<sub>\($0[0])</sub>" }

        let symbols = [
            "alpha": "α", "beta": "β", "gamma": "γ", "delta": "δ",
            "epsilon": "ε", "theta": "θ", "lambda": "λ", "mu": "μ",
            "pi": "π", "rho": "ρ", "sigma": "σ", "phi": "φ",
            "omega": "ω", "Gamma": "Γ", "Delta": "Δ", "Theta": "Θ",
            "Lambda": "Λ", "Pi": "Π", "Sigma": "Σ", "Phi": "Φ",
            "Omega": "Ω", "times": "×", "cdot": "⋅", "div": "÷",
            "pm": "±", "leq": "≤", "geq": "≥", "neq": "≠",
            "approx": "≈", "infty": "∞", "sum": "∑", "prod": "∏",
            "int": "∫", "sqrt": "√", "left": "", "right": "",
        ]
        result = result.replacingMatches(pattern: "\\\\([A-Za-z]+)") { groups in
            symbols[groups[0]] ?? groups[0]
        }
        result = result
            .replacingOccurrences(of: "{", with: "")
            .replacingOccurrences(of: "}", with: "")
        return result
    }

    private static func headingLine(_ line: String) -> (level: Int, text: String)? {
        guard let match = firstMatch(line, pattern: "^ {0,3}(#{1,6})\\s+(.+)$") else { return nil }
        return (match[0].count, match[1])
    }

    private static func isFenceClose(_ line: String, opener: String) -> Bool {
        line.allSatisfy { $0 == opener.first || $0.isWhitespace } && line.contains(String(opener.first ?? "`"))
    }

    private static func fenceLanguage(_ line: String) -> (opener: String, language: String)? {
        guard let match = firstMatch(line, pattern: "^ {0,3}(```+|~~~+)\\s*([A-Za-z0-9_+-]*)\\s*$") else { return nil }
        return (match[0], match[1])
    }

    private static func listLine(_ line: String) -> (ordered: Bool, content: String)? {
        if let match = firstMatch(line, pattern: "^ {0,3}(?:[-*+]|([0-9]+)[.)])\\s+(.+)$") {
            return (match.count > 1, match.count > 1 ? match[1] : match[0])
        }
        return nil
    }

    private static func taskState(_ content: String) -> Bool? {
        if content.hasPrefix("[x] ") || content.hasPrefix("[X] ") { return true }
        if content.hasPrefix("[ ] ") { return false }
        return nil
    }

    private static func isTableRow(_ line: String) -> Bool {
        let trimmed = line.trimmingCharacters(in: .whitespaces)
        return trimmed.hasPrefix("|") && trimmed.hasSuffix("|")
    }

    private static func isTableSeparator(_ line: String) -> Bool {
        let range = line.range(of: "^[ :\\-|]+$", options: .regularExpression)
        return range != nil && line.contains("-")
    }

    private static func tableCells(_ line: String) -> [String] {
        line.trimmingCharacters(in: .whitespaces)
            .dropFirst().dropLast()
            .split(separator: "|", omittingEmptySubsequences: false)
            .map { $0.trimmingCharacters(in: .whitespaces) }
    }

    private static func tableAlignments(_ separator: String) -> [String] {
        tableCells(separator).map { cell in
            if cell.hasPrefix(":") && cell.hasSuffix(":") { return " style=\"text-align:center\"" }
            if cell.hasSuffix(":") { return " style=\"text-align:right\"" }
            return ""
        }
    }

    private static func firstMatch(_ text: String, pattern: String) -> [String]? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) else { return nil }
        return (1..<match.numberOfRanges).compactMap { range in
            Range(match.range(at: range), in: text).map { String(text[$0]) }
        }
    }
}

private struct PlaceholderStore {
    private struct Replacement {
        let token: String
        let html: String
    }

    private var replacements: [Replacement] = []

    mutating func replaceMatches(in input: String, pattern: String, transform: ([String]) -> String) -> String {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return input }
        let range = NSRange(input.startIndex..., in: input)
        let matches = regex.matches(in: input, range: range)
        guard !matches.isEmpty else { return input }

        var result = ""
        var cursor = input.startIndex
        for match in matches {
            guard let matchRange = Range(match.range, in: input) else { continue }
            let groups = (1..<match.numberOfRanges).compactMap { range in
                Range(match.range(at: range), in: input).map { String(input[$0]) }
            }
            result += input[cursor..<matchRange.lowerBound]
            let token = "\u{0}markleaf-\(replacements.count)\u{0}"
            result += token
            replacements.append(Replacement(token: token, html: transform(groups)))
            cursor = matchRange.upperBound
        }
        result += input[cursor...]
        return result
    }

    func restore(_ text: String) -> String {
        replacements.reduce(text) { partial, replacement in
            partial.replacingOccurrences(of: replacement.token, with: replacement.html)
        }
    }
}

private extension String {
    func replacingMatches(pattern: String, transform: ([String]) -> String) -> String {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return self }
        let matches = regex.matches(in: self, range: NSRange(startIndex..., in: self))
        guard !matches.isEmpty else { return self }

        var result = ""
        var cursor = startIndex
        for match in matches {
            guard let matchRange = Range(match.range, in: self) else { continue }
            let groups = (1..<match.numberOfRanges).compactMap { range in
                Range(match.range(at: range), in: self).map { String(self[$0]) }
            }
            result += self[cursor..<matchRange.lowerBound] + transform(groups)
            cursor = matchRange.upperBound
        }
        result += self[cursor...]
        return result
    }
}
