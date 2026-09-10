import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

// 纯规范化：去除 . 与 ..，大小写不敏感卷折叠为小写
expect(
    FileIdentityPolicy.normalize(path: "/Users/test/./docs/../docs/Note.md", volumeIsCaseSensitive: true) == "/Users/test/docs/Note.md",
    "case-sensitive normalization should remove dot segments only"
)
expect(
    FileIdentityPolicy.normalize(path: "/Users/test/Docs/NOTE.md", volumeIsCaseSensitive: false) == "/Users/test/docs/note.md",
    "case-insensitive volumes should fold the path to lowercase"
)

// 同一文件的符号链接与直接路径必须得到同一身份
let tmp = FileManager.default.temporaryDirectory
    .appendingPathComponent("markleaf-file-identity-\(UUID().uuidString)", isDirectory: true)
try FileManager.default.createDirectory(at: tmp, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: tmp) }
let real = tmp.appendingPathComponent("Real.md")
try "# t".write(to: real, atomically: true, encoding: .utf8)
let link = tmp.appendingPathComponent("Link.md")
try FileManager.default.createSymbolicLink(at: link, withDestinationURL: real)
expect(
    FileIdentityPolicy.identity(for: link) == FileIdentityPolicy.identity(for: real),
    "symlink and direct path should resolve to the same identity"
)
expect(
    FileIdentityPolicy.matches(path: link.path, identity: FileIdentityPolicy.identity(for: real)),
    "matches should compare through symlinks"
)
print("PASS")
