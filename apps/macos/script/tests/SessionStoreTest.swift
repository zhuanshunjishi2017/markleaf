import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let root = FileManager.default.temporaryDirectory.appendingPathComponent("markleaf-session-store-\(UUID().uuidString)")
let store = SessionStore(rootDirectory: root)
func makeManifest(_ generation: Int64, _ title: String) -> SessionManifest {
    SessionManifest(schemaVersion: 1, generation: generation, savedAt: Date(), windows: [SessionWindowRecord(windowID: "win-1", frameX: nil, frameY: nil, frameWidth: nil, frameHeight: nil, workspacePath: nil, sidebarVisible: nil, sidebarTab: nil, sidebarWidth: nil, outlineDetached: nil, outlineWidth: nil, statusBarVisible: nil, tabOrder: ["00000000-0000-0000-0000-000000000001"], activeTabID: "00000000-0000-0000-0000-000000000001", tabs: [SessionTabRecord(tabID: "00000000-0000-0000-0000-000000000001", path: nil, title: title, untitledSequence: 1, isDirty: true, revision: 1, encoding: "UTF-8", newLine: "LF", fingerprintModificationSeconds: nil, fingerprintSize: nil, cursorPosition: nil, selectionAnchor: nil, selectionHead: nil, scrollTop: nil, snapshotFileName: "snap-00000000-0000-0000-0000-000000000001.recovery.json")])])
}
let name = try store.writeSnapshot(tabID: "00000000-0000-0000-0000-000000000001", content: "# hi", revision: 1, encoding: "UTF-8", newLine: "LF")
expect(store.readSnapshot(fileName: name) == "# hi", "snapshot roundtrip")
try store.commit(manifest: makeManifest(1, "gen1")); try store.commit(manifest: makeManifest(2, "gen2"))
expect(store.loadLatest().manifest?.generation == 2 && !store.loadLatest().fellBack, "latest generation wins")
try "garbage".write(to: root.appendingPathComponent("MarkLeaf/Session/manifest.json"), atomically: true, encoding: .utf8)
let fallback = store.loadLatest()
expect(fallback.manifest?.generation == 1 && fallback.fellBack && !fallback.diagnostics.isEmpty, "corrupted latest falls back")
try store.commit(manifest: makeManifest(3, "gen3")); try store.writeSnapshot(tabID: "00000000-0000-0000-0000-000000000009", content: "orphan", revision: 1, encoding: "UTF-8", newLine: "LF")
store.pruneOrphanSnapshots(keeping: store.loadLatest().manifest!)
expect(store.readSnapshot(fileName: "snap-00000000-0000-0000-0000-000000000009.recovery.json") == nil, "orphan removed")
expect(store.readSnapshot(fileName: "snap-00000000-0000-0000-0000-000000000001.recovery.json") == "# hi", "referenced snapshot kept")
print("PASS")
