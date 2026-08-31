import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
let store = TabStore()
let a = DocumentTab(path: "/tmp/ws/Note.md", title: "Note.md", encoding: "UTF-8", newLine: "LF")
let b = DocumentTab(path: "/tmp/ws/Other.md", title: "Other.md", encoding: "UTF-8", newLine: "LF")
store.append(a); store.append(b)
let moved = TabPathMigration.applyRename(store: store, from: "/tmp/ws/Note.md", to: "/tmp/ws/renamed/Note.md")
expect(moved == [a.tabID], "only matching tab migrates")
expect(a.path == "/tmp/ws/renamed/Note.md" && a.title == "Note.md", "path and title follow rename")
expect(a.fileIdentity == FileIdentityPolicy.identity(forPath: "/tmp/ws/renamed/Note.md"), "identity rebuilt")
expect(b.path == "/tmp/ws/Other.md", "unrelated tab untouched")
expect(TabPathMigration.applyRename(store: store, from: "/tmp/ws/Unknown.md", to: "/tmp/ws/X.md").isEmpty, "unknown path changes nothing")
print("PASS")
