import AppKit
@testable import MarkLeaf

_ = NSApplication.shared
SettingsService.shared.update {
    $0.multiTabEnabled = false
    $0.newLineStyle = "lf"
}

var failures = [String]()
func checkRestore(markdown: String?, snapshotFileName: String?, label: String) {
    let session = EditorSession()
    let window = WindowSession()
    let tab = DocumentTab(path: nil, title: "Untitled", encoding: "utf-8", newLine: "LF")
    window.tabStore.append(tab)
    window.attach(session: session, to: tab.tabID)
    let controller = EditorWindowController(session: session)
    controller.windowSession = window
    window.controller = controller
    window.closedTabHistory.push(ClosedTabRecord(
        path: nil, title: "Restored", untitledSequence: 2, isDirty: markdown != nil || snapshotFileName != nil,
        isReadOnly: false, encoding: "utf-8", newLine: "CRLF",
        visualSelectionFrom: 1, visualSelectionTo: 3, sourceSelectionFrom: 2, sourceSelectionTo: 4,
        scrollTop: 42, snapshotFileName: snapshotFileName, documentKind: .markdown, markdown: markdown))
    controller.restoreLastClosedTab()
    if session.documentNewLine != "CRLF" {
        failures.append("\(label): expected CRLF, got \(session.documentNewLine)")
    }
    if session.documentEncoding != "UTF-8" || session.visualSelectionFrom != 1 || session.sourceSelectionTo != 4 {
        failures.append("\(label): restore must retain encoding and both selection ranges")
    }
    controller.close()
}

checkRestore(markdown: "first\nsecond\n", snapshotFileName: nil, label: "memory snapshot")
checkRestore(markdown: nil, snapshotFileName: nil, label: "empty untitled")

// Use a unique snapshot and remove only those exact test-owned files.
let snapshot = try SessionStore.shared.writeSnapshot(tabID: "restore-test-\(UUID().uuidString)",
    content: "first\nsecond\n", revision: 1, encoding: "utf-8", newLine: "CRLF")
let snapshotURL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
    .appendingPathComponent("MarkLeaf/Session/snapshots/\(snapshot)")
defer {
    try? FileManager.default.removeItem(at: snapshotURL)
    try? FileManager.default.removeItem(at: snapshotURL.appendingPathExtension("meta"))
}
checkRestore(markdown: nil, snapshotFileName: snapshot, label: "disk snapshot")

func restoreFixture(path: String? = nil) -> (EditorSession, WindowSession, EditorWindowController) {
    let session = EditorSession()
    let window = WindowSession()
    let tab = DocumentTab(path: path, title: "Current", encoding: "UTF-8", newLine: "LF")
    window.tabStore.append(tab)
    window.attach(session: session, to: tab.tabID)
    let controller = EditorWindowController(session: session)
    controller.windowSession = window
    window.controller = controller
    window.closedTabHistory.push(ClosedTabRecord(path: path, title: "Closed", untitledSequence: 4,
        isDirty: true, isReadOnly: false, encoding: "UTF-8", newLine: "CRLF",
        visualSelectionFrom: nil, visualSelectionTo: nil, sourceSelectionFrom: nil, sourceSelectionTo: nil,
        scrollTop: nil, snapshotFileName: nil, documentKind: .markdown, markdown: "old closed buffer"))
    return (session, window, controller)
}

do {
    let (session, window, controller) = restoreFixture()
    session.loadDocument(markdown: "current unsaved", fileURL: nil, initialDirty: true)
    session.setDocumentNewLine(.crlf)
    // No presentation window means the real disposition coordinator cancels.
    session.webView = nil
    controller.restoreLastClosedTab()
    if !window.closedTabHistory.hasRecords { failures.append("cancelled restore must not consume closed buffer") }
    controller.close()
}
SettingsService.shared.update { $0.multiTabEnabled = true }
do {
    let path = NSTemporaryDirectory() + "markleaf-existing-\(UUID().uuidString).md"
    let (session, window, controller) = restoreFixture(path: path)
    session.loadDocument(markdown: "newer unsaved", fileURL: URL(fileURLWithPath: path), initialDirty: true)
    let identity = session.currentDocumentIdentifier
    controller.restoreLastClosedTab()
    if window.tabStore.tabs.count != 1 || session.currentDocumentIdentifier != identity {
        failures.append("restore must activate an existing file without duplicating or replacing its buffer")
    }
    controller.close()
}
for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
if !failures.isEmpty { exit(1) }
print("PASS: closed-tab restore retains CRLF and selection metadata for memory, disk and empty buffers")
