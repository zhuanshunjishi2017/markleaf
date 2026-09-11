import Foundation

struct RecoverySnapshot {
    let documentId: String
    let documentPath: String?
    let markdown: String
    let revision: Int64
    let timestamp: Date
    let displayName: String?
}

/// Filesystem adapter for the shared recovery envelope. A snapshot is one atomic file.
final class RecoveryService {
    static let shared = RecoveryService()
    private let processId = Int(ProcessInfo.processInfo.processIdentifier)
    private let lock = NSLock()
    private let fm = FileManager.default

    private var recoveryDirectory: URL {
        let base = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first ?? fm.homeDirectoryForCurrentUser
        return base.appendingPathComponent("MarkLeaf/Recovery", isDirectory: true)
    }

    @discardableResult
    func writeSnapshot(documentId: String, path: String?, markdown: String, revision: Int64, displayName: String?) -> Bool {
        lock.lock()
        defer { lock.unlock() }
        do {
            let content: String = try DocumentCoreRuntime.shared.call("serializeRecovery", [
                "documentId": documentId, "documentPath": path as Any? ?? NSNull(), "markdown": markdown,
                "revision": String(revision), "timestamp": ISO8601DateFormatter().string(from: Date()),
                "displayName": displayName as Any? ?? NSNull()
            ])
            let name: String = try DocumentCoreRuntime.shared.call("recoveryFileName", ["owner": processId, "documentId": documentId])
            try fm.createDirectory(at: recoveryDirectory, withIntermediateDirectories: true)
            try content.write(to: recoveryDirectory.appendingPathComponent(name), atomically: true, encoding: .utf8)
            AppLog.info("恢复快照已保存: \(displayName ?? "未命名")")
            return true
        } catch {
            AppLog.warning("恢复快照写入失败: \(error.localizedDescription)")
            return false
        }
    }

    func delete(documentId: String) { deleteMatching(["documentId": documentId]) }
    func deleteOwnFiles() { deleteMatching(["owner": processId]) }

    private func deleteMatching(_ identity: [String: Any]) {
        lock.lock()
        defer { lock.unlock() }
        do {
            guard fm.fileExists(atPath: recoveryDirectory.path) else { return }
            for file in try fm.contentsOfDirectory(at: recoveryDirectory, includingPropertiesForKeys: nil) {
                var request = identity
                request["name"] = file.lastPathComponent
                let matches: Bool = try DocumentCoreRuntime.shared.call("ownsRecoveryFile", request)
                if matches { try fm.removeItem(at: file) }
            }
        } catch { AppLog.warning("恢复快照清理失败: \(error.localizedDescription)") }
    }

    static func pendingRecoveries() -> [RecoverySnapshot] {
        let service = shared
        service.lock.lock()
        defer { service.lock.unlock() }
        guard service.fm.fileExists(atPath: service.recoveryDirectory.path) else { return [] }
        do {
            let files = try service.fm.contentsOfDirectory(at: service.recoveryDirectory, includingPropertiesForKeys: nil)
            var records: [KernelRecovery] = []
            for file in files where file.lastPathComponent.hasSuffix(".recovery.json") || file.pathExtension == "meta" {
                do {
                    let owned: Bool = try DocumentCoreRuntime.shared.call("ownsRecoveryFile", ["name": file.lastPathComponent])
                    guard owned else { continue }
                    var input: [String: Any] = ["content": try String(contentsOf: file, encoding: .utf8)]
                    if file.pathExtension == "meta" {
                        input["legacyMarkdown"] = try String(contentsOf: file.deletingPathExtension(), encoding: .utf8)
                    }
                    let value: KernelRecovery = try DocumentCoreRuntime.shared.call("parseRecovery", input)
                    records.append(value)
                } catch { AppLog.warning("恢复快照读取失败 \(file.lastPathComponent): \(error.localizedDescription)") }
            }
            let data = try JSONEncoder().encode(records)
            let values = try JSONSerialization.jsonObject(with: data)
            let selected: [KernelRecovery] = try DocumentCoreRuntime.shared.call("selectRecoveries", ["records": values])
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            return try selected.map { value in
                guard let timestamp = formatter.date(from: value.timestamp), let revision = Int64(value.revision) else {
                    throw DocumentKernelFailure(code: "invalid_recovery", message: "Invalid recovery timestamp or revision")
                }
                return RecoverySnapshot(documentId: value.documentId, documentPath: value.documentPath, markdown: value.markdown,
                    revision: revision, timestamp: timestamp, displayName: value.displayName)
            }
        } catch {
            AppLog.warning("恢复目录读取失败: \(error.localizedDescription)")
            return []
        }
    }
    static func discardAll() { shared.deleteMatching([:]) }
}
