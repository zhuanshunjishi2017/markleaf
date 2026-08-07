import Foundation

/// 崩溃恢复快照（对应 C# RecoverySnapshot）。
struct RecoverySnapshot {
    let documentId: String
    let documentPath: String?
    let markdown: String
    let revision: Int64
    let timestamp: Date
    let displayName: String?
}

/// 崩溃恢复服务（对应 C# RecoveryService）：
/// 周期把脏文档写入 <Application Support>/MarkLeaf/Recovery/doc-{pid}-{docId}.md + .meta。
/// 正常退出时删除本进程快照；崩溃遗留的快照在下一次启动时提示恢复。
final class RecoveryService {
    static let shared = RecoveryService()

    private let processId = ProcessInfo.processInfo.processIdentifier
    private let fm = FileManager.default

    private var recoveryDirectory: URL {
        let base = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? fm.homeDirectoryForCurrentUser
        return base.appendingPathComponent("MarkLeaf/Recovery", isDirectory: true)
    }

    private func dataPath(for documentId: String) -> URL {
        recoveryDirectory.appendingPathComponent("doc-\(processId)-\(documentId).md")
    }

    private func metaPath(for documentId: String) -> URL {
        recoveryDirectory.appendingPathComponent("doc-\(processId)-\(documentId).md.meta")
    }

    // MARK: - 写入 / 删除

    func writeSnapshot(documentId: String, path: String?, markdown: String, revision: Int64, displayName: String?) {
        do {
            try fm.createDirectory(at: recoveryDirectory, withIntermediateDirectories: true)
            let dataURL = dataPath(for: documentId)
            let metaURL = metaPath(for: documentId)
            try markdown.write(to: dataURL, atomically: true, encoding: .utf8)

            let meta: [String: Any] = [
                "documentId": documentId,
                "documentPath": path ?? NSNull(),
                "revision": revision,
                "timestamp": ISO8601DateFormatter().string(from: Date()),
                "displayName": displayName ?? NSNull(),
            ]
            let json = try JSONSerialization.data(withJSONObject: meta, options: [.prettyPrinted, .sortedKeys])
            try json.write(to: metaURL, options: .atomic)
            AppLog.info("恢复快照已保存: \(displayName ?? "未命名")")
        } catch {
            AppLog.warning("恢复快照写入失败: \(error.localizedDescription)")
        }
    }

    func delete(documentId: String) {
        try? fm.removeItem(at: dataPath(for: documentId))
        try? fm.removeItem(at: metaPath(for: documentId))
    }

    /// 正常退出时清理本进程快照。
    func deleteOwnFiles() {
        guard fm.fileExists(atPath: recoveryDirectory.path) else { return }
        let prefix = "doc-\(processId)-"
        guard let files = try? fm.contentsOfDirectory(at: recoveryDirectory, includingPropertiesForKeys: nil) else { return }
        for file in files where file.lastPathComponent.hasPrefix(prefix) {
            try? fm.removeItem(at: file)
        }
        AppLog.info("已清理本进程恢复快照")
    }

    // MARK: - 枚举（跨进程，崩溃遗留）

    static func pendingRecoveries() -> [RecoverySnapshot] {
        let fm = FileManager.default
        let base = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? fm.homeDirectoryForCurrentUser
        let dir = base.appendingPathComponent("MarkLeaf/Recovery", isDirectory: true)
        guard fm.fileExists(atPath: dir.path),
              let metaFiles = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)
                .filter({ $0.lastPathComponent.hasSuffix(".meta") }) else {
            return []
        }

        var result: [RecoverySnapshot] = []
        for metaFile in metaFiles {
            let dataURL = metaFile.deletingPathExtension() // doc-x-y.md.meta → doc-x-y.md
            guard fm.fileExists(atPath: dataURL.path),
                  let metaData = try? Data(contentsOf: metaFile),
                  let meta = try? JSONSerialization.jsonObject(with: metaData) as? [String: Any],
                  let documentId = meta["documentId"] as? String else { continue }

            guard let markdown = try? String(contentsOf: dataURL, encoding: .utf8), !markdown.isEmpty else { continue }

            let timestamp = (meta["timestamp"] as? String).flatMap { ISO8601DateFormatter().date(from: $0) } ?? Date()
            result.append(RecoverySnapshot(
                documentId: documentId,
                documentPath: meta["documentPath"] as? String,
                markdown: markdown,
                revision: (meta["revision"] as? NSNumber)?.int64Value ?? 0,
                timestamp: timestamp,
                displayName: meta["displayName"] as? String))
        }
        return result.sorted { $0.timestamp > $1.timestamp }
    }

    static func discardAll() {
        let fm = FileManager.default
        let base = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? fm.homeDirectoryForCurrentUser
        let dir = base.appendingPathComponent("MarkLeaf/Recovery", isDirectory: true)
        if let files = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) {
            for file in files {
                try? fm.removeItem(at: file)
            }
        }
    }
}
