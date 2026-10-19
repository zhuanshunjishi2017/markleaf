import Foundation

var failures: [String] = []

func expect(
    _ command: String,
    _ expected: Bool,
    hasWindow: Bool = true,
    hasWorkspace: Bool = false,
    hasClosedTabs: Bool = false,
    file: StaticString = #fileID,
    line: UInt = #line
) {
    let actual = MenuCommandAvailabilityPolicy.isEmptyWindowCommandEnabled(
        command: command,
        hasWindow: hasWindow,
        hasWorkspace: hasWorkspace,
        hasClosedTabs: hasClosedTabs
    )
    if actual != expected {
        failures.append("\(command) at \(file):\(line): expected \(expected), got \(actual)")
    }
}

let appCommands = [
    "recoverUnsavedFiles", "showPreferences", "showAbout", "openWelcome", "openChangelog",
    "showShortcuts", "installOptionalFonts", "codeFormatterManager", "checkForUpdates",
    "openHelp", "learnMarkdown", "openSampleAlert", "openSampleYamlBasic",
    "openSampleYamlAdvanced", "toggleFollowSystemTheme", "toggleCodeHighlight", "showThemeSettings",
]
let windowCommands = [
    "new", "newPlainText", "newWindow", "open", "openReadOnly", "openInNewWindow", "openFolder",
    "toggleFocusMode", "toggleFullScreen", "toggleSidebar", "toggleStatusBar", "workspaceTab",
    "outlineTab", "treeView", "listView", "toggleDetachedOutline",
]
let documentCommands = [
    "save", "saveAll", "saveAs", "export", "exportWithLastSettings", "print", "undo", "redo",
    "cut", "copy", "copyPlain", "paste", "selectAll", "find", "insertImage", "insertTable",
    "insertLink", "insertFootnote", "toggleBold", "setParagraph", "setHeading1", "toggleBulletList",
    "toggleCodeBlock", "formatCodeBlock", "clearFormat", "sourceMode", "toggleReadOnlyMode",
    "toggleEditorFocusMode", "toggleTypewriterMode", "zoomIn", "zoomOut", "resetZoom",
    "rerenderAllMermaid", "restartEditor", "tabNext", "tabPrevious", "closeCurrentTab",
    "closeOtherTabs", "copyActiveTabPath", "copyActiveFileContents", "renameActiveTab",
]

for command in appCommands where !(command == "toggleFollowSystemTheme" || command == "toggleCodeHighlight") {
    expect(command, true, hasWindow: false, hasWorkspace: false, hasClosedTabs: false)
}
expect("toggleFollowSystemTheme", true, hasWindow: false)
expect("toggleCodeHighlight", true, hasWindow: false)

for command in windowCommands {
    expect(command, true)
    expect(command, false, hasWindow: false)
}
for command in documentCommands {
    expect(command, false)
}

expect("closeFolder", false, hasWorkspace: false)
expect("closeFolder", true, hasWorkspace: true)
expect("restoreClosedTab", false, hasClosedTabs: false)
expect("restoreClosedTab", true, hasClosedTabs: true)
expect("tabManagementParent", false, hasClosedTabs: false)
expect("tabManagementParent", true, hasClosedTabs: true)

if !failures.isEmpty {
    for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
    exit(1)
}
print("PASS: empty-window menu availability follows the documented command matrix")
