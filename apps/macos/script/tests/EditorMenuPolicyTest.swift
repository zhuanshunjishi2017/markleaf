import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let readOnly = EditorMenuPolicy.commands(isSourceMode: false, isReadOnly: true)
expect(readOnly.contains("copy"), "read-only menu should keep copy")
expect(readOnly.contains("selectAll"), "read-only menu should keep select all")
expect(!readOnly.contains("paste"), "read-only menu should hide paste")
expect(!readOnly.contains("toggleBold"), "read-only menu should hide formatting")

let editableSource = EditorMenuPolicy.commands(isSourceMode: true, isReadOnly: false)
expect(editableSource.contains("pastePlainText"), "editable source menu should expose paste as plain text")
expect(editableSource.contains("undo"), "editable source menu should expose undo")

print("PASS")
