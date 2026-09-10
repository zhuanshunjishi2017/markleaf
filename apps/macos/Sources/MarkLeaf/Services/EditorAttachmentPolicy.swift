import Foundation

/// Determines whether a tab session still needs an editor container attached.
enum EditorAttachmentPolicy {
    static func needsContainer(existingSession: Bool, hasAttachedContainer: Bool) -> Bool {
        !hasAttachedContainer
    }
}
