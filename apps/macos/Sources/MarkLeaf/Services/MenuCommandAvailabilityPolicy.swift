import Foundation

/// Inputs used to decide which menu commands remain available for the active window.
struct MenuCommandAvailabilityState {
    let tabCount: Int
    let activeTabPath: String?
    let workspaceRoot: String?
    /// 目标文档当前是否有内容。空文档没有可复制的内容，复制类命令应一并置灰。
    let hasContent: Bool
}

/// Keeps menu-bar tab actions aligned with the guards that actually execute them.
enum MenuCommandAvailabilityPolicy {
    private static let emptyWindowAppCommands: Set<String> = [
        "recoverUnsavedFiles", "showPreferences", "showAbout",
        "openWelcome", "openChangelog", "showShortcuts", "installOptionalFonts",
        "codeFormatterManager", "checkForUpdates", "openHelp", "learnMarkdown",
        "openSampleAlert", "openSampleYamlBasic", "openSampleYamlAdvanced",
        "toggleFollowSystemTheme", "toggleCodeHighlight", "showThemeSettings",
    ]

    private static let emptyWindowWindowCommands: Set<String> = [
        "new", "newPlainText", "newWindow", "open", "openReadOnly", "openInNewWindow",
        "openFolder", "toggleFocusMode", "toggleFullScreen",
        "toggleSidebar", "toggleStatusBar", "workspaceTab", "outlineTab",
        "treeView", "listView", "toggleDetachedOutline",
    ]

    /// The empty-tab availability matrix. This policy is intentionally evaluated
    /// before submenu-parent commands are rewritten to a representative child.
    static func isEmptyWindowCommandEnabled(
        command: String,
        hasWindow: Bool,
        hasWorkspace: Bool,
        hasClosedTabs: Bool
    ) -> Bool {
        switch command {
        case "closeFolder":
            return hasWorkspace
        case "restoreClosedTab", "tabManagementParent":
            return hasClosedTabs
        default:
            if emptyWindowAppCommands.contains(command) { return true }
            return hasWindow && emptyWindowWindowCommands.contains(command)
        }
    }

    static func isTabCommandEnabled(
        command: String,
        state: MenuCommandAvailabilityState
    ) -> Bool {
        switch command {
        case "tabNext", "tabPrevious", "closeOtherTabs":
            return state.tabCount > 1
        case "closeCurrentTab":
            return state.tabCount > 0
        case "copyActiveTabPath", "revealActiveTabInFinder",
             "shareActiveTab":
            return state.activeTabPath != nil
        case "copyActiveFileContents":
            // 复制的是编辑器当前内容（包含未保存修改），因此只要有活动标签且文档非空即可用。
            return state.tabCount > 0 && state.hasContent
        case "revealActiveTabInWorkspace":
            guard let path = state.activeTabPath,
                  let root = state.workspaceRoot else { return false }
            return isPath(path, insideDirectory: root)
        default:
            return true
        }
    }

    static func isPath(_ path: String, insideDirectory root: String) -> Bool {
        let standardizedPath = URL(fileURLWithPath: path).standardizedFileURL.path
        let standardizedRoot = URL(fileURLWithPath: root, isDirectory: true).standardizedFileURL.path
        return standardizedPath.hasPrefix(standardizedRoot + "/")
    }
}
