import AppKit
import WebKit
@testable import MarkLeaf

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

// Exercise native request production/consumption without loading a web page.
final class RecordingWebView: WKWebView {
    var messages: [[String: Any]] = []
    var failCommands = false
    override func evaluateJavaScript(_ script: String, completionHandler: (@MainActor @Sendable (Any?, Error?) -> Void)? = nil) {
        let prefix = "window.postMessage(JSON.parse("
        let suffix = "), '*')"
        if script.hasPrefix(prefix), script.hasSuffix(suffix) {
            let literal = String(script.dropFirst(prefix.count).dropLast(suffix.count))
            let json = (try! JSONSerialization.jsonObject(with: Data("[\(literal)]".utf8))) as! [String]
            let message = (try! JSONSerialization.jsonObject(with: Data(json[0].utf8))) as! [String: Any]
            messages.append(message)
            if failCommands, message["type"] as? String == "command" {
                completionHandler?(nil, NSError(domain: "ClipboardTest", code: 1))
                return
            }
        }
        completionHandler?(nil, nil)
    }
    override func reload() -> WKNavigation? { nil }
}

_ = NSApplication.shared
SettingsService.shared.update { $0.displayLanguage = "zh-Hans" }
let webView = RecordingWebView(frame: .zero)
let session = EditorSession()
session.webView = webView
session.handleEditorMessage(["type": "ready"])

let pasteboard = NSPasteboard.withUniqueName()
defer { pasteboard.releaseGlobally(); session.cleanupForClose() }
func copy(_ mode: EditorSession.ClipboardCopyMode, text: String = "bold", markdown: String = "**bold**", html: String = "<p><strong>bold</strong></p>") {
    session.copySelectionAs(mode, pasteboard: pasteboard)
    session.handleSelectionExport(["text": text, "markdown": markdown, "html": html])
    RunLoop.main.run(until: Date().addingTimeInterval(0.03))
}
copy(.html)
expect(pasteboard.string(forType: .string) == "<p><strong>bold</strong></p>", "Copy HTML must provide source in the text clipboard format")
expect(!(pasteboard.types ?? []).contains(.html), "Copy HTML source must not advertise formatted HTML")
copy(.formatted)
expect(pasteboard.string(forType: .string) == "bold", "formatted copy must retain readable plain text")
expect(pasteboard.string(forType: .html) == "<p><strong>bold</strong></p>", "formatted copy must retain rich HTML")
copy(.markdown)
expect(pasteboard.string(forType: .string) == "**bold**", "Markdown copy must retain markers")
copy(.html, text: "visible", html: "")
expect(pasteboard.string(forType: .string) == "**bold**", "an empty HTML export must leave clipboard content intact")
expect(session.statusText == "当前没有可复制的文本", "empty copy must report no content")

func beginPaste(sourceMode: Bool = false, html: String? = nil) -> [String: Any] {
    session.handleEditorMessage(["type": "commandStateChanged", "payload": ["sourceMode": sourceMode]])
    let command = EditorPastePolicy.command(isSourceMode: sourceMode, plainText: "# heading", html: html)!
    session.statusText = "waiting"
    session.executePaste(command)
    let request = webView.messages.last!
    expect(request["requestId"] is String, "paste must send a request id")
    expect(session.statusText == "waiting", "sending a command must not claim paste success")
    expect((request["payload"] as? [String: Any])?["command"] as? String == command.command, "paste route must reach the webview")
    return request
}
func reply(_ request: [String: Any], success: Bool = true, outcome: String? = nil, error: String? = nil) {
    var payload: [String: Any] = ["success": success]
    payload["outcome"] = outcome
    payload["error"] = error
    session.handleEditorMessage([
        "type": "commandResult", "documentId": request["documentId"]!,
        "requestId": request["requestId"]!, "revision": 20, "payload": payload,
    ])
}

