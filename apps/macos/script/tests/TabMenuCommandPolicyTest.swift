import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let noTabs = MenuCommandAvailabilityState(tabCount: 0, activeTabPath: nil, workspaceRoot: nil, hasContent: false)
let untitled = MenuCommandAvailabilityState(tabCount: 1, activeTabPath: nil, workspaceRoot: nil, hasContent: true)
let emptyDocument = MenuCommandAvailabilityState(
    tabCount: 1,
    activeTabPath: "/tmp/project/a.md",
    workspaceRoot: "/tmp/project",
    hasContent: false
)
let oneFile = MenuCommandAvailabilityState(
    tabCount: 1,
    activeTabPath: "/tmp/project/a.md",
    workspaceRoot: "/tmp/project",
    hasContent: true
)
let outsideFile = MenuCommandAvailabilityState(
    tabCount: 2,
    activeTabPath: "/tmp/outside/a.md",
    workspaceRoot: "/tmp/project",
    hasContent: true
)

expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "closeCurrentTab", state: noTabs),
       "close current tab must be disabled with no tabs")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "tabNext", state: untitled),
       "next tab must be disabled with one tab")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "closeOtherTabs", state: untitled),
       "close other tabs must be disabled with one tab")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "closeCurrentTab", state: untitled),
       "close current tab must be enabled for an untitled tab")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "tabNext", state: outsideFile),
       "next tab must be enabled with two or more tabs")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "copyActiveTabPath", state: oneFile),
       "copy path must be enabled when the active tab has a path")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "revealActiveTabInFinder", state: oneFile),
       "Finder reveal must be enabled when the active tab has a path")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "shareActiveTab", state: oneFile),
       "share must be enabled when the active tab has a path")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "copyActiveFileContents", state: oneFile),
       "copy file contents must be enabled when the active tab has a path")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "copyActiveTabPath", state: untitled),
       "copy path must be disabled for an untitled tab")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "shareActiveTab", state: untitled),
       "share must be disabled for an untitled tab")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "copyActiveFileContents", state: untitled),
       "copy file contents must stay available for an untitled tab (it copies editor content)")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "copyActiveFileContents", state: noTabs),
       "copy file contents must be disabled without any tab")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "copyActiveFileContents", state: emptyDocument),
       "copy file contents must be disabled when the document is empty")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "copyActiveTabPath", state: emptyDocument),
       "path commands stay available for an empty saved document")
expect(MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "revealActiveTabInWorkspace", state: oneFile),
       "workspace reveal must be enabled for a file inside the workspace")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "revealActiveTabInWorkspace", state: untitled),
       "workspace reveal must be disabled for an untitled tab")
expect(!MenuCommandAvailabilityPolicy.isTabCommandEnabled(command: "revealActiveTabInWorkspace", state: outsideFile),
       "workspace reveal must be disabled for a file outside the workspace")

print("TabMenuCommandPolicy tests passed")
