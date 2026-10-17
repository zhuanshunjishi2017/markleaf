import Foundation

/// Keeps Save commands aligned with the work their actions would actually do.
enum SaveMenuPolicy {
    static func isSaveEnabled(hasSession: Bool, isDirty: Bool, isReadOnly: Bool, hasFileURL: Bool = false) -> Bool {
        hasSession && !isReadOnly && (isDirty || !hasFileURL)
    }

    static func isSaveAsEnabled(hasSession: Bool) -> Bool {
        hasSession
    }

    static func isSaveAllEnabled(hasSavableTarget: Bool) -> Bool {
        hasSavableTarget
    }
}
