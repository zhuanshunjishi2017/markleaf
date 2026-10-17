import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let tab = SessionTabRecord(tabID: "tab-1", path: "/tmp/A.md", title: "A.md", untitledSequence: nil, isDirty: true, revision: 3, encoding: "UTF-8", newLine: "LF", fingerprintModificationSeconds: 100, fingerprintSize: 12, cursorPosition: 42, selectionAnchor: 42, selectionHead: 42, scrollTop: 180, snapshotFileName: "snap-tab-1.md")
let window = SessionWindowRecord(windowID: "win-1", frameX: 10, frameY: 20, frameWidth: 1100, frameHeight: 760, workspacePath: "/tmp/ws", sidebarVisible: true, sidebarTab: "workspace", sidebarWidth: 240, outlineDetached: false, outlineWidth: 230, statusBarVisible: true, tabOrder: ["tab-1"], activeTabID: "tab-1", tabs: [tab])
let manifest = SessionManifest(schemaVersion: 1, generation: 7, savedAt: Date(), windows: [window])
let data = try SessionManifestCodec.encode(manifest)
var diagnostics: [String] = []
let decoded = SessionManifestCodec.decode(data, diagnostics: &diagnostics)
expect(decoded == manifest, "roundtrip keeps every field")
expect(diagnostics.isEmpty, "clean decode reports no diagnostics")
let readingTab = SessionTabRecord(
    tabID: "tab-2", path: "/tmp/B.md", title: "B.md", untitledSequence: nil,
    isDirty: false, revision: 4, encoding: "UTF-8", newLine: "LF",
    fingerprintModificationSeconds: nil, fingerprintSize: nil, cursorPosition: nil,
    selectionAnchor: nil, selectionHead: nil, visualSelectionFrom: nil,
    visualSelectionTo: nil, sourceSelectionFrom: nil, sourceSelectionTo: nil,
    scrollTop: 260,
    readingAnchor: ReadingAnchor(kind: .visual, ordinal: 4, total: 24, token: "target", fraction: 0.25),
    snapshotFileName: nil
)
var readingWindow = window
readingWindow.tabs = [tab, readingTab]
var readingManifest = manifest
readingManifest.windows = [readingWindow]
diagnostics = []
let readingData = try SessionManifestCodec.encode(readingManifest)
let decodedReading = SessionManifestCodec.decode(readingData, diagnostics: &diagnostics)
expect(decodedReading?.windows.first?.tabs.last?.readingAnchor == readingTab.readingAnchor, "reading anchor roundtrips")
var future = manifest; future.schemaVersion = 999
diagnostics = []
let futureData = try SessionManifestCodec.encode(future)
expect(SessionManifestCodec.decode(futureData, diagnostics: &diagnostics) == nil, "future schema rejected")
expect(!diagnostics.isEmpty, "future schema records diagnostic")
let json = String(data: data, encoding: .utf8)!.replacingOccurrences(of: "\"windowID\" : \"win-1\"", with: "\"windowIDBroken\" : true")
diagnostics = []
let partial = SessionManifestCodec.decode(json.data(using: .utf8)!, diagnostics: &diagnostics)
expect(partial?.windows.isEmpty == true && !diagnostics.isEmpty, "invalid window skipped")
diagnostics = []
expect(SessionManifestCodec.decode(Data("{ not json".utf8), diagnostics: &diagnostics) == nil, "unparseable manifest rejected")
print("PASS")
var lockedTab = tab
lockedTab.isReadOnly = true
lockedTab.isReadOnlyLocked = false
lockedTab.detachedFileExtension = "txt"
var lockedWindow = window
lockedWindow.tabs = [lockedTab]
var lockedManifest = manifest
lockedManifest.windows = [lockedWindow]
let lockedData = try SessionManifestCodec.encode(lockedManifest)
let lockedDecoded = SessionManifestCodec.decode(lockedData, diagnostics: &diagnostics)?.windows.first?.tabs.first
expect(lockedDecoded?.isReadOnly == true && lockedDecoded?.isReadOnlyLocked == false,
       "user read-only must remain reversible after restart")
expect(lockedDecoded?.detachedFileExtension == "txt", "deleted text files retain their document kind")
