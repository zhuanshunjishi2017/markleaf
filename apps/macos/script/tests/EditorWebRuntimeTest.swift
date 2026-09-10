import AppKit
import WebKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

func waitUntil(_ condition: () -> Bool, seconds: TimeInterval = 12) -> Bool {
    let deadline = Date().addingTimeInterval(seconds)
    while !condition(), Date() < deadline {
        RunLoop.main.run(until: Date().addingTimeInterval(0.02))
    }
    return condition()
}

final class RuntimeMessages: NSObject, WKScriptMessageHandler {
    var messages: [[String: Any]] = []
    func userContentController(_ controller: WKUserContentController, didReceive message: WKScriptMessage) {
        if let body = message.body as? [String: Any] { messages.append(body) }
    }
    func received(_ type: String) -> Bool { messages.contains { $0["type"] as? String == type } }
}

_ = NSApplication.shared
let root = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let recorder = RuntimeMessages()
let configuration = WKWebViewConfiguration()
configuration.setURLSchemeHandler(EditorSchemeHandler(root: root), forURLScheme: "markleaf")
configuration.userContentController.add(recorder, name: "markleaf")
configuration.userContentController.addUserScript(WKUserScript(source: """
window.__runtimeErrors = [];
window.addEventListener('error', e => window.__runtimeErrors.push({message:e.message, file:e.filename, line:e.lineno, resource:e.target?.src}), true);
window.addEventListener('unhandledrejection', e => window.__runtimeErrors.push({rejection:String(e.reason),stack:e.reason?.stack}));
window.addEventListener('securitypolicyviolation', e => window.__runtimeErrors.push({directive:e.violatedDirective,blocked:e.blockedURI}));
""", injectionTime: .atDocumentStart, forMainFrameOnly: true))
let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: 900, height: 650), configuration: configuration)
webView.load(URLRequest(url: URL(string: "markleaf://editor/index.html")!))

func evaluate(_ script: String) -> Any? {
    var finished = false
    var value: Any?
    webView.evaluateJavaScript(script) { result, error in
        value = result
        if let error { print("JavaScript evaluation: \(error)") }
        finished = true
    }
    expect(waitUntil { finished }, "JavaScript evaluation must complete")
    return value
}

if !waitUntil({ recorder.received("ready") }) {
    print("Startup diagnostics: \(evaluate("JSON.stringify({errors:window.__runtimeErrors,bridge:typeof window.chrome?.webview,html:document.body?.innerHTML})") ?? "unavailable")")
    expect(false, "packaged editor must send a real ready message through WKWebView")
}
print("PASS: packaged editor ready")

func send(_ type: String, payload: [String: Any] = [:]) {
    let message: [String: Any] = ["protocolVersion": 1, "type": type, "documentId": "runtime-test", "revision": 0, "requestId": UUID().uuidString, "payload": payload]
    let data = try! JSONSerialization.data(withJSONObject: message)
    _ = evaluate("window.postMessage(\(String(data: data, encoding: .utf8)!), '*')")
}
send("loadDocument", payload: ["markdown": "# Runtime check\n\nOriginal paragraph.\n", "readOnly": false])
expect(waitUntil { recorder.received("documentLoaded") }, "host document must load through the native shim")
expect((evaluate("document.querySelector('#editor h1')?.textContent") as? String) == "Runtime check", "visual editor must render the loaded Markdown heading")
send("localizeFindBar", payload: ["find": "Find", "alertNote": "Note"])
send("command", payload: ["command": "setLanguage", "text": "en"])
send("command", payload: ["command": "findText", "text": "Original\t0\t0"])
expect(waitUntil { recorder.received("findResult") }, "native find must work without HTML search controls")
let found = recorder.messages.last { $0["type"] as? String == "findResult" }?["payload"] as? [String: Any]
expect(found?["total"] as? Int == 1, "native find must return the actual match count")
send("command", payload: ["command": "replaceAll", "text": "Original\tReplaced\t0\t0"])
expect(waitUntil { recorder.messages.contains { ($0["payload"] as? [String: Any])?["replaced"] as? Int == 1 } }, "native replacement must work without HTML search controls")
send("command", payload: ["command": "findClose"])
recorder.messages.removeAll()
send("command", payload: ["command": "pasteMarkdown", "text": "**Native edit**"])
expect(waitUntil { recorder.received("commandResult") }, "edit command must produce an actual result")
send("requestSnapshot")
expect(waitUntil { recorder.received("snapshot") }, "edited document must return a snapshot")
let snapshot = recorder.messages.last { $0["type"] as? String == "snapshot" }?["payload"] as? [String: Any]
expect((snapshot?["markdown"] as? String)?.contains("**Native edit**") == true, "snapshot must contain the real edit")
expect((snapshot?["markdown"] as? String)?.contains("Replaced") == true, "snapshot must include the native replacement")
print("PASS: document load, visual render, native find/replace, edit and snapshot round trip")
