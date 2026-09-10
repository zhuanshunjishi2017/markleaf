import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let doc = "/tmp/markleaf-links/docs/readme.md"
expect(LocalLinkPolicy.resolve("./images/a.png", documentPath: doc) == "/tmp/markleaf-links/docs/images/a.png", "relative link resolves from document directory")
expect(LocalLinkPolicy.resolve("../notes.txt", documentPath: doc) == "/tmp/markleaf-links/notes.txt", "parent relative link resolves")
expect(LocalLinkPolicy.resolve("https://example.com", documentPath: doc) == nil, "remote link is not treated as local")
expect(!LocalLinkPolicy.isOpenableFile("/tmp/markleaf-links/docs"), "directories should not be opened as local links")
print("PASS")
