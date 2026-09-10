import Foundation

enum SidebarStateSourcePolicy {
    static func selectedTabIndex(activeSessionIndex: Int, bootstrapSessionIndex: Int) -> Int {
        activeSessionIndex
    }
}
