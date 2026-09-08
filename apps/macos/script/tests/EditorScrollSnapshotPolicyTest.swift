import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

expect(EditorScrollSnapshotPolicy.restoreValue(rawValue: 120.0) == 120, "finite positive offsets should be preserved")
expect(EditorScrollSnapshotPolicy.restoreValue(rawValue: 0.0) == 0, "zero should be preserved")
expect(EditorScrollSnapshotPolicy.restoreValue(rawValue: -1.0) == 0, "negative offsets should clamp to the top")
expect(EditorScrollSnapshotPolicy.restoreValue(rawValue: Double.nan) == 0, "NaN should clamp to the top")
expect(EditorScrollSnapshotPolicy.restoreValue(rawValue: 12) == 12, "integer JSON offsets should be accepted")
expect(EditorScrollSnapshotPolicy.restoreValue(rawValue: "bad") == 0, "invalid offsets should clamp to the top")
print("PASS")
