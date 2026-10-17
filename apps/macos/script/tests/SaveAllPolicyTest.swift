import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
let clean = DocumentTab(path: "/tmp/A.md", title: "A.md", encoding: "UTF-8", newLine: "LF")
let dirty = DocumentTab(path: "/tmp/B.md", title: "B.md", encoding: "UTF-8", newLine: "LF"); dirty.isDirty = true
let untitled = DocumentTab(path: nil, title: "未命名 1", encoding: "UTF-8", newLine: "LF"); untitled.isDirty = true
let locked = DocumentTab(path: "/tmp/locked.md", title: "locked", encoding: "UTF-8", newLine: "LF")
locked.isDirty = true
locked.isReadOnly = true
expect(SaveAllPolicy.targets(tabs: [clean, dirty, untitled, locked]) == [dirty.tabID, untitled.tabID],
       "save all includes dirty untitled buffers and skips all read-only tabs")
expect(SaveAllPolicy.hasTarget(tabs: [untitled]), "an untitled dirty buffer enables save all")
expect(!SaveAllPolicy.hasTarget(tabs: [locked]), "read-only dirty buffers do not enable save all")
expect(SaveAllPolicy.targets(tabs: [clean]).isEmpty, "clean tabs produce no targets")
var started: [String] = []
var callbacks: [(Bool) -> Void] = []
var result: Bool?
SaveAllPolicy.run(["A", "B", "C"], operation: { item, done in
    started.append(item)
    callbacks.append(done)
}, completion: { result = $0 })
expect(started == ["A"], "batch operations wait for each asynchronous item")
callbacks[0](true)
expect(started == ["A", "B"], "the next item starts only after success")
callbacks[1](false)
expect(started == ["A", "B"] && result == false, "cancellation keeps A complete and never starts C")
print("PASS")
