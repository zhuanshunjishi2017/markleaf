import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(MultiTabModePolicy.showsTabBar(isEnabled: true), "the tab bar should be visible when multi-tab is enabled")
expect(!MultiTabModePolicy.showsTabBar(isEnabled: false), "the tab bar should be hidden when multi-tab is disabled")
expect(MultiTabModePolicy.allowsTabCreation(isEnabled: true), "multi-tab enabled should allow tab creation")
expect(!MultiTabModePolicy.allowsTabCreation(isEnabled: false), "multi-tab disabled must prevent tab creation")
expect(MultiTabModePolicy.workspacePrefersNewTab(workspacePreference: true, multiTabEnabled: true),
       "workspace files should respect the new-tab preference when tabs are enabled")
expect(!MultiTabModePolicy.workspacePrefersNewTab(workspacePreference: true, multiTabEnabled: false),
       "workspace files must replace the current document when tabs are disabled")
expect(MultiTabModePolicy.externalFileMode(.newTab, multiTabEnabled: false) == .currentWindow,
       "external new-tab requests must become replacement requests when tabs are disabled")
expect(MultiTabModePolicy.externalFileMode(.newWindow, multiTabEnabled: false) == .newWindow,
       "external new-window requests remain windows when tabs are disabled")
expect(MultiTabModePolicy.externalFileMode(.newTab, multiTabEnabled: true) == .newTab,
       "multi-tab enabled should retain the external open preference")

print("PASS")
