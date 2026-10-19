import AppKit
import WebKit
@testable import MarkLeaf

_ = NSApplication.shared
var failures = [String]()
func commands(_ menu: NSMenu) -> [String] {
    menu.items.flatMap { item in
        (item.representedObject as? String).map { [$0] } ?? (item.submenu.map(commands) ?? [])
    }
}
func checkMenu(context: String, extra: [String: Any], expected: [String]) {
    let s = EditorSession()
    s.loadDocument(markdown: "example", fileURL: nil, readOnly: true)
    var payload = extra
    payload["readOnly"] = true
    payload["semanticContext"] = context
    payload["actions"] = Dictionary(uniqueKeysWithValues: expected.map { ($0, ["enabled": true, "checked": false]) })
    s.handleEditorMessage(["type": "commandStateChanged", "documentId": s.currentDocumentIdentifier,
        "revision": 0, "payload": payload])
    let (menu, lifetime) = s.buildEditorContextMenu()
    for name in expected where !commands(menu).contains(name) { failures.append("\(context) missing native action \(name)") }
    lifetime.finish()
}
checkMenu(context: "mermaid", extra: ["mermaidSelected": true, "mermaidRendered": true],
    expected: ["viewMermaidSource", "saveMermaidAs", "rerenderMermaid"])
checkMenu(context: "math", extra: ["mathBlock": true], expected: ["viewMathSource"])
checkMenu(context: "ordinaryBlock", extra: ["linkHref": "https://example.com", "footnoteReferenceLabel": "n"],
    expected: ["openLink", "copyLinkAddress", "goToFootnoteDefinition"])

final class RecordingWebView: WKWebView {
    var scripts = [String]()
    override func evaluateJavaScript(_ javaScriptString: String, completionHandler: (@MainActor (Any?, Error?) -> Void)? = nil) {
        scripts.append(javaScriptString)
        completionHandler?(nil, nil)
    }
}
let s = EditorSession()
s.loadDocument(markdown: "[^n]: note", fileURL: nil, readOnly: true)
let web = RecordingWebView(frame: .zero)
s.webView = web
s.execute("goToFootnoteReference", text: "n")
if !web.scripts.contains(where: { $0.contains("goToFootnoteReference") }) {
    failures.append("readonly footnote navigation must reach the Web bridge")
}
let previousMultiTab = SettingsService.shared.settings.multiTabEnabled
for enabled in [false, true] {
    SettingsService.shared.update { $0.multiTabEnabled = enabled }
    let item = NSMenuItem(title: "", action: nil, keyEquivalent: "w")
    item.representedObject = "closeCurrentTab"
    _ = MenuRouter.shared.validateMenuItem(item)
    if item.title != L10n.t(enabled ? "关闭当前标签页" : "关闭当前文件") {
        failures.append("close action title must follow the multi-tab setting")
    }
}
SettingsService.shared.update { $0.multiTabEnabled = previousMultiTab }
for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
if !failures.isEmpty { exit(1) }
print("PASS: native object actions are reachable and readonly navigation crosses the host bridge")
