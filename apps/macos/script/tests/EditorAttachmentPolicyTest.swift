import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \\(message)\\n", stderr)
        exit(1)
    }
}

expect(EditorAttachmentPolicy.needsContainer(existingSession: true, hasAttachedContainer: false),
       "pre-registered first tab still needs an editor container")
expect(!EditorAttachmentPolicy.needsContainer(existingSession: true, hasAttachedContainer: true),
       "already attached tab does not create a duplicate editor container")
expect(EditorAttachmentPolicy.needsContainer(existingSession: false, hasAttachedContainer: false),
       "new tab needs an editor container")

print("PASS")
