import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \\(message)\\n", stderr)
        exit(1)
    }
}

expect(SidebarStateSourcePolicy.selectedTabIndex(activeSessionIndex: 1, bootstrapSessionIndex: 0) == 1,
       "sidebar refresh keeps the active session tab selection")
expect(SidebarStateSourcePolicy.selectedTabIndex(activeSessionIndex: 0, bootstrapSessionIndex: 1) == 0,
       "sidebar refresh can switch back to workspace from the active session")

print("PASS")
