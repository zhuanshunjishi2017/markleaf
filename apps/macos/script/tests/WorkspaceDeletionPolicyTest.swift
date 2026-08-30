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

print("PASS")
