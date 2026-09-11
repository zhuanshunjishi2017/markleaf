import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

func context(_ actions: [String: EditorActionState], semantic: EditorSemanticContext = .ordinaryBlock) -> EditorContextMenuState {
    EditorContextMenuState(
        isSourceMode: false, isReadOnly: false, isPlainText: false,
        footnoteDefinitionLabel: nil, inTable: false, mermaidSelected: false,
        mermaidCount: 0, imageSelected: false, mathInline: false, mathBlock: false,
        codeBlock: false, codeBlockText: nil, actions: actions, semanticContext: semantic
    )
}

let projection = EditorActionState.decode([
    "setCodeBlockLanguage": ["enabled": true, "checked": false],
    "deleteTable": ["enabled": false, "checked": true],
    "copyCodeBlock": ["enabled": true, "checked": false],
])
expect(projection["deleteTable"] == EditorActionState(enabled: false, checked: true), "decode both action fields")
expect(EditorMenuPolicy.allows(.declareCodeLanguage, state: context(projection)), "map native command to the core identifier")
expect(EditorMenuPolicy.allows(.copyCodeBlock, state: context(projection)), "honor projected availability without rebuilding a code-block rule")
expect(!EditorMenuPolicy.allows(.deleteTable, state: context(projection)), "honor disabled actions")
expect(!EditorMenuPolicy.allows(.insertMermaid, state: context(projection)), "missing action is disabled")
expect(EditorActionState.decode(nil).isEmpty, "missing projection has no enabled actions")
expect(EditorActionState.decode(["copy": ["enabled": "true", "checked": false]]).isEmpty, "malformed action is not accepted")
expect(EditorMenuPolicy.semanticContext(for: context(projection, semantic: .math)) == .math, "consume projected context")
let menu = NSMenu()
EditorContextMenuState.preserveExplicitAvailability(in: menu)
expect(!menu.autoenablesItems, "AppKit must preserve the core's explicit availability")
print("PASS")
