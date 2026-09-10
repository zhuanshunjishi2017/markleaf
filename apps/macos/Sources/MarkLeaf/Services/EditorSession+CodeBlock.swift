import AppKit

extension EditorSession {
    func toggleCodeBlockWithLanguage() {
        guard !isReadOnly, !isPlainText, !isSourceMode else { return }
        if editorMenuState.codeBlock {
            execute("toggleCodeBlock")
            return
        }
        showCodeBlockLanguagePicker(initialLanguage: "") { [weak self] language in
            guard let self, let language else { return }
            self.execute("insertCodeBlockWithLanguage", text: language)
        }
    }

    func declareCodeBlockLanguage() {
        guard EditorMenuPolicy.allows(.declareCodeLanguage, state: editorMenuState) else { return }
        showCodeBlockLanguagePicker(initialLanguage: codeBlockLanguage ?? "") { [weak self] language in
            guard let self, let language else { return }
            self.execute("setCodeBlockLanguage", text: language)
            self.statusText = L10n.t("代码语言已更新")
        }
    }

    func setCodeBlockLanguageAt(position: Int, language: String) {
        guard position >= 0 else { return }
        let payload: [String: Any] = [
            "position": position,
            "language": CodeBlockLanguageCatalog.normalized(language),
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: payload),
              let text = String(data: data, encoding: .utf8) else { return }
        execute("setCodeBlockLanguageAt", text: text)
        statusText = L10n.t("代码语言已更新")
    }

    func showCodeBlockLanguagePicker(
        initialLanguage: String,
        completion: @escaping (String?) -> Void
    ) {
        guard let window = webView?.window ?? AppWindowManager.shared.activeWindowController?.window else {
            return
        }
        let alert = NSAlert()
        alert.messageText = L10n.t("代码语言")
        alert.informativeText = L10n.t("选择代码块语言；选择“未指定”可清除语言声明。")
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))

        let combo = NSComboBox()
        combo.frame = NSRect(x: 0, y: 0, width: 320, height: 26)
        combo.isEditable = true
        combo.numberOfVisibleItems = 12
        combo.addItems(withObjectValues: [L10n.t("未指定")] + CodeBlockLanguageCatalog.commonLanguages)
        combo.stringValue = initialLanguage.isEmpty ? L10n.t("未指定") : initialLanguage
        alert.accessoryView = combo
        alert.window.initialFirstResponder = combo

        alert.beginSheetModal(for: window) { response in
            guard response == .alertFirstButtonReturn else {
                completion(nil)
                return
            }
            let normalized = CodeBlockLanguageCatalog.normalized(combo.stringValue)
            completion(normalized == L10n.t("未指定") ? "" : normalized)
        }
    }

    func copyEntireCodeBlock() {
        guard EditorMenuPolicy.allows(.copyCodeBlock, state: editorMenuState),
              let codeBlockText else { return }
        copyCodeBlockText(codeBlockText)
    }

    func copyCodeBlockText(_ text: String) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        guard pasteboard.setString(text, forType: .string) else {
            statusText = L10n.t("无法复制整段代码")
            return
        }
        statusText = L10n.t("已复制")
    }
}
