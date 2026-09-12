import Foundation

/// 工作区文档列表预览缓存：最多读取前 2 KB，按修改时间和长度失效。
final class WorkspacePreviewCache {
    private struct Fingerprint: Equatable {
        let modificationDate: Date
        let size: Int
    }

    private struct Entry {
        let fingerprint: Fingerprint
        let preview: String?
    }

    private let readLimit = 2 * 1024
    private var entries: [String: Entry] = [:]
    private let lock = NSLock()

    func preview(path: String, isMarkdown: Bool) -> String? {
        lock.lock()
        defer { lock.unlock() }

        let fm = FileManager.default
        guard let attributes = try? fm.attributesOfItem(atPath: path),
              let date = attributes[.modificationDate] as? Date,
              let sizeNumber = attributes[.size] as? NSNumber else { return nil }
        let size = sizeNumber.intValue
        let fingerprint = Fingerprint(modificationDate: date, size: size)
        if let cached = entries[path], cached.fingerprint == fingerprint {
            return cached.preview
        }

        let preview = readPreview(path: path, isMarkdown: isMarkdown, size: size)
        entries[path] = Entry(fingerprint: fingerprint, preview: preview)
        return preview
    }

    func invalidate(path: String) {
        lock.lock()
        entries[path] = nil
        lock.unlock()
    }

    func reset() {
        lock.lock()
        entries.removeAll()
        lock.unlock()
    }

    private func readPreview(path: String, isMarkdown: Bool, size: Int) -> String? {
        guard size > 0, let handle = FileHandle(forReadingAtPath: path) else { return nil }
        defer { try? handle.close() }
        let data = handle.readData(ofLength: readLimit)
        guard !data.isEmpty else { return nil }

        let source = Self.decode(data) ?? String(decoding: data, as: UTF8.self)
        let plainText = MarkdownPlainText.fromDocument(source, isMarkdown: isMarkdown)
        return plainText.isEmpty ? nil : plainText
    }

    private static func decode(_ data: Data) -> String? {
        let encoding = DocumentEncodingPolicy.detect(data: data)
        for length in stride(from: data.count, to: max(0, data.count - 4), by: -1) {
            if let text = DocumentEncodingPolicy.decode(data.prefix(length), using: encoding),
               !text.contains("\u{FFFD}") {
                return text
            }
        }
        return DocumentEncodingPolicy.decode(data, using: encoding)
    }
}
