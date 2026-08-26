import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let readOnly = EditorMenuPolicy.commands(isSourceMode: false, isReadOnly: true)
expect(readOnly.contains("copy"), "read-only menu should keep copy")
expect(readOnly.contains("selectAll"), "read-only menu should keep select all")
expect(!readOnly.contains("paste"), "read-only menu should hide paste")
expect(!readOnly.contains("toggleBold"), "read-only menu should hide formatting")

let editableSource = EditorMenuPolicy.commands(isSourceMode: true, isReadOnly: false)
expect(editableSource.contains("pastePlainText"), "editable source menu should expose paste as plain text")
expect(editableSource.contains("undo"), "editable source menu should expose undo")

// 启用规则：剪切/拷贝/复制为* 需要选中；粘贴/粘贴为纯文本需要剪贴板内容；只读限制写入类命令。
expect(!EditorMenuPolicy.isEnabled(command: "cut", hasSelection: false, clipboardHasContent: true, isReadOnly: false),
       "cut should be disabled without selection")
expect(EditorMenuPolicy.isEnabled(command: "cut", hasSelection: true, clipboardHasContent: true, isReadOnly: false),
       "cut should be enabled with selection")
expect(!EditorMenuPolicy.isEnabled(command: "copy", hasSelection: false, clipboardHasContent: true, isReadOnly: false),
       "copy should be disabled without selection")
expect(!EditorMenuPolicy.isEnabled(command: "copyAs", hasSelection: false, clipboardHasContent: true, isReadOnly: false),
       "copy as submenu should be disabled without selection")
expect(EditorMenuPolicy.isEnabled(command: "copyAs", hasSelection: true, clipboardHasContent: false, isReadOnly: true),
       "copy as submenu should stay available in read-only documents with selection")
expect(EditorMenuPolicy.isEnabled(command: "copyMarkdown", hasSelection: true, clipboardHasContent: false, isReadOnly: false),
       "copy as markdown should require selection only")
expect(EditorMenuPolicy.isEnabled(command: "copyPlain", hasSelection: true, clipboardHasContent: false, isReadOnly: false),
       "copy as plain text should require selection only")
expect(!EditorMenuPolicy.isEnabled(command: "paste", hasSelection: true, clipboardHasContent: false, isReadOnly: false),
       "paste should be disabled without clipboard content")
expect(EditorMenuPolicy.isEnabled(command: "paste", hasSelection: false, clipboardHasContent: true, isReadOnly: false),
       "paste should not require selection")
expect(EditorMenuPolicy.isEnabled(command: "pastePlainText", hasSelection: false, clipboardHasContent: true, isReadOnly: false),
       "paste as plain text should follow clipboard availability")
expect(EditorMenuPolicy.isEnabled(command: "copy", hasSelection: true, clipboardHasContent: true, isReadOnly: true),
       "copy should stay available in read-only documents with selection")
expect(!EditorMenuPolicy.isEnabled(command: "copy", hasSelection: false, clipboardHasContent: true, isReadOnly: true),
       "copy in read-only documents should require selection")
expect(!EditorMenuPolicy.isEnabled(command: "paste", hasSelection: true, clipboardHasContent: true, isReadOnly: true),
       "paste should be disabled in read-only documents")
expect(EditorMenuPolicy.isEnabled(command: "selectAll", hasSelection: false, clipboardHasContent: false, isReadOnly: true),
       "select all should stay available in read-only documents")

final class MenuActionTarget: NSObject {
    @objc func performAction(_ sender: NSMenuItem) {}
}

let contextMenu = NSMenu()
let target = MenuActionTarget()
let disabledCopy = NSMenuItem(
    title: "Copy",
    action: #selector(MenuActionTarget.performAction(_:)),
    keyEquivalent: ""
)
disabledCopy.target = target
disabledCopy.isEnabled = false
contextMenu.addItem(disabledCopy)
EditorContextMenuState.preserveExplicitAvailability(in: contextMenu)
contextMenu.update()
expect(!contextMenu.autoenablesItems, "context menus should preserve explicit clipboard availability")
expect(!disabledCopy.isEnabled, "AppKit should not re-enable copy when there is no selection")

