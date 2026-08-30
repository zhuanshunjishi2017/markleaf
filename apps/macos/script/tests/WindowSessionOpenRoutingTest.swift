import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

// 先创建真实文件，使符号链接可被解析；这与既有 FileIdentity 测试的用法一致。
let aPath = "/tmp/A.md"
try? FileManager.default.removeItem(atPath: aPath)
try "# a".write(to: URL(fileURLWithPath: aPath), atomically: true, encoding: .utf8)
defer { try? FileManager.default.removeItem(atPath: aPath) }

let store = TabStore()
let existing = DocumentTab(path: aPath, title: "A.md", encoding: "UTF-8", newLine: "LF")
store.append(existing)

// 命中去重：同一文件（含符号链接差异）→ 激活已有。
let link = "/tmp/A-link.md"
try? FileManager.default.removeItem(atPath: link)
try FileManager.default.createSymbolicLink(atPath: link, withDestinationPath: aPath)
defer { try? FileManager.default.removeItem(atPath: link) }

let hit = TabOpenResolution.resolve(
    store: store,
    url: URL(fileURLWithPath: link),
    untitledLabel: "未命名"
)
guard case .activateExisting(let id) = hit else {
    fputs("FAIL: symlinked path should hit the existing tab\n", stderr)
    exit(1)
}
expect(id == existing.tabID, "resolution should activate the matching tab")

// 未命中：创建新标签。
let miss = TabOpenResolution.resolve(
    store: store,
    url: URL(fileURLWithPath: "/tmp/B.md"),
    untitledLabel: "未命名"
)
guard case .created(let newTab) = miss else {
    fputs("FAIL: unknown path should create a tab\n", stderr)
    exit(1)
}
expect(newTab.path == "/tmp/B.md" && newTab.title == "B.md", "created tab carries path and title")
expect(store.tab(withID: newTab.tabID) != nil, "created tab is appended to the store")
print("PASS")
