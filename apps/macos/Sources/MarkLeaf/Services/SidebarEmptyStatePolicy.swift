import Foundation

enum SidebarEmptyState {
    case noWorkspace
    case noSupportedFiles
    case populated
}

enum SidebarEmptyStatePolicy {
    static func state(
        hasWorkspace: Bool,
        treeCount: Int,
        documentCount: Int,
        listMode: Bool
    ) -> SidebarEmptyState {
        guard hasWorkspace else { return .noWorkspace }
        let supportedCount = listMode ? documentCount : treeCount
        return supportedCount == 0 ? .noSupportedFiles : .populated
    }
}
