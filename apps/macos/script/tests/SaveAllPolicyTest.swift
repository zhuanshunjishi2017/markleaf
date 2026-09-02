import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
let clean = DocumentTab(path: "/tmp/A.md", title: "A.md", encoding: "UTF-8", newLine: "LF")
let dirty = DocumentTab(path: "/tmp/B.md", title: "B.md", encoding: "UTF-8", newLine: "LF"); dirty.isDirty = true
let untitled = DocumentTab(path: nil, title: "未命名 1", encoding: "UTF-8", newLine: "LF"); untitled.isDirty = true
expect(SaveAllPolicy.targets(tabs: [clean, dirty, untitled]) == [dirty.tabID], "only dirty pathed tabs target save all")
expect(SaveAllPolicy.targets(tabs: [clean]).isEmpty, "clean tabs produce no targets")
print("PASS")
