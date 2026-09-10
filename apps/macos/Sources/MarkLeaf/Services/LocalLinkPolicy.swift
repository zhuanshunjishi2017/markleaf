import Foundation

enum LocalLinkPolicy {
    static func isOpenableFile(_ path: String) -> Bool {
        (try? URL(fileURLWithPath: path).resourceValues(forKeys: [.isRegularFileKey]))?.isRegularFile == true
    }

    static func resolve(_ value: String, documentPath: String?) -> String? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        if let url = URL(string: trimmed), let scheme = url.scheme?.lowercased(),
           ["http", "https", "mailto"].contains(scheme) { return nil }
        if trimmed.lowercased().hasPrefix("file:") {
            guard let url = URL(string: trimmed), url.isFileURL else { return nil }
            return url.standardizedFileURL.path
        }
        let decoded = trimmed.removingPercentEncoding ?? trimmed
        if decoded.hasPrefix("/") { return URL(fileURLWithPath: decoded).standardizedFileURL.path }
        guard let documentPath else { return nil }
        let base = URL(fileURLWithPath: documentPath).deletingLastPathComponent()
        return base.appendingPathComponent(decoded).standardizedFileURL.path
    }
}
