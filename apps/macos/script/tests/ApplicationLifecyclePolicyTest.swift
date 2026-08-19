import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(ApplicationLifecyclePolicy.terminateAfterLastWindowClosed == false,
       "the app should remain alive after its last window closes")
expect(ApplicationLifecyclePolicy.reopenAction(hasVisibleWindows: false, hasEditorWindow: false) == .createNewWindow,
       "Dock reopen with no editor window should create a blank window")
expect(ApplicationLifecyclePolicy.reopenAction(hasVisibleWindows: false, hasEditorWindow: true) == .showExistingWindow,
       "Dock reopen with an existing hidden editor should show it")
expect(ApplicationLifecyclePolicy.reopenAction(hasVisibleWindows: true, hasEditorWindow: true) == .none,
       "Dock reopen with a visible window should not create another window")

print("PASS")
