import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let doc = "/tmp/markleaf-links/docs/readme.md"
expect(LocalLinkPolicy.resolve("./images/a.png", documentPath: doc) == "/tmp/markleaf-links/docs/images/a.png", "relative link resolves from document directory")
expect(LocalLinkPolicy.resolve("../notes.txt", documentPath: doc) == "/tmp/markleaf-links/notes.txt", "parent relative link resolves")
expect(LocalLinkPolicy.resolve("https://example.com", documentPath: doc) == nil, "remote link is not treated as local")
let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
defer { try? FileManager.default.removeItem(at: root) }
let folder = root.appendingPathComponent("会议记录", isDirectory: true)
try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
let file = root.appendingPathComponent("readme.md")
try Data("hello".utf8).write(to: file)
expect(LocalLinkPolicy.isOpenableItem(folder.path), "existing directories must be openable local links")
expect(LocalLinkPolicy.isOpenableItem(file.path), "regular files remain openable")
expect(!LocalLinkPolicy.isOpenableItem(root.appendingPathComponent("missing").path), "missing targets are not openable")
expect(LocalLinkPolicy.resolve("会议记录/", documentPath: file.path) == folder.path, "Chinese directory links resolve beside the document")
expect(LocalLinkPolicy.resolve("%E4%BC%9A%E8%AE%AE%E8%AE%B0%E5%BD%95/", documentPath: file.path) == folder.path, "encoded directory links resolve")
expect(LocalLinkPolicy.resolve(folder.absoluteString, documentPath: nil) == folder.path, "file URLs resolve directories without a document")
expect(LocalLinkPolicy.isAllowedAddress("#heading"), "anchor links remain editable")
expect(LocalLinkPolicy.isAllowedAddress("../notes.md"), "relative links remain editable")
expect(!LocalLinkPolicy.isAllowedAddress("javascript:alert(1)"), "unsafe schemes are rejected")
print("PASS")
