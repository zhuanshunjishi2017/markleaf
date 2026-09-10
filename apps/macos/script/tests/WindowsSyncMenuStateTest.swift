import AppKit
import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func contextState(
    sourceMode: Bool = false,
    readOnly: Bool = false,
    plainText: Bool = false,
    inTable: Bool = false,
    mermaidSelected: Bool = false,
    mermaidCount: Int = 0,
    imageSelected: Bool = false,
    mathInline: Bool = false,
    mathBlock: Bool = false,
    codeBlock: Bool = false,
    codeBlockText: String? = nil,
    frontMatterActive: Bool = false,
    expandedSource: Bool = false
) -> EditorContextMenuState {
    EditorContextMenuState(
        isSourceMode: sourceMode,
        isReadOnly: readOnly,
        isPlainText: plainText,
        footnoteDefinitionLabel: nil,
        inTable: inTable,
        mermaidSelected: mermaidSelected,
        mermaidCount: mermaidCount,
        imageSelected: imageSelected,
        mathInline: mathInline,
        mathBlock: mathBlock,
        codeBlock: codeBlock,
        codeBlockText: codeBlockText,
        frontMatterActive: frontMatterActive,
        expandedSource: expandedSource
    )
}

// Command scopes match the migration contract.
expect(EditorCommandRouter.scope(for: "toggleHighlight") == .document, "highlight is document scoped")
expect(EditorCommandRouter.scope(for: "insertAlertNote") == .document, "alerts are document scoped")
expect(EditorCommandRouter.scope(for: "showFrontMatter") == .document, "front matter is document scoped")
expect(EditorCommandRouter.scope(for: "setMathNumber") == .document, "math numbering is document scoped")
expect(EditorCommandRouter.scope(for: "copyHtml") == .document, "copy HTML is document scoped")
expect(EditorCommandRouter.scope(for: "restartEditor") == .windowLevel, "restart editor is window scoped")
expect(EditorCommandRouter.scope(for: "learnMarkdown") == .app, "learn Markdown is app scoped")

// Markdown visual/source/read-only/plain-text state matrix.
expect(EditorMenuPolicy.isInlineFormatCommandEnabled(
    command: "toggleHighlight", hasSelection: false, isSourceMode: false, isReadOnly: false
), "highlight is available in visual Markdown without a selection")
expect(!EditorMenuPolicy.isInlineFormatCommandEnabled(
    command: "toggleHighlight", hasSelection: true, isSourceMode: true, isReadOnly: false
), "highlight is disabled in source mode")

for command in ["insertAlertNote", "insertAlertTip", "insertAlertImportant", "insertAlertWarning", "insertAlertCaution"] {
    expect(EditorMenuPolicy.isParagraphCommandEnabled(
        command: command, isSourceMode: false, isReadOnly: false, inTable: false, isPlainText: false
    ), "\(command) is available in visual Markdown")
    expect(!EditorMenuPolicy.isParagraphCommandEnabled(
        command: command, isSourceMode: true, isReadOnly: false, inTable: false, isPlainText: false
    ), "\(command) is disabled in source mode")
    expect(!EditorMenuPolicy.isParagraphCommandEnabled(
        command: command, isSourceMode: false, isReadOnly: true, inTable: false, isPlainText: false
    ), "\(command) is disabled when read-only")
    expect(!EditorMenuPolicy.isParagraphCommandEnabled(
        command: command, isSourceMode: false, isReadOnly: false, inTable: true, isPlainText: false
    ), "\(command) is disabled in tables")
}

expect(EditorMenuPolicy.isMathNumberCommandEnabled(
    mathBlock: true, isSourceMode: false, isReadOnly: false
), "math numbering is available for a visual block formula")
expect(!EditorMenuPolicy.isMathNumberCommandEnabled(
    mathBlock: false, isSourceMode: false, isReadOnly: false
), "math numbering requires a block formula")
expect(!EditorMenuPolicy.isMathNumberCommandEnabled(
    mathBlock: true, isSourceMode: true, isReadOnly: false
), "math numbering is disabled in source mode")
expect(!EditorMenuPolicy.isMathNumberCommandEnabled(
    mathBlock: true, isSourceMode: false, isReadOnly: true
), "math numbering is disabled when read-only")

expect(EditorMenuPolicy.isCopyHtmlEnabled(
    hasSelection: true, isSourceMode: false, isReadOnly: true
), "copy HTML remains available for read-only selected Markdown")
expect(!EditorMenuPolicy.isCopyHtmlEnabled(
    hasSelection: true, isSourceMode: true, isReadOnly: false
), "copy HTML is disabled in source mode")

// Context precedence and YAML code-like handling.
expect(EditorMenuPolicy.semanticContext(for: contextState(
    mathBlock: true, codeBlock: true
)) == .math, "math has higher context precedence than a generic code block")
expect(EditorMenuPolicy.semanticContext(for: contextState(
    codeBlock: true, frontMatterActive: true
)) == .frontMatter, "YAML front matter has its own code-like context")
expect(EditorMenuPolicy.allows(.copyCodeBlock, state: contextState(
    codeBlockText: "title: MarkLeaf", frontMatterActive: true
)), "YAML source can be copied as a code block")

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
