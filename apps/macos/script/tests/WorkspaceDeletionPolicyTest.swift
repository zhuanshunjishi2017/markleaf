import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(WorkspaceDeletionPolicy.deletesOpenDocument(
    openDocument: URL(fileURLWithPath: "/tmp/work/file.md"),
    deletedEntry: URL(fileURLWithPath: "/tmp/work/file.md"),
    isDirectory: false
), "deleting the open file should reset the editor")

expect(WorkspaceDeletionPolicy.deletesOpenDocument(
    openDocument: URL(fileURLWithPath: "/tmp/work/folder/file.md"),
    deletedEntry: URL(fileURLWithPath: "/tmp/work/folder"),
    isDirectory: true
), "deleting a parent folder should reset the editor")

expect(!WorkspaceDeletionPolicy.deletesOpenDocument(
    openDocument: URL(fileURLWithPath: "/tmp/work/file.md"),
    deletedEntry: URL(fileURLWithPath: "/tmp/work/other.md"),
    isDirectory: false
), "deleting another file should preserve the editor")

let store = TabStore()
let first = DocumentTab(path: "/tmp/work/foo/一.md", title: "一.md", encoding: "UTF-8", newLine: "LF")
let second = DocumentTab(path: "/tmp/work/foo/二.md", title: "二.md", encoding: "UTF-8", newLine: "LF")
let sibling = DocumentTab(path: "/tmp/work/foobar/三.md", title: "三.md", encoding: "UTF-8", newLine: "LF")
first.visualSelectionFrom = 3
second.scrollTop = 45
second.isReadOnly = true
[first, second, sibling].forEach { store.append($0) }
let deleted = URL(fileURLWithPath: "/tmp/work/foo")
expect(WorkspaceDeletionPolicy.detach(store: store, deletedEntry: deleted, isDirectory: true, recycled: false).isEmpty,
       "a failed recycle cannot mutate tab state")
expect(first.path != nil && second.isReadOnly, "failed recycle preserves paths and modes")
expect(WorkspaceDeletionPolicy.detach(store: store, deletedEntry: deleted, isDirectory: true, recycled: true) == [first.tabID, second.tabID],
       "all actual descendants detach after successful recycle")
expect(first.path == nil && second.path == nil && first.isDirty && second.isDirty, "deleted backing files become dirty untitled buffers")
expect(first.visualSelectionFrom == 3 && second.scrollTop == 45 && !second.isReadOnly, "buffer metadata and position survive detach")
expect(sibling.path == "/tmp/work/foobar/三.md", "prefix siblings are unaffected")
print("PASS")
