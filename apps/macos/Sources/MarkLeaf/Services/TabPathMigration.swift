import Foundation

enum TabPathMigration {
    static func applyRename(store: TabStore, from oldPath: String, to newPath: String) -> [DocumentTabID] {
        let oldIdentity = FileIdentityPolicy.identity(forPath: oldPath)
        var migrated: [DocumentTabID] = []
        for tab in store.tabs where tab.fileIdentity == oldIdentity {
            tab.path = newPath
            tab.fileIdentity = FileIdentityPolicy.identity(forPath: newPath)
            tab.title = URL(fileURLWithPath: newPath).lastPathComponent
            migrated.append(tab.tabID)
        }
        return migrated
    }
}
