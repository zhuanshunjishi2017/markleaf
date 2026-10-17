import AppKit

extension EditorSession {
    func toggleCodeBlockWithLanguage() {
        guard !isReadOnly, !isPlainText, !isSourceMode else { return }
        execute("toggleCodeBlock")
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

        let languagePopup = NSPopUpButton(frame: NSRect(x: 0, y: 32, width: 360, height: 28))
        languagePopup.addItems(withTitles: [L10n.t("未指定")] + CodeBlockLanguageCatalog.commonLanguages)

        let customLanguageField = NSTextField(frame: NSRect(x: 0, y: 0, width: 360, height: 26))
        customLanguageField.placeholderString = L10n.t("自定义语言（可选）")

        let accessory = NSView(frame: NSRect(x: 0, y: 0, width: 360, height: 60))
        accessory.addSubview(languagePopup)
        accessory.addSubview(customLanguageField)
        languagePopup.autoresizingMask = [.width]
        customLanguageField.autoresizingMask = [.width]

        if let initialLanguage = CodeBlockLanguageCatalog.commonLanguages.first(where: {
            $0.caseInsensitiveCompare(initialLanguage) == .orderedSame
        }) {
            languagePopup.selectItem(withTitle: initialLanguage)
        } else if !initialLanguage.isEmpty {
            customLanguageField.stringValue = initialLanguage
        } else {
            languagePopup.selectItem(withTitle: L10n.t("未指定"))
        }

        alert.accessoryView = accessory
        alert.window.initialFirstResponder = languagePopup

        alert.beginSheetModal(for: window) { response in
            guard response == .alertFirstButtonReturn else {
                completion(nil)
                return
            }
            let customLanguage = CodeBlockLanguageCatalog.normalized(customLanguageField.stringValue)
            let popupLanguage = CodeBlockLanguageCatalog.normalized(languagePopup.titleOfSelectedItem ?? "")
            let normalized = customLanguage.isEmpty ? popupLanguage : customLanguage
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