// 脚注命令：插入在可编辑文档可用；重设编号仅在光标位于脚注定义段落时可用。
expect(EditorMenuPolicy.isFootnoteCommandEnabled(command: "insertFootnote", hasFootnoteLabel: false, isReadOnly: false),
       "insert footnote should be enabled in editable documents")
expect(!EditorMenuPolicy.isFootnoteCommandEnabled(command: "insertFootnote", hasFootnoteLabel: true, isReadOnly: true),
       "insert footnote should be disabled in read-only documents")
expect(!EditorMenuPolicy.isFootnoteCommandEnabled(command: "resetFootnoteLabel", hasFootnoteLabel: false, isReadOnly: false),
       "reset footnote label should require a footnote definition")
expect(EditorMenuPolicy.isFootnoteCommandEnabled(command: "resetFootnoteLabel", hasFootnoteLabel: true, isReadOnly: false),
       "reset footnote label should be enabled on a footnote definition")
expect(!EditorMenuPolicy.isFootnoteCommandEnabled(
    command: "insertFootnote", hasFootnoteLabel: false, isReadOnly: false, isSourceMode: true),
    "insert footnote should be disabled in source mode like other paragraph commands")

// 行内格式：粗体/斜体/下划线/删除线必须有真实选区；行内代码在空选时通过输入框插入。
for command in ["toggleBold", "toggleItalic", "toggleUnderline", "toggleStrike"] {
    expect(!EditorMenuPolicy.isInlineFormatCommandEnabled(
        command: command, hasSelection: false, isSourceMode: false, isReadOnly: false),
        "\(command) should be disabled without a selection")
    expect(EditorMenuPolicy.isInlineFormatCommandEnabled(
        command: command, hasSelection: true, isSourceMode: false, isReadOnly: false),
        "\(command) should be enabled with a selection")
}
expect(EditorMenuPolicy.isInlineFormatCommandEnabled(
    command: "toggleCode", hasSelection: false, isSourceMode: false, isReadOnly: false),
    "inline code should remain available without a selection because it opens an insertion dialog")
expect(!EditorMenuPolicy.isInlineFormatCommandEnabled(
    command: "toggleCode", hasSelection: true, isSourceMode: true, isReadOnly: false),
    "inline formatting should be disabled in source mode")
expect(!EditorMenuPolicy.isInlineFormatCommandEnabled(
    command: "toggleCode", hasSelection: true, isSourceMode: false, isReadOnly: true),
    "inline formatting should be disabled in read-only documents")

// 段落命令不依赖文本选区：空选时作用于当前段落；源码/只读模式不可用。
expect(EditorMenuPolicy.isParagraphCommandEnabled(
    command: "setParagraph", isSourceMode: false, isReadOnly: false),
    "paragraph commands should apply to the current block without a text selection")
expect(EditorMenuPolicy.isParagraphCommandEnabled(
    command: "toggleBlockquote", isSourceMode: false, isReadOnly: false),
    "paragraph transforms should share one editable visual-mode policy")
expect(!EditorMenuPolicy.isParagraphCommandEnabled(
    command: "setHeading1", isSourceMode: true, isReadOnly: false),
    "paragraph commands should be disabled in source mode")
expect(!EditorMenuPolicy.isParagraphCommandEnabled(
    command: "toggleCodeBlock", isSourceMode: false, isReadOnly: true),
    "paragraph commands should be disabled in read-only documents")

expect(!EditorMenuPolicy.isHeadingLevelCommandEnabled(command: "promoteHeading", headingLevel: nil),
       "promote heading should be disabled outside a heading")
expect(EditorMenuPolicy.isHeadingLevelCommandEnabled(command: "promoteHeading", headingLevel: 2),
       "promote heading should be enabled for levels 2 through 6")
