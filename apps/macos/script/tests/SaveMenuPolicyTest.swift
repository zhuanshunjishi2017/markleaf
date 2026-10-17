import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(SaveMenuPolicy.isSaveEnabled(hasSession: true, isDirty: false, isReadOnly: false),
       "an empty untitled document must be savable")
expect(!SaveMenuPolicy.isSaveEnabled(hasSession: false, isDirty: true, isReadOnly: false),
       "no session cannot save")
expect(!SaveMenuPolicy.isSaveEnabled(hasSession: true, isDirty: true, isReadOnly: true),
       "read-only documents cannot save in place")
expect(!SaveMenuPolicy.isSaveEnabled(hasSession: true, isDirty: false, isReadOnly: false, hasFileURL: true),
       "a named clean document has no changes to save")
expect(SaveMenuPolicy.isSaveEnabled(hasSession: true, isDirty: true, isReadOnly: false, hasFileURL: true),
       "a named dirty document can save")
expect(SaveMenuPolicy.isSaveAsEnabled(hasSession: true), "read-only sessions can save a copy")
expect(!SaveMenuPolicy.isSaveAsEnabled(hasSession: false), "save as needs a session")
print("PASS")
