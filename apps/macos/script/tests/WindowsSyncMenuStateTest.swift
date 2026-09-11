import AppKit
import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

// Command scopes match the migration contract.
expect(EditorCommandRouter.scope(for: "toggleHighlight") == .document, "highlight is document scoped")
expect(EditorCommandRouter.scope(for: "insertAlertNote") == .document, "alerts are document scoped")
expect(EditorCommandRouter.scope(for: "showFrontMatter") == .document, "front matter is document scoped")
expect(EditorCommandRouter.scope(for: "setMathNumber") == .document, "math numbering is document scoped")
expect(EditorCommandRouter.scope(for: "copyHtml") == .document, "copy HTML is document scoped")
expect(EditorCommandRouter.scope(for: "restartEditor") == .windowLevel, "restart editor is window scoped")
expect(EditorCommandRouter.scope(for: "learnMarkdown") == .app, "learn Markdown is app scoped")

// Session consumes the editor-web state payload without trusting malformed data.
let payload = EditorCommandStatePayload.decode([
    "sourceMode": true,
    "readOnly": false,
    "frontMatter": true,
    "mathBlock": true,
    "expandedSource": true,
])
expect(payload.sourceMode, "payload source mode should decode")
expect(!payload.readOnly, "payload read-only should decode")
expect(payload.frontMatter, "payload front matter should decode")
expect(payload.mathBlock, "payload math block should decode")
expect(payload.expandedSource, "payload expanded source should decode")
expect(EditorCommandStatePayload.decode(nil).frontMatter == false, "missing front matter defaults to false")
expect(EditorCommandStatePayload.decode(nil).expandedSource == false, "missing expanded source defaults to false")

print("PASS")
