import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

let encoded = MarkdownImagePathPolicy.encode("我的 文件/图 片.png")
expect(encoded == "我的%20文件/图%20片.png", "only spaces should be percent encoded")
expect(MarkdownImagePathPolicy.absolute("/tmp/中文 图.png") == "/tmp/中文%20图.png", "absolute Unicode paths should stay readable")

let relative = MarkdownImagePathPolicy.relative(
    documentPath: "/tmp/docs/readme.md",
    filePath: "/tmp/docs/assets/中文 图.png",
    prefixDotSlash: true
)
expect(relative == "./assets/中文%20图.png", "relative paths should keep Unicode readable and encode spaces")

let outside = MarkdownImagePathPolicy.relative(
    documentPath: "/tmp/docs/readme.md",
    filePath: "/tmp/中文 图.png",
    prefixDotSlash: true
)
expect(outside == nil, "paths above the document directory should not be relativized")
print("PASS")
