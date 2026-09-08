import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func record(path: String? = nil, title: String = "Untitled", sequence: Int? = nil) -> ClosedTabRecord {
    ClosedTabRecord(
        path: path,
        title: title,
        untitledSequence: sequence,
        isDirty: false,
        isReadOnly: false,
        encoding: "utf-8",
        newLine: "LF",
        visualSelectionFrom: nil,
        visualSelectionTo: nil,
        sourceSelectionFrom: nil,
        sourceSelectionTo: nil,
        scrollTop: nil,
        snapshotFileName: nil,
        documentKind: path?.hasSuffix(".txt") == true ? .plainText : .markdown
    )
}

expect(!ClosedTabHistoryPolicy.shouldRecord(closeReason: .closeWindow), "window close must not create a browser-style tab history entry")
expect(ClosedTabHistoryPolicy.shouldRecord(closeReason: .closeTab), "an explicit tab close must be restorable")

var history: [ClosedTabRecord] = []
history = ClosedTabHistoryPolicy.push(record(path: "/tmp/a.md"), into: history)
history = ClosedTabHistoryPolicy.push(record(path: "/tmp/b.md"), into: history)
expect(history.map(\.path) == ["/tmp/a.md", "/tmp/b.md"], "history must preserve close order")

history = ClosedTabHistoryPolicy.push(record(path: "/tmp/a.md"), into: history)
expect(history.count == 2, "re-closing a path must remove its older duplicate")
expect(history.last?.path == "/tmp/a.md", "the newest close must stay last")

history = ClosedTabHistoryPolicy.push(record(title: "Untitled 1", sequence: 1), into: history)
expect(history.count == 3, "untitled tabs must be restorable too")

history = ClosedTabHistoryPolicy.push(record(title: "Untitled 1", sequence: 1), into: history)
expect(history.count == 3, "re-closing an untitled tab must remove its older duplicate")
expect(history.last?.untitledSequence == 1, "the newest untitled close must stay last")

history = (0..<ClosedTabHistoryPolicy.maximumCount + 5).map { record(path: "/tmp/\($0).md") }
.reduce(history) { ClosedTabHistoryPolicy.push($1, into: $0) }
expect(history.count == ClosedTabHistoryPolicy.maximumCount, "history must stay bounded")
expect(history.first?.path == "/tmp/5.md", "history must drop the oldest entries first")

print("ClosedTabHistoryPolicy tests passed")
