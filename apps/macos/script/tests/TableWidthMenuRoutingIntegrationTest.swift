import AppKit
import WebKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
func evaluate(_ web: WKWebView, _ script: String) -> Any? {
    var done = false
    var value: Any?
    web.evaluateJavaScript(script) { result, error in
        expect(error == nil, "menu receiver JavaScript must run")
        value = result; done = true
    }
    let deadline = Date().addingTimeInterval(8)
    while !done, Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.01)) }
    expect(done, "menu receiver must respond")
    return value
}
func entries(_ menu: NSMenu) -> [NSMenuItem] {
    menu.items.flatMap { [$0] + ($0.submenu.map(entries) ?? []) }
}
_ = NSApplication.shared
NSApp.setActivationPolicy(.prohibited)
let session = EditorSession()
let web = WKWebView(frame: NSRect(x: 0, y: 0, width: 600, height: 400))
session.webView = web
web.loadHTMLString("<script>window.nativeCommands=[];addEventListener('message',e=>{if(e.data.type==='command')nativeCommands.push(e.data.payload.command)})</script>", baseURL: nil)
let deadline = Date().addingTimeInterval(8)
while web.isLoading, Date() < deadline { RunLoop.main.run(until: Date().addingTimeInterval(0.01)) }
expect((evaluate(web, "Array.isArray(window.nativeCommands)") as? Bool) == true, "receiver must load")
let commands = ["fitTableContents", "distributeTableColumns", "resetTableWidths"]
session.handleEditorMessage(["type": "commandStateChanged", "payload": [
    "inTable": true, "semanticContext": "table", "selectionScope": "caret",
    "actions": Dictionary(uniqueKeysWithValues: commands.map { ($0, ["enabled": true, "checked": false]) })
]])
let (menu, menuSession) = session.buildEditorContextMenu()
for command in commands {
    guard let item = entries(menu).first(where: { $0.representedObject as? String == command }) else {
        expect(false, "native menu must contain \(command)"); fatalError()
    }
    expect(item.isEnabled && item.action != nil, "width command must be available")
    expect(NSApp.sendAction(item.action!, to: item.target, from: item), "native menu action must be delivered")
    RunLoop.main.run(until: Date().addingTimeInterval(0.05))
    expect((evaluate(web, "window.nativeCommands") as? [String])?.last == command,
           "native menu click must forward \(command) to the editor")
}
menuSession.finish()
print("PASS: all native table-width menu actions reach the WebKit command bridge")
