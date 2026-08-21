import Foundation

private var failures = 0

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        failures += 1
        fputs("FAIL: \(message)\n", stderr)
        return
    }
}

expect(WorkspaceTreeDataSourcePolicy.safeIndex(0, count: 0) == nil,
       "stale outline rows must not index an empty child array")
expect(WorkspaceTreeDataSourcePolicy.safeIndex(-1, count: 3) == nil,
       "negative outline rows must be rejected")
expect(WorkspaceTreeDataSourcePolicy.safeIndex(2, count: 3) == 2,
       "valid outline rows must remain addressable")
expect(WorkspaceTreeDataSourcePolicy.shouldRestoreSelection(
    activePath: "/tmp/workspace/README.md",
    entryPath: "/tmp/workspace/README.md"
) == true, "active document selection should survive a tree reload")
expect(WorkspaceTreeDataSourcePolicy.shouldRestoreSelection(
    activePath: "/tmp/workspace/README.md",
    entryPath: "/tmp/workspace/other.md"
) == false, "tree reload must not select a different document")

if failures > 0 {
    exit(1)
}
print("PASS")
