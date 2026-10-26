import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
func descendants(_ view: NSView) -> [NSView] { [view] + view.subviews.flatMap(descendants) }
func settleLayout() { RunLoop.main.run(until: Date().addingTimeInterval(0.4)) }

let app = NSApplication.shared
app.setActivationPolicy(.prohibited)
SettingsService.shared.update {
    $0.multiTabEnabled = true
    $0.sidebarVisible = true
    $0.statusBarVisible = true
}
let workspace = WorkspaceContext()
let bootstrap = EditorSession(workspace: workspace)
let controller = EditorWindowController(session: bootstrap)
let windowSession = WindowSession(workspace: workspace)
windowSession.controller = controller
let first = DocumentTab(path: nil, title: "First", encoding: "utf-8", newLine: "lf")
windowSession.tabStore.append(first)
windowSession.attach(session: bootstrap, to: first.tabID)
controller.windowSession = windowSession
let second = DocumentTab(path: nil, title: "Second", encoding: "utf-8", newLine: "lf")
windowSession.tabStore.append(second, activate: false)
let documentSession = EditorSession(workspace: workspace)
windowSession.attach(session: documentSession, to: second.tabID)
controller.activateTab(second.tabID, animated: false)
controller.applyViewState()
let views = descendants(controller.window!.contentView!)
let button = views.compactMap { $0 as? NSButton }.first {
    $0.action == NSSelectorFromString("toggleSidebarFromStatusBar")
}!
let sidebar = views.compactMap { $0 as? SidebarView }.first!
func clickToggle() {
    expect(NSApp.sendAction(button.action!, to: button.target, from: button), "status-bar button must route an action")
    settleLayout()
}
expect(windowSession.activeTabSession === documentSession, "test must activate a different document session")
expect(bootstrap.sidebarVisible, "window sidebar initially visible")
clickToggle()
expect(!bootstrap.sidebarVisible, "status-bar toggle must update window state when a later tab is active")
expect(sidebar.isHiddenOrHasHiddenAncestor, "status-bar toggle must actually hide the sidebar")
controller.activateTab(first.tabID, animated: false)
expect(!bootstrap.sidebarVisible, "switching tabs must preserve hidden window state")
clickToggle()
expect(bootstrap.sidebarVisible && !sidebar.isHiddenOrHasHiddenAncestor, "toggle must reveal the sidebar after a tab switch")
controller.activateTab(second.tabID, animated: false)
clickToggle()
expect(!bootstrap.sidebarVisible && sidebar.isHiddenOrHasHiddenAncestor, "repeated toggle on the later tab must hide it again")
windowSession.tabStore.close(first.tabID)
windowSession.tabStore.close(second.tabID)
clickToggle()
expect(bootstrap.sidebarVisible && !sidebar.isHiddenOrHasHiddenAncestor, "window toggle must remain usable with no document tabs")
print("PASS: status-bar sidebar action controls window layout across tab switches and empty tabs")
