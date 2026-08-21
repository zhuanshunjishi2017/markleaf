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

print("PASS")
