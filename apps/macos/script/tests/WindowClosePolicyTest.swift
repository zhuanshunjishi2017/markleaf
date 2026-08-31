import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(WindowClosePolicy.shouldCloseAllTabs(tabCount: 3),
       "red-button window close should target every open tab")
expect(WindowClosePolicy.shouldCloseAllTabs(tabCount: 1),
       "red-button window close should target the last open tab")
expect(!WindowClosePolicy.shouldCloseAllTabs(tabCount: 0),
       "an already empty editor has no tabs to close")
expect(WindowClosePolicy.keepsWindowAfterClosingAllTabs,
       "closing all tabs should leave the native window open")

print("PASS")