expect(!EditorMenuPolicy.isHeadingLevelCommandEnabled(command: "promoteHeading", headingLevel: 1),
       "promote heading should be disabled at level 1")
expect(!EditorMenuPolicy.isHeadingLevelCommandEnabled(command: "demoteHeading", headingLevel: nil),
       "demote heading should be disabled outside a heading")
expect(EditorMenuPolicy.isHeadingLevelCommandEnabled(command: "demoteHeading", headingLevel: 5),
       "demote heading should be enabled for levels 1 through 5")
expect(!EditorMenuPolicy.isHeadingLevelCommandEnabled(command: "demoteHeading", headingLevel: 6),
       "demote heading should be disabled at level 6")

func contextState(
    sourceMode: Bool = false,
    readOnly: Bool = false,
    plainText: Bool = false,
    footnoteLabel: String? = nil,
    inTable: Bool = false,
    mermaidSelected: Bool = false,
    mermaidCount: Int = 0,
    imageSelected: Bool = false,
    mathInline: Bool = false,
    mathBlock: Bool = false,
    codeBlock: Bool = false,
    codeBlockText: String? = nil
) -> EditorContextMenuState {
    EditorContextMenuState(
        isSourceMode: sourceMode,
        isReadOnly: readOnly,
        isPlainText: plainText,
        footnoteDefinitionLabel: footnoteLabel,
        inTable: inTable,
        mermaidSelected: mermaidSelected,
        mermaidCount: mermaidCount,
        imageSelected: imageSelected,
        mathInline: mathInline,
        mathBlock: mathBlock,
        codeBlock: codeBlock,
        codeBlockText: codeBlockText
    )
}

// 语义上下文必须按脚注、表格、Mermaid、图片、公式、代码块、普通块的顺序分类。
expect(EditorMenuPolicy.semanticContext(for: contextState(
    footnoteLabel: "1", inTable: true, mermaidSelected: true, imageSelected: true, mathBlock: true, codeBlock: true
)) == .footnoteDefinition, "footnote definitions should have the highest context priority")
expect(EditorMenuPolicy.semanticContext(for: contextState(
    inTable: true, mermaidSelected: true, imageSelected: true, mathInline: true, codeBlock: true
)) == .table, "tables should win over embedded block types")
expect(EditorMenuPolicy.semanticContext(for: contextState(
    mermaidSelected: true, imageSelected: true, mathBlock: true, codeBlock: true
)) == .mermaid, "Mermaid should not fall through to the generic code-block context")
expect(EditorMenuPolicy.semanticContext(for: contextState(imageSelected: true, mathBlock: true, codeBlock: true)) == .image,
       "images should win over math and code blocks")
expect(EditorMenuPolicy.semanticContext(for: contextState(mathInline: true, codeBlock: true)) == .math,
       "math should win over code blocks")
expect(EditorMenuPolicy.semanticContext(for: contextState(codeBlock: true)) == .codeBlock,
       "ordinary fenced code should use the code-block context")
expect(EditorMenuPolicy.semanticContext(for: contextState()) == .ordinaryBlock,
       "unclassified content should use the ordinary block context")
expect(EditorMenuPolicy.semanticContext(for: contextState(sourceMode: true, mermaidSelected: true, codeBlock: true)) == .ordinaryBlock,
       "source mode should not expose visual block semantics")

// Mermaid、代码块、脚注和表格命令统一由策略矩阵决定。
let editableMermaid = contextState(mermaidSelected: true, mermaidCount: 2, codeBlock: true)
expect(EditorMenuPolicy.allows(.insertMermaid, state: editableMermaid), "editable Markdown should allow Mermaid insertion")
expect(EditorMenuPolicy.allows(.editMermaid, state: editableMermaid), "selected Mermaid should allow editing")
expect(EditorMenuPolicy.allows(.rerenderMermaid, state: editableMermaid), "selected Mermaid should allow rerendering")
expect(EditorMenuPolicy.allows(.rerenderAllMermaid, state: editableMermaid), "documents with Mermaid should allow rerender all")
expect(EditorMenuPolicy.allows(.deleteMermaid, state: editableMermaid), "selected Mermaid should allow deletion")

