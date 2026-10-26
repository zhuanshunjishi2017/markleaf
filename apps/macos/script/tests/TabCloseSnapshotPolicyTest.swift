import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(
    TabCloseSnapshotPolicy.fallbackMarkdown(isReady: true, pendingMarkdown: "live") == nil,
    "ready sessions must continue using a live snapshot"
)
expect(
    TabCloseSnapshotPolicy.fallbackMarkdown(isReady: false, pendingMarkdown: "pending") == "pending",
    "not-ready sessions must close from their pending buffer"
)
expect(
    TabCloseSnapshotPolicy.fallbackMarkdown(isReady: false, pendingMarkdown: nil) == "",
    "empty not-ready sessions must close with an empty buffer"
)

print("PASS")
