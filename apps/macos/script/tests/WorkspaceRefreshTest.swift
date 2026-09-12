import AppKit
@testable import MarkLeaf

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
func waitUntil(_ condition: () -> Bool) -> Bool {
    let deadline = Date().addingTimeInterval(4)
    while !condition(), Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.01)) }
    return condition()
}

_ = NSApplication.shared
let fm = FileManager.default
let root = fm.temporaryDirectory.appendingPathComponent("markleaf-refresh-\(UUID().uuidString)")
let nested = root.appendingPathComponent("Folder/Nested")
try fm.createDirectory(at: nested, withIntermediateDirectories: true)
for i in 0..<40 { try "text".write(to: nested.appendingPathComponent(String(format: "%02d.md", i)), atomically: true, encoding: .utf8) }
defer { try? fm.removeItem(at: root) }

let context = WorkspaceContext()
let session = EditorSession(workspace: context)
let sidebar = SidebarView(session: session)
func findTree(_ view: NSView) -> WorkspaceTreeView? {
    if let tree = view as? WorkspaceTreeView { return tree }
    return view.subviews.lazy.compactMap(findTree).first
}
let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 260, height: 400), styleMask: [.titled], backing: .buffered, defer: false)
window.contentView = sidebar
let tree = findTree(sidebar)!
context.load(root.path)
expect(waitUntil { tree.numberOfRows == 1 }, "workspace root must load")
let folder = tree.item(atRow: 0) as! WorkspaceEntry
tree.expandItem(folder)
expect(waitUntil { tree.numberOfRows == 2 }, "folder expansion must load children")
let child = tree.item(atRow: 1) as! WorkspaceEntry
tree.expandItem(child)
expect(waitUntil { tree.numberOfRows == 42 }, "nested folder expansion must load files")
window.contentView?.layoutSubtreeIfNeeded()
tree.selectRowIndexes(IndexSet(integer: 20), byExtendingSelection: false)
tree.scrollRowToVisible(20)
let selection = tree.item(atRow: tree.selectedRow) as! WorkspaceEntry
let origin = tree.enclosingScrollView!.contentView.bounds.origin

func expectBrowsingState(_ operation: String) {
    expect(tree.isItemExpanded(folder) && tree.isItemExpanded(child), "\(operation) must preserve nested expansion")
    expect((tree.item(atRow: tree.selectedRow) as? WorkspaceEntry)?.path == selection.path, "\(operation) must preserve the user's selection")
    expect(abs(tree.enclosingScrollView!.contentView.bounds.origin.y - origin.y) < 1, "\(operation) must preserve the scroll position")
}

context.rescan()
RunLoop.main.run(until: Date().addingTimeInterval(0.3))
expectBrowsingState("unchanged filesystem refresh")
sidebar.selectTab(0, persist: false)
sidebar.setWorkspaceMode(listMode: false)
expectBrowsingState("repeated view state synchronization")
let otherTab = EditorSession(workspace: context)
sidebar.rebind(to: otherTab)
expectBrowsingState("switching to another untitled tab in the same workspace")

let detachedTab = EditorSession(workspace: context)
detachedTab.outlineDetached = true
sidebar.rebind(to: detachedTab)
expect(!sidebar.tabControl.isEnabled(forSegment: 1), "right outline must disable the sidebar outline tab")
expect(sidebar.tabControl.selectedSegment == 0, "right outline must force the sidebar to Workspace")

try "new".write(to: nested.appendingPathComponent("new.md"), atomically: true, encoding: .utf8)
context.rescan()
expect(waitUntil { tree.numberOfRows == 43 }, "refresh must show new files inside expanded directories")
expectBrowsingState("file creation")
try fm.removeItem(at: nested.appendingPathComponent("new.md"))
context.rescan()
expect(waitUntil { tree.numberOfRows == 42 }, "refresh must remove deleted files")
expectBrowsingState("file deletion")

tree.collapseItem(child)
try "later".write(to: nested.appendingPathComponent("later.md"), atomically: true, encoding: .utf8)
context.rescan()
RunLoop.main.run(until: Date().addingTimeInterval(0.1))
expect(!tree.isItemExpanded(child), "refresh must not undo an intentional collapse")
tree.expandItem(child)
expect(waitUntil { tree.numberOfRows == 43 }, "reopening a collapsed directory must refresh its contents")
context.close()
print("PASS: nested expansion, selection, scrolling, tab binding and file changes")