let readOnlyMermaid = contextState(readOnly: true, mermaidSelected: true, mermaidCount: 1, codeBlock: true)
expect(!EditorMenuPolicy.allows(.insertMermaid, state: readOnlyMermaid), "read-only documents must reject Mermaid insertion")
expect(!EditorMenuPolicy.allows(.editMermaid, state: readOnlyMermaid), "read-only documents must reject Mermaid editing")
expect(!EditorMenuPolicy.allows(.rerenderMermaid, state: readOnlyMermaid), "read-only documents must not open selected-Mermaid context actions")
expect(EditorMenuPolicy.allows(.rerenderAllMermaid, state: readOnlyMermaid), "read-only documents may rerender all Mermaid charts")
expect(!EditorMenuPolicy.allows(.deleteMermaid, state: readOnlyMermaid), "read-only documents must reject Mermaid deletion")

let plainText = contextState(plainText: true, mermaidCount: 1)
expect(!EditorMenuPolicy.allows(.insertMermaid, state: plainText), "plain-text documents must reject Mermaid insertion")
expect(!EditorMenuPolicy.allows(.rerenderAllMermaid, state: plainText), "plain-text documents have no Mermaid rendering context")

let source = contextState(sourceMode: true, mermaidSelected: true, mermaidCount: 1, codeBlock: true, codeBlockText: "x")
expect(EditorMenuPolicy.allows(.insertMermaid, state: source), "writable Markdown source mode should allow Mermaid insertion")
expect(!EditorMenuPolicy.allows(.editMermaid, state: source), "source mode must not expose selected-Mermaid commands")
expect(!EditorMenuPolicy.allows(.rerenderAllMermaid, state: source), "source mode must not expose Mermaid rendering commands")
expect(!EditorMenuPolicy.allows(.declareCodeLanguage, state: source), "source mode must not expose visual code-block metadata commands")

let editableCode = contextState(codeBlock: true, codeBlockText: "let leaf = 1")
expect(EditorMenuPolicy.allows(.declareCodeLanguage, state: editableCode), "editable code blocks should allow language declaration")
expect(EditorMenuPolicy.allows(.copyCodeBlock, state: editableCode), "code blocks with text should allow whole-block copy")
expect(!EditorMenuPolicy.allows(.declareCodeLanguage, state: contextState(readOnly: true, codeBlock: true, codeBlockText: "x")),
       "read-only code blocks must reject language changes")
expect(EditorMenuPolicy.allows(.copyCodeBlock, state: contextState(readOnly: true, codeBlock: true, codeBlockText: "")),
       "an empty read-only code block should remain copyable")
expect(!EditorMenuPolicy.allows(.copyCodeBlock, state: contextState(codeBlock: true)),
       "copy whole code block requires a decoded code-block text value")

let footnote = contextState(footnoteLabel: "note")
for command in [EditorNativeCommand.goToFootnoteReference, .resetFootnoteNumber, .clearFootnoteReferences, .deleteFootnote] {
    expect(EditorMenuPolicy.allows(command, state: footnote), "editable footnote definitions should allow \(command)")
    expect(!EditorMenuPolicy.allows(command, state: contextState(readOnly: true, footnoteLabel: "note")),
           "read-only footnote definitions must reject \(command)")
}

let table = contextState(inTable: true)
for command in [EditorNativeCommand.tableCaption, .tableRows, .tableColumns, .tableAlignment, .deleteTable] {
    expect(EditorMenuPolicy.allows(command, state: table), "editable tables should allow \(command)")
    expect(!EditorMenuPolicy.allows(command, state: contextState(readOnly: true, inTable: true)),
           "read-only tables must reject \(command)")
}

print("PASS")
