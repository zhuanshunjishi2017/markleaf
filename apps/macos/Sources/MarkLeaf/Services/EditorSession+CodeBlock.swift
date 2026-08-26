import AppKit

extension EditorSession {
    func declareCodeBlockLanguage() {
        guard EditorMenuPolicy.allows(.declareCodeLanguage, state: editorMenuState),
              let window = webView?.window else { return }

        let alert = NSAlert()
        alert.messageText = L10n.t("代码语言")
        alert.informativeText = L10n.t("输入代码块语言；留空可清除语言声明。")
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))

        let field = NSTextField(string: codeBlockLanguage ?? "")
        field.frame = NSRect(x: 0, y: 0, width: 280, height: 24)
        field.placeholderString = "swift"
        DialogTextFieldStyle.apply(to: field)
        alert.accessoryView = field
        alert.window.initialFirstResponder = field

        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self else { return }
            let language = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            self.execute("setCodeBlockLanguage", text: language)
            self.statusText = L10n.t("代码语言已更新")
        }
    }

    func copyEntireCodeBlock() {
        guard EditorMenuPolicy.allows(.copyCodeBlock, state: editorMenuState),
              let codeBlockText else { return }
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        guard pasteboard.setString(codeBlockText, forType: .string) else {
            statusText = L10n.t("无法复制整段代码")
            return
        }
        statusText = L10n.t("已复制")
    }
}
