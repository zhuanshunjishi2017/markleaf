import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
expect(TabExternalChangePolicy.action(isDirty: false, fileExists: true, fingerprintChanged: true) == .reloadPreservingPosition, "clean tabs reload external changes")
expect(TabExternalChangePolicy.action(isDirty: true, fileExists: true, fingerprintChanged: true) == .presentConflict, "dirty tabs present conflict")
expect(TabExternalChangePolicy.action(isDirty: true, fileExists: false, fingerprintChanged: true) == .keepPending, "deleted dirty tabs keep pending")
expect(TabExternalChangePolicy.action(isDirty: false, fileExists: false, fingerprintChanged: true) == .showMissing, "deleted clean tabs show missing")
expect(TabExternalChangePolicy.action(isDirty: true, fileExists: true, fingerprintChanged: false) == .ignore, "unchanged fingerprints ignored")
print("PASS")
