import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(NativeTextEditingPolicy.shouldRoute(command: "paste", toNativeTextFieldEditor: true),
       "paste should route to a focused native text field")
expect(NativeTextEditingPolicy.shouldRoute(command: "copy", toNativeTextFieldEditor: true),
       "copy should route to a focused native text field")
expect(NativeTextEditingPolicy.shouldRoute(command: "selectAll", toNativeTextFieldEditor: true),
       "select all should route to a focused native text field")
expect(!NativeTextEditingPolicy.shouldRoute(command: "paste", toNativeTextFieldEditor: false),
       "paste should stay on the editor session outside a native text field")
expect(!NativeTextEditingPolicy.shouldRoute(command: "copyAs", toNativeTextFieldEditor: true),
       "copy as should not be routed to a native text field")

expect(NativeTextEditingPolicy.isEnabled(command: "copy", editable: true, hasSelection: true, hasClipboard: false),
       "copy should be enabled for a selected native text field")
expect(!NativeTextEditingPolicy.isEnabled(command: "copy", editable: true, hasSelection: false, hasClipboard: true),
       "copy should be disabled without a native text selection")
expect(NativeTextEditingPolicy.isEnabled(command: "paste", editable: true, hasSelection: false, hasClipboard: true),
       "paste should be enabled with clipboard text")
expect(!NativeTextEditingPolicy.isEnabled(command: "paste", editable: false, hasSelection: false, hasClipboard: true),
       "paste should be disabled in a non-editable native field")
expect(NativeTextEditingPolicy.isEnabled(command: "selectAll", editable: false, hasSelection: false, hasClipboard: false),
       "select all should be enabled in a read-only native field")

print("PASS")