for (outcome, expected) in [
    ("markdown", "已粘贴 Markdown"),
    ("normalized", "已粘贴 Markdown，并转换了不兼容的格式"),
    ("plainText", "Markdown 格式不兼容，已作为纯文本粘贴"),
    ("formatted", "已粘贴格式化内容"),
] {
    let request = beginPaste()
    let timeout = session.pendingPasteCommands[request["requestId"] as! String]!.timeout
    reply(request, outcome: outcome)
    expect(session.statusText == expected, "paste outcome \(outcome) must use the Windows status")
    expect(session.pendingPasteCommands.isEmpty && timeout.isCancelled, "completed paste must release its timeout")
    reply(request, success: false)
    expect(session.statusText == expected, "duplicate replies must not replace the completed result")
}
reply(beginPaste(), outcome: "plainText", error: "Invalid marks: code")
expect(session.statusText == "已作为纯文本粘贴：Invalid marks: code", "plain text fallback must expose its reason")
reply(beginPaste(sourceMode: true), outcome: "plainText")
expect(session.statusText == "已粘贴纯文本", "source paste is literal text, not a Markdown fallback")
reply(beginPaste(sourceMode: true))
expect(session.statusText == "已粘贴纯文本", "source pasteText accepts a successful result without an outcome")
reply(beginPaste(html: "<b>heading</b>"))
expect(session.statusText == "已粘贴格式化内容", "successful legacy rich replies use the requested format")
reply(beginPaste(), success: false, outcome: "markdown")
expect(session.statusText == "无法粘贴剪贴板内容", "failure must win over any outcome label")

let stale = beginPaste()
let staleID = stale["requestId"] as! String
let revision = session.currentRevision
session.handleEditorMessage(["type": "commandResult", "documentId": "old-document", "requestId": staleID, "revision": 9999, "payload": ["success": true]])
session.handleEditorMessage(["type": "commandResult", "documentId": session.currentDocumentIdentifier, "requestId": "unknown", "revision": 9999, "payload": ["success": true]])
expect(session.statusText == "waiting" && session.currentRevision == revision, "unmatched replies must not mutate status or revision")
expect(session.pendingPasteCommands[staleID] != nil, "wrong-document reply must not consume the pending request")
session.loadDocument(markdown: "new", fileURL: nil)
expect(session.pendingPasteCommands.isEmpty, "document replacement must cancel pending paste")
session.statusText = "new document"
reply(stale)
expect(session.statusText == "new document" && session.currentRevision == 0, "old-document reply must not affect the replacement")

let timed = beginPaste()
session.pendingPasteCommands[timed["requestId"] as! String]!.timeout.perform()
expect(session.pendingPasteCommands.isEmpty && session.statusText == "剪贴板操作失败", "timeout must fail and release the request")
reply(timed)
expect(session.statusText == "剪贴板操作失败", "late success must not hide a timeout")
webView.failCommands = true
session.executePaste(EditorPasteCommand(command: "pasteMarkdown", text: "text", html: nil))
expect(session.pendingPasteCommands.isEmpty && session.statusText == "剪贴板操作失败", "transport failure must fail immediately")
webView.failCommands = false

session.handleEditorMessage(["type": "commandStateChanged", "payload": ["readOnly": true]])
let sentBeforeReadOnly = webView.messages.count
session.executePaste(EditorPasteCommand(command: "pasteMarkdown", text: "blocked", html: nil))
expect(webView.messages.count == sentBeforeReadOnly && session.pendingPasteCommands.isEmpty, "read-only paste must not issue a command")

let closing = beginPaste()
session.cleanupForClose()
expect(session.pendingPasteCommands.isEmpty, "close must cancel pending paste")
session.statusText = "closed"
reply(closing)
expect(session.statusText == "closed", "closed sessions must ignore pending replies")
let restarting = beginPaste()
session.restartEditor()
session.handleEditorMessage(["type": "snapshot", "payload": ["markdown": "saved"]])
expect(session.pendingPasteCommands.isEmpty, "editor restart must cancel pending paste")
session.statusText = "restarting"
reply(restarting)
expect(session.statusText == "restarting", "replies from the old webview must not affect a restart")

for language in ["en", "ja", "zh-Hant"] {
    for key in ["已粘贴 Markdown", "已粘贴 Markdown，并转换了不兼容的格式", "Markdown 格式不兼容，已作为纯文本粘贴", "已作为纯文本粘贴：%@", "无法粘贴剪贴板内容"] {
        expect(L10n.translationKeys(for: language).contains(key), "\(language) must translate \(key)")
    }
}
print("PASS: clipboard content, paste replies, failures and session lifecycle")
