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
