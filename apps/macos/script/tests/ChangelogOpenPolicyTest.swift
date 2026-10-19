import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(ChangelogOpenPolicy.destination(multiTabEnabled: true) == .currentTab,
       "changelog opens in the active window when multi-tab mode is enabled")
expect(ChangelogOpenPolicy.destination(multiTabEnabled: false) == .newWindow,
       "changelog opens in a separate window when multi-tab mode is disabled")

print("PASS")
