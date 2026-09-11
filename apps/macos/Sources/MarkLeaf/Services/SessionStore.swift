import Foundation

final class SessionStore {
    static let shared = SessionStore(rootDirectory: nil)
    private let rootDirectory: URL?
    private let fileManager = FileManager.default

    init(rootDirectory: URL? = nil) { self.rootDirectory = rootDirectory }

    private var sessionDirectory: URL {
        let base = rootDirectory ?? fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first ?? fileManager.homeDirectoryForCurrentUser
        let dir = base.appendingPathComponent("MarkLeaf/Session", isDirectory: true)
        try? fileManager.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }
    private var manifestURL: URL { sessionDirectory.appendingPathComponent("manifest.json") }
    private var previousManifestURL: URL { sessionDirectory.appendingPathComponent("manifest.prev.json") }
    private var snapshotsDirectory: URL {
        let dir = sessionDirectory.appendingPathComponent("snapshots", isDirectory: true)
        try? fileManager.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    @discardableResult
    func writeSnapshot(tabID: String, content: String, revision: Int64, encoding: String, newLine: String) throws -> String {
        let fileName = "snap-\(tabID).recovery.json"
        let content: String = try DocumentCoreRuntime.shared.call("serializeRecovery", [
            "documentId": tabID, "markdown": content, "revision": String(revision),
            "encoding": encoding, "newLine": newLine, "timestamp": ISO8601DateFormatter().string(from: Date())
        ])
        try content.write(to: snapshotsDirectory.appendingPathComponent(fileName), atomically: true, encoding: .utf8)
        return fileName
    }

    func readSnapshot(fileName: String) -> String? {
        guard fileName.hasPrefix("snap-"), !fileName.contains("/"), !fileName.contains("\\") else { return nil }
        do {
            let content = try String(contentsOf: snapshotsDirectory.appendingPathComponent(fileName), encoding: .utf8)
            if fileName.hasSuffix(".md") { return content } // Existing session manifests reference legacy UTF-8 snapshots.
            guard fileName.hasSuffix(".recovery.json") else { return nil }
            let snapshot: KernelRecovery = try DocumentCoreRuntime.shared.call("parseRecovery", ["content": content])
            return snapshot.markdown
        } catch {
            AppLog.warning("会话快照读取失败: \(error.localizedDescription)")
            return nil
        }
    }

    func commit(manifest: SessionManifest) throws {
        let data = try SessionManifestCodec.encode(manifest)
        if fileManager.fileExists(atPath: manifestURL.path) {
            try? fileManager.removeItem(at: previousManifestURL)
            try fileManager.moveItem(at: manifestURL, to: previousManifestURL)
        }
        try data.write(to: manifestURL, options: .atomic)
    }

    struct LoadResult {
        let manifest: SessionManifest?
        let fellBack: Bool
        let diagnostics: [String]
    }

    func loadLatest() -> LoadResult {
        var diagnostics: [String] = []
        if let data = try? Data(contentsOf: manifestURL), let manifest = SessionManifestCodec.decode(data, diagnostics: &diagnostics) {
            return LoadResult(manifest: manifest, fellBack: false, diagnostics: diagnostics)
        }
        diagnostics.append("latest manifest unavailable, trying previous")
        if let data = try? Data(contentsOf: previousManifestURL), let manifest = SessionManifestCodec.decode(data, diagnostics: &diagnostics) {
            return LoadResult(manifest: manifest, fellBack: true, diagnostics: diagnostics)
        }
        return LoadResult(manifest: nil, fellBack: true, diagnostics: diagnostics)
    }

    func pruneOrphanSnapshots(keeping manifest: SessionManifest) {
        let referenced = Set(manifest.windows.flatMap { $0.tabs }.compactMap(\.snapshotFileName))
        guard let files = try? fileManager.contentsOfDirectory(at: snapshotsDirectory, includingPropertiesForKeys: nil) else { return }
        for file in files where (file.pathExtension == "md" || file.lastPathComponent.hasSuffix(".recovery.json")) && !referenced.contains(file.lastPathComponent) {
            try? fileManager.removeItem(at: file)
            try? fileManager.removeItem(at: file.appendingPathExtension("meta"))
        }
    }
}

enum SessionSnapshotIO {
    static func read(fileName: String) -> String? { SessionStore.shared.readSnapshot(fileName: fileName) }
}
