import Foundation

enum WorkspaceDeletionPolicy {
    /// Update only metadata; the owning host must retain each live editor's
    /// buffer, or capture a suspended tab's contents before recycling its file.
    static func detach(store: TabStore, deletedEntry: URL, isDirectory: Bool, recycled: Bool) -> [DocumentTabID] {
        guard recycled else { return [] }
        var detached: [DocumentTabID] = []
        for tab in store.tabs {
            guard deletesOpenDocument(openDocument: tab.path.map { URL(fileURLWithPath: $0) },
                                      deletedEntry: deletedEntry, isDirectory: isDirectory) else { continue }
            tab.detachedFileExtension = tab.path.map { URL(fileURLWithPath: $0).pathExtension }
            tab.path = nil
            tab.fileIdentity = nil
            tab.isDirty = true
            tab.isReadOnly = false
            tab.isReadOnlyLocked = false
            tab.untitledSequence = tab.untitledSequence ?? store.nextUntitledSequence()
            detached.append(tab.tabID)
        }
        return detached
    }

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
