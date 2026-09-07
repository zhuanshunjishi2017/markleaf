import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let mixed = EditorDropPolicy.classify([
    URL(fileURLWithPath: "/tmp/photo.PNG"),
    URL(fileURLWithPath: "/tmp/readme.md"),
    URL(fileURLWithPath: "/tmp/notes.txt"),
])
expect(mixed.images == [URL(fileURLWithPath: "/tmp/photo.PNG")], "image drops should be classified")
expect(mixed.documents.map(\.path) == ["/tmp/readme.md", "/tmp/notes.txt"], "document drops should be classified")
expect(EditorDropPolicy.classify([URL(fileURLWithPath: "/tmp/file.zip")]).isEmpty, "unsupported drops should be ignored")
print("PASS")
