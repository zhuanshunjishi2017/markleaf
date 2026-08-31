import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(WindowClosePolicy.closesWindowOnTrafficLight,
       "the red traffic-light button must close the window itself")
expect(WindowClosePolicy.keepsWindowAfterClosingAllTabs,
       "closing all tabs should leave the native window open")

print("PASS")
