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

    private var readLimit: Int { DocumentCoreRuntime.shared.require("previewReadLimit") }
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

        do {
            let text: String = try DocumentCoreRuntime.shared.call("preview", ["bytes": Array(data), "truncated": size > data.count, "isMarkdown": isMarkdown])
            return text.isEmpty ? nil : text
        } catch {
            AppLog.warning("Document preview failed for \(path): \(error.localizedDescription)")
            return nil
        }
    }
}
