import AppKit
@testable import MarkLeaf

_ = NSApplication.shared
var failures = [String]()
func attach(_ session: EditorSession, to window: WindowSession) {
    let tab = DocumentTab(path: nil, title: "Untitled", encoding: "UTF-8", newLine: "LF")
    window.tabStore.append(tab)
    window.attach(session: session, to: tab.tabID)
}
let window = WindowSession()
let first = EditorSession(), second = EditorSession(), other = EditorSession()
attach(first, to: window); attach(second, to: window)
let otherWindow = WindowSession(); attach(other, to: otherWindow)
first.setZoom(150)
first.toggleEditorFocusMode()
first.toggleTypewriterMode()
if second.zoomPercent != 150 || !second.isEditorFocusMode || !second.isTypewriterMode {
    failures.append("existing tabs must share window zoom and writing modes")
}
if other.zoomPercent != 100 || other.isEditorFocusMode || other.isTypewriterMode {
    failures.append("another window must retain independent view state")
}
let third = EditorSession(); attach(third, to: window)
third.applyPostLoadSettings()
if third.zoomPercent != 150 || !third.isEditorFocusMode || !third.isTypewriterMode {
    failures.append("new tabs must inherit the current window view state")
}
first.sidebarVisible = false
first.showWorkspaceTab()
if !first.sidebarVisible || first.sidebarTabIndex != 0 { failures.append("workspace navigation must reveal even an already active hidden page") }
first.sidebarVisible = false
first.showOutlineTab()
if !first.sidebarVisible || first.sidebarTabIndex != 1 { failures.append("outline navigation must reveal the hidden sidebar") }
for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
if !failures.isEmpty { exit(1) }
print("PASS: view state belongs to its window, not its tabs or other windows")
