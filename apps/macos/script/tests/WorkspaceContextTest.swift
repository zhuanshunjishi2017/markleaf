import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let context = WorkspaceContext()
expect(context.root == nil && context.tree.isEmpty, "fresh workspace is empty")

// 加载不存在的目录应保持空
context.load("/tmp/markleaf-does-not-exist-\(UUID().uuidString)")
expect(context.root == nil, "loading a missing directory should not bind the workspace")

// 加载真实临时目录
let tmp = FileManager.default.temporaryDirectory
    .appendingPathComponent("markleaf-workspace-\(UUID().uuidString)", isDirectory: true)
try FileManager.default.createDirectory(at: tmp, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: tmp) }
try "x".write(to: tmp.appendingPathComponent("a.md"), atomically: true, encoding: .utf8)
context.load(tmp.path)
expect(context.root == tmp.path, "loading an existing directory binds the workspace")

func waitUntil(_ condition: () -> Bool) -> Bool {
    let deadline = Date().addingTimeInterval(3)
    while !condition(), Date() < deadline {
        RunLoop.main.run(until: Date().addingTimeInterval(0.01))
    }
    return condition()
}
expect(waitUntil { context.tree.count == 1 }, "initial scan completes")
let originalEntry = context.tree[0]
var refreshes = 0
context.onChanged = { refreshes += 1 }
context.rescan()
expect(context.tree.first === originalEntry && refreshes == 0, "refresh must not publish an empty intermediate tree")
expect(waitUntil { refreshes > 0 }, "refresh publishes its completed scan")
expect(context.tree.first === originalEntry, "unchanged paths keep their AppKit item identity")

context.setListMode(true)
context.rescan()
expect(waitUntil { context.documents.count == 1 && context.tree.count == 1 }, "tree and document-list scans must both complete")

var cancelledCallback = false
let cancelledScanner = WorkspaceScanner(root: tmp.path) { _ in cancelledCallback = true }
cancelledScanner.scan()
cancelledScanner.cancel()
context.rescan()

context.close()
expect(context.root == nil && context.tree.isEmpty, "close clears the workspace")
RunLoop.main.run(until: Date().addingTimeInterval(0.1))
expect(!cancelledCallback, "cancelled scans must not publish results")
expect(context.tree.isEmpty && context.documents.isEmpty, "queued scans must not repopulate a closed workspace")

let second = tmp.appendingPathComponent("other", isDirectory: true)
try FileManager.default.createDirectory(at: second, withIntermediateDirectories: true)
try "second".write(to: second.appendingPathComponent("b.md"), atomically: true, encoding: .utf8)
context.load(tmp.path)
context.load(second.path)
expect(waitUntil { context.tree.first?.name == "b.md" && context.documents.first?.name == "b.md" }, "switching workspaces must discard old tree and document results")
context.close()

// 排序选择回读
context.setSortOrder(.fileNameAscending)
expect(context.sortOrder == .fileNameAscending, "sort order persists on the context")
context.setListMode(true)
expect(context.listMode, "list mode persists on the context")
print("PASS")
