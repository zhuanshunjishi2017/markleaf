import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

expect(WorkspaceOpenRoutingPolicy.shouldRevealSidebarOnWorkspaceOpen(hadWorkspace: false, sidebarWasVisible: false), "a hidden sidebar should reveal when the first workspace opens")
expect(!WorkspaceOpenRoutingPolicy.shouldRevealSidebarOnWorkspaceOpen(hadWorkspace: true, sidebarWasVisible: false), "an existing workspace should not change sidebar visibility")
expect(!WorkspaceOpenRoutingPolicy.shouldRevealSidebarOnWorkspaceOpen(hadWorkspace: false, sidebarWasVisible: true), "a visible sidebar should remain visible")
print("PASS")
