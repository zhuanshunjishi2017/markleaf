import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(DocumentEncodingChangePolicy.action(
    current: .utf8, target: .utf16, hasFileURL: true, isDirty: false, isReadOnly: true
) == .rejectReadOnly, "read-only state should take precedence over every encoding choice")

expect(DocumentEncodingChangePolicy.action(
    current: .utf8, target: .utf8, hasFileURL: true, isDirty: true, isReadOnly: false
) == .noOp, "choosing the current encoding should be a no-op")

expect(DocumentEncodingChangePolicy.action(
    current: .utf8, target: .shiftJIS, hasFileURL: false, isDirty: false, isReadOnly: false
) == .updateUnsavedDocumentEncoding(.shiftJIS),
       "an unsaved document should update its planned save encoding without a disk prompt")

expect(DocumentEncodingChangePolicy.action(
    current: .utf8, target: .gb18030, hasFileURL: true, isDirty: false, isReadOnly: false
) == .prompt(target: .gb18030, warnsAboutUnsavedChanges: false),
       "a clean saved document should offer direct read, conversion, and cancel")

expect(DocumentEncodingChangePolicy.action(
    current: .utf8, target: .big5, hasFileURL: true, isDirty: true, isReadOnly: false
) == .prompt(target: .big5, warnsAboutUnsavedChanges: true),
       "a dirty saved document should explicitly warn that direct read discards edits")

print("PASS")
