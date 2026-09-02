import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \\(message)\\n", stderr)
        exit(1)
    }
}

if case .noWorkspace = SidebarEmptyStatePolicy.state(hasWorkspace: false, treeCount: 0, documentCount: 0, listMode: false) {
} else {
    expect(false, "missing workspace uses the workspace empty state")
}

if case .populated = SidebarEmptyStatePolicy.state(hasWorkspace: true, treeCount: 0, documentCount: 3, listMode: true) {
} else {
    expect(false, "list mode with scanned documents never shows the empty state")
}

if case .noSupportedFiles = SidebarEmptyStatePolicy.state(hasWorkspace: true, treeCount: 0, documentCount: 0, listMode: true) {
} else {
    expect(false, "empty list mode shows the supported-file empty state")
}

if case .populated = SidebarEmptyStatePolicy.state(hasWorkspace: true, treeCount: 2, documentCount: 0, listMode: false) {
} else {
    expect(false, "tree mode with entries never shows the empty state")
}

print("PASS")
