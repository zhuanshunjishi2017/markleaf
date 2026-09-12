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
        let fileName = "snap-\(tabID).md"
        try content.write(to: snapshotsDirectory.appendingPathComponent(fileName), atomically: true, encoding: .utf8)
        let meta: [String: Any] = ["tabID": tabID, "revision": revision, "encoding": encoding, "newLine": newLine, "timestamp": ISO8601DateFormatter().string(from: Date())]
        let metaData = try JSONSerialization.data(withJSONObject: meta, options: [.sortedKeys])
        try metaData.write(to: snapshotsDirectory.appendingPathComponent(fileName + ".meta"), options: .atomic)
        return fileName
    }

    func readSnapshot(fileName: String) -> String? {
        guard fileName.hasPrefix("snap-"), fileName.hasSuffix(".md") else { return nil }
        return try? String(contentsOf: snapshotsDirectory.appendingPathComponent(fileName), encoding: .utf8)
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
        for file in files where file.pathExtension == "md" && !referenced.contains(file.lastPathComponent) {
            try? fileManager.removeItem(at: file)
            try? fileManager.removeItem(at: file.appendingPathExtension("meta"))
        }
    }
}

enum SessionSnapshotIO {
    static func read(fileName: String) -> String? { SessionStore.shared.readSnapshot(fileName: fileName) }
}
