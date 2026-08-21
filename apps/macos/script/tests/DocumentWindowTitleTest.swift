import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(
    DocumentWindowTitle.format(
        fileName: "笔记.md",
        isDirty: false,
        untitledLabel: "未命名",
        modifiedLabel: "已修改"
    ) == "笔记.md",
    "saved documents should keep the plain filename"
)
expect(
    DocumentWindowTitle.format(
        fileName: "笔记.md",
        isDirty: true,
        untitledLabel: "未命名",
        modifiedLabel: "已修改"
    ) == "笔记.md - 已修改",
    "modified documents should append the modified marker"
)
expect(
    DocumentWindowTitle.format(
        fileName: nil,
        isDirty: true,
        untitledLabel: "未命名",
        modifiedLabel: "已修改"
    ) == "未命名 - 已修改",
    "untitled modified documents should still show the marker"
)
print("PASS")
