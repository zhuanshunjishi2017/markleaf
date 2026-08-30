import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let tab = DocumentTab(path: nil, title: "旧标题", encoding: "UTF-8", newLine: "LF")
tab.untitledSequence = 1

// 无路径文档：标题保持「未命名 N」，不受同步影响。
TabStateSync.apply(
    tab: tab,
    fileName: nil,
    isDirty: true,
    revision: 7,
    encoding: "GBK",
    newLine: "CRLF",
    untitledLabel: "未命名"
)
expect(tab.title == "未命名 1", "untitled tab keeps its numbered title")
expect(tab.isDirty && tab.contentRevision == 7, "dirty and revision sync verbatim")
expect(tab.encoding == "GBK" && tab.newLine == "CRLF", "encoding and newline sync verbatim")
expect(tab.fileIdentity == nil && tab.path == nil, "untitled tab keeps nil identity")

// 有路径文档：标题取文件名，身份随路径更新。
let pathed = DocumentTab(path: "/tmp/Old.md", title: "Old.md", encoding: "UTF-8", newLine: "LF")
TabStateSync.apply(
    tab: pathed,
    fileName: "/tmp/New.md",
    isDirty: false,
    revision: 9,
    encoding: "UTF-8",
    newLine: "LF",
    untitledLabel: "未命名"
)
expect(pathed.path == "/tmp/New.md" && pathed.title == "New.md", "pathed tab follows the session URL")
expect(pathed.fileIdentity == FileIdentityPolicy.identity(forPath: "/tmp/New.md"), "identity updates with path")
print("PASS")
