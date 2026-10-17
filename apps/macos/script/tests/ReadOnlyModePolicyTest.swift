import Foundation

func expect(
    _ condition: @autoclosure () -> Bool,
    _ message: String,
    file: StaticString = #filePath,
    line: UInt = #line
) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(
    ReadOnlyModePolicy.action(
        currentReadOnly: false,
        isDirty: true,
        fileURLIsWritable: true
    ) == .toggle,
    "user read-only is a view state; entering it must preserve an unsaved buffer"
)
expect(
    ReadOnlyModePolicy.action(
        currentReadOnly: false,
        isDirty: false,
        fileURLIsWritable: true
    ) == .toggle,
    "a clean writable document can enter read-only mode"
)
expect(
    ReadOnlyModePolicy.action(
        currentReadOnly: true,
        isDirty: false,
        fileURLIsWritable: false
    ) == .blockedFileNotWritable,
    "a read-only file cannot leave read-only mode"
)
expect(
    ReadOnlyModePolicy.action(
        currentReadOnly: true,
        isDirty: false,
        fileURLIsWritable: true
    ) == .toggle,
    "a writable file can leave read-only mode"
)

print("PASS")
