import Foundation

/// Inputs used to decide which menu commands remain available for the active window.
struct MenuCommandAvailabilityState {
    let tabCount: Int
    let activeTabPath: String?
    let workspaceRoot: String?
}

/// Keeps menu-bar tab actions aligned with the guards that actually execute them.
enum MenuCommandAvailabilityPolicy {
    static func isTabCommandEnabled(
        command: String,
        state: MenuCommandAvailabilityState
    ) -> Bool {
        switch command {
        case "tabNext", "closeOtherTabs":
            return state.tabCount > 1
        case "closeCurrentTab":
            return state.tabCount > 0
        case "copyActiveTabPath", "revealActiveTabInFinder":
            return state.activeTabPath != nil
        case "revealActiveTabInWorkspace":
            guard let path = state.activeTabPath,
                  let root = state.workspaceRoot else { return false }
            return isPath(path, insideDirectory: root)
        default:
            return true
        }
    }

    private static func isPath(_ path: String, insideDirectory root: String) -> Bool {
        let standardizedPath = URL(fileURLWithPath: path).standardizedFileURL.path
        let standardizedRoot = URL(fileURLWithPath: root, isDirectory: true).standardizedFileURL.path
        return standardizedPath.hasPrefix(standardizedRoot + "/")
    }
}