// 只有非文本文件的目录过滤后为空，展开后必须稳定，不能自行不断重载。
final class ReloadTrackingTree: WorkspaceTreeView {
    var directoryReloads = 0

    override func reloadDirectoryChildren(_ entry: WorkspaceEntry) {
        directoryReloads += 1
        super.reloadDirectoryChildren(entry)
    }
}

let filteredRoot = root.appendingPathComponent("Filtered")
let pdfFolder = filteredRoot.appendingPathComponent("PDF only")
try fm.createDirectory(at: pdfFolder, withIntermediateDirectories: true)
for name in ["manual.pdf", "image.png", "archive.zip"] {
    try Data([0x25, 0x50, 0x44, 0x46, 0x00]).write(to: pdfFolder.appendingPathComponent(name))
}
try "text".write(to: filteredRoot.appendingPathComponent("readme.md"), atomically: true, encoding: .utf8)
let filteredContext = WorkspaceContext()
let filteredSession = EditorSession(workspace: filteredContext)
let filteredTree = ReloadTrackingTree(frame: NSRect(x: 0, y: 0, width: 260, height: 400))
filteredTree.configure(session: filteredSession)
filteredContext.onChanged = { filteredTree.reloadData() }
filteredContext.load(filteredRoot.path)
expect(waitUntil { filteredTree.numberOfRows == 2 }, "non-text files must stay out of the workspace tree")
let pdfDirectory = filteredTree.item(atRow: 0) as! WorkspaceEntry
filteredTree.expandItem(pdfDirectory)
RunLoop.main.run(until: Date().addingTimeInterval(0.5))
let settledReloads = filteredTree.directoryReloads
RunLoop.main.run(until: Date().addingTimeInterval(0.3))
print("Non-text directory reloads: \(settledReloads) -> \(filteredTree.directoryReloads)")
expect(filteredTree.directoryReloads == settledReloads, "an empty filtered directory must stop reloading without user or filesystem input")
expect(filteredTree.isItemExpanded(pdfDirectory), "an empty filtered directory must preserve the user's expansion")
expect(filteredTree.numberOfRows == 2, "PDF, images and archives must not become document rows")

try "notes".write(to: pdfFolder.appendingPathComponent("notes.txt"), atomically: true, encoding: .utf8)
filteredContext.rescan()
expect(waitUntil { filteredTree.numberOfRows == 3 }, "an expanded non-text directory must show a newly created text document")
try fm.removeItem(at: pdfFolder.appendingPathComponent("notes.txt"))
filteredContext.rescan()
expect(waitUntil { filteredTree.numberOfRows == 2 }, "deleting the last text document must remove its row")
RunLoop.main.run(until: Date().addingTimeInterval(0.3))
let emptyReloads = filteredTree.directoryReloads
RunLoop.main.run(until: Date().addingTimeInterval(0.3))
expect(filteredTree.directoryReloads == emptyReloads, "removing the last text document must not restart a reload loop")
expect(filteredTree.isItemExpanded(pdfDirectory), "removing the last text document must keep expansion")
filteredContext.close()
print("PASS: non-text-only folders settle and continue tracking text file changes")

// 搜索与树/列表读取同一份跨平台文件类型样例。
struct SearchFixture: Decodable {
    struct FileCase: Decodable { let name: String; let included: Bool }
    let files: [FileCase]
}
let searchFixture = try JSONDecoder().decode(SearchFixture.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
let searchRoot = root.appendingPathComponent("Search")
try fm.createDirectory(at: searchRoot, withIntermediateDirectories: true)
for item in searchFixture.files { try "needle".write(to: searchRoot.appendingPathComponent(item.name), atomically: true, encoding: .utf8) }
let search = WorkspaceSearchService()
var matches: [WorkspaceSearchResult]?
search.search(root: searchRoot.path, query: "needle") { matches = $0 }
expect(waitUntil { matches != nil }, "workspace search must finish")
expect(Set(matches!.map { $0.entry.name }) == Set(searchFixture.files.filter(\.included).map(\.name)), "search uses the same Windows text scope as tree and list")
print("PASS: workspace search matches the shared file-type fixture")
