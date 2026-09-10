import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

// When every tab has been closed, "open in current tab" has no destination.
// The window must recover by routing the request as a new tab.
expect(WorkspaceOpenRoutingPolicy.route(hasActiveTab: false, prefersNewTab: false) == .newTab,
       "current-tab mode falls back to a new tab after all tabs close")
expect(WorkspaceOpenRoutingPolicy.route(hasActiveTab: true, prefersNewTab: false) == .currentTab,
       "current-tab mode replaces the active tab while one exists")
expect(WorkspaceOpenRoutingPolicy.route(hasActiveTab: true, prefersNewTab: true) == .newTab,
       "new-tab mode keeps using window routing while a tab exists")
expect(WorkspaceOpenRoutingPolicy.route(hasActiveTab: false, prefersNewTab: true) == .newTab,
       "new-tab mode also works without an active tab")

print("PASS")
