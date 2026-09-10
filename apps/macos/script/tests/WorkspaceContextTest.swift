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

context.close()
expect(context.root == nil && context.tree.isEmpty, "close clears the workspace")

// 排序选择回读
context.setSortOrder(.fileNameAscending)
expect(context.sortOrder == .fileNameAscending, "sort order persists on the context")
context.setListMode(true)
expect(context.listMode, "list mode persists on the context")
print("PASS")
