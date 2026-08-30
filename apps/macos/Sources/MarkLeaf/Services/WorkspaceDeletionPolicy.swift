import Foundation

enum WorkspaceDeletionPolicy {
    static func deletesOpenDocument(
        openDocument: URL?,
        deletedEntry: URL,
        isDirectory: Bool
    ) -> Bool {
        guard let openDocument else { return false }
        let documentPath = openDocument.standardizedFileURL.path
        let entryPath = deletedEntry.standardizedFileURL.path
        if documentPath == entryPath { return true }
        return isDirectory && documentPath.hasPrefix(entryPath + "/")
    }
}
