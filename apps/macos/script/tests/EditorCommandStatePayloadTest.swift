import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let complete = EditorCommandStatePayload.decode([
    "sourceMode": true,
    "readOnly": true,
    "codeBlock": true,
    "codeBlockLanguage": "swift",
    "codeBlockText": "let leaf = 1",
    "mermaidSelected": true,
    "mermaidSource": "graph TD\n  A-->B",
    "mermaidCount": 2,
])
expect(complete.sourceMode, "source mode should decode")
expect(complete.readOnly, "read-only should decode")
expect(complete.codeBlock, "code-block state should decode")
expect(complete.codeBlockLanguage == "swift", "code-block language should decode")
expect(complete.codeBlockText == "let leaf = 1", "complete code text should decode")
expect(complete.mermaidSelected, "Mermaid selection should decode")
expect(complete.mermaidSource == "graph TD\n  A-->B", "Mermaid source should decode")
expect(complete.mermaidCount == 2, "Mermaid count should decode")

let missing = EditorCommandStatePayload.decode(nil)
expect(missing == EditorCommandStatePayload(
    sourceMode: false,
    readOnly: false,
    codeBlock: false,
    codeBlockLanguage: nil,
    codeBlockText: nil,
    mermaidSelected: false,
    mermaidSource: nil,
    mermaidCount: 0
), "missing optional fields should use safe defaults")

let malformed = EditorCommandStatePayload.decode([
    "sourceMode": "yes",
    "readOnly": 1,
    "codeBlock": NSNull(),
    "codeBlockLanguage": 7,
    "codeBlockText": NSNull(),
    "mermaidSelected": "true",
    "mermaidSource": false,
    "mermaidCount": -4,
])
expect(malformed == missing, "wrong types and negative counts should use safe defaults")

let emptyText = EditorCommandStatePayload.decode(["codeBlockText": ""])
expect(emptyText.codeBlockText == "", "an empty code block must remain distinct from a missing value")

print("PASS")
