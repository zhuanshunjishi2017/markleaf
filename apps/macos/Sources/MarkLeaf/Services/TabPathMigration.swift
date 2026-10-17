import Foundation

enum TabPathMigration {
    static func applyRename(store: TabStore, from oldPath: String, to newPath: String) -> [DocumentTabID] {
        let oldIdentity = FileIdentityPolicy.identity(forPath: oldPath).normalizedPath
        var migrated: [DocumentTabID] = []
        for tab in store.tabs {
            guard let path = tab.path else { continue }
            let identity = tab.fileIdentity?.normalizedPath ?? FileIdentityPolicy.identity(forPath: path).normalizedPath
            guard identity == oldIdentity || identity.hasPrefix(oldIdentity + "/") else { continue }
            let descendantCount = URL(fileURLWithPath: identity).pathComponents.count
                - URL(fileURLWithPath: oldIdentity).pathComponents.count
            let suffix = URL(fileURLWithPath: path).pathComponents.suffix(descendantCount)
            let target = suffix.reduce(URL(fileURLWithPath: newPath)) { $0.appendingPathComponent($1) }.path
            tab.path = target
            tab.fileIdentity = FileIdentityPolicy.identity(forPath: target)
            tab.title = URL(fileURLWithPath: target).lastPathComponent
            migrated.append(tab.tabID)
        }
        return migrated
    }
}
