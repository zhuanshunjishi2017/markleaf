import AppKit

private final class FootnoteLabelEditingController: NSObject, NSTextFieldDelegate {
    private var editingOriginal = ""
    private var cancelingCurrentEdit = false
    private let onInvalid: (NSTextField) -> Void

    init(onInvalid: @escaping (NSTextField) -> Void) {
        self.onInvalid = onInvalid
    }

    func controlTextDidBeginEditing(_ notification: Notification) {
        guard let field = notification.object as? NSTextField else { return }
        editingOriginal = field.stringValue
        field.textColor = .labelColor
        field.toolTip = nil
    }

    func controlTextDidEndEditing(_ notification: Notification) {
        guard let field = notification.object as? NSTextField else { return }
        if cancelingCurrentEdit {
            cancelingCurrentEdit = false
            return
        }
        guard let normalized = FootnoteLabelPolicy.normalized(field.stringValue) else {
            field.stringValue = editingOriginal
            onInvalid(field)
            return
        }
        field.stringValue = normalized
    }

    func cancelCurrentEdit(in field: NSTextField) {
        cancelingCurrentEdit = true
        field.stringValue = editingOriginal
        field.textColor = .labelColor
        field.toolTip = nil
    }
}

extension EditorSession {
    // MARK: - 插入脚注（对应 Windows InsertFootnote）

    func insertFootnote() {
        guard webView?.window != nil else { return }
        presentFootnoteInputDialog { [weak self] label, note in
            guard let self, let window = self.webView?.window, let label, let note else { return }
            self.footnoteLabelExists(label) { exists in
                DispatchQueue.main.async {
                    guard exists else {
                        self.executeFootnoteInsert(label: label, note: note)
                        return
                    }
                    let alert = NSAlert()
                    alert.messageText = L10n.t("注释编号重复")
                    alert.informativeText = L10n.f("已存在编号为“%@”的注释。仍要继续插入重复的编号？", label)
                    alert.alertStyle = .warning
                    alert.addButton(withTitle: L10n.t("确定"))
                    alert.addButton(withTitle: L10n.t("取消"))
                    alert.beginSheetModal(for: window) { response in
                        if response == .alertFirstButtonReturn {
                            self.executeFootnoteInsert(label: label, note: note)
                        } else {
                            // 与 Windows 一致：取消重复警告后重新打开输入框。
                            self.insertFootnote()
                        }
                    }
                }
            }
        }
    }

    private func executeFootnoteInsert(label: String, note: String) {
        guard let payload = footnotePayload(["label": label, "note": note]) else { return }
        execute("insertFootnote", text: payload)
        statusText = L10n.t("已插入注释")
    }

    // MARK: - 重设脚注编号（对应 Windows ResetFootnoteLabel）

    func resetFootnoteLabel() {
        guard let window = webView?.window,
              let oldLabel = footnoteDefinitionLabel, !oldLabel.isEmpty else { return }
        let alert = NSAlert()
        alert.messageText = L10n.t("重设注释编号")
        alert.informativeText = L10n.t("新的注释编号：")
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))
        let field = NSTextField(string: oldLabel)
        field.frame = NSRect(x: 0, y: 0, width: 280, height: 24)
        alert.accessoryView = field
        alert.window.initialFirstResponder = field
        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self else { return }
            let newLabel = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !newLabel.isEmpty, newLabel != oldLabel,
                  let payload = self.footnotePayload(["oldLabel": oldLabel, "newLabel": newLabel]) else { return }
            self.execute("resetFootnoteLabel", text: payload)
            self.statusText = L10n.t("已重设注释编号")
        }
    }

    // MARK: - 对话框

    private func presentFootnoteInputDialog(completion: @escaping (String?, String?) -> Void) {
        guard let window = webView?.window else {
            completion(nil, nil)
            return
        }
        let alert = NSAlert()
        alert.messageText = L10n.t("插入注释")
        alert.informativeText = L10n.t("输入注释编号和注释文本：")
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))
        let okButton = alert.buttons.first
        okButton?.isEnabled = false

        let labelField = NSTextField(string: "")
        labelField.placeholderString = "1"
        labelField.bezelStyle = .roundedBezel
        DialogTextFieldStyle.apply(to: labelField)
        let noteView = NSTextView(frame: NSRect(x: 0, y: 0, width: 300, height: 70))
        noteView.isEditable = true
        noteView.isRichText = false
        noteView.font = .systemFont(ofSize: 13)
        noteView.textContainerInset = NSSize(width: 2, height: 2)
        noteView.isVerticallyResizable = true

        let accessory = NSView(frame: NSRect(x: 0, y: 0, width: 380, height: 128))
        let labelCaption = NSTextField(labelWithString: L10n.t("注释编号"))
        labelCaption.font = .systemFont(ofSize: 12)
        labelCaption.frame = NSRect(x: 0, y: 100, width: 70, height: 18)
        labelField.frame = NSRect(x: 76, y: 97, width: 304, height: labelField.frame.height)
        let noteCaption = NSTextField(labelWithString: L10n.t("注释文本"))
        noteCaption.font = .systemFont(ofSize: 12)
        noteCaption.frame = NSRect(x: 0, y: 66, width: 70, height: 18)
        let scroll = NSScrollView(frame: NSRect(x: 76, y: 8, width: 304, height: 84))
        scroll.documentView = noteView
        scroll.hasVerticalScroller = true
        scroll.autohidesScrollers = true
        scroll.borderType = .noBorder
        scroll.wantsLayer = true
        scroll.layer?.cornerRadius = 6
        scroll.layer?.borderWidth = 1
        scroll.layer?.borderColor = NSColor.separatorColor.cgColor
        scroll.layer?.masksToBounds = true
        accessory.addSubview(labelCaption)
        accessory.addSubview(labelField)
        accessory.addSubview(noteCaption)
        accessory.addSubview(scroll)
        alert.accessoryView = accessory
        alert.window.initialFirstResponder = labelField

        let labelDelegate = FootnoteLabelEditingController { field in
            NSSound.beep()
            field.textColor = .systemRed
            field.toolTip = L10n.t("注释编号不能为空，且不能包含 ] 或换行符")
        }
        labelField.delegate = labelDelegate
        let keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            guard event.window === alert.window, event.keyCode == 53 else { return event }
            if let editor = alert.window.firstResponder as? NSTextView,
               editor.delegate as? NSTextField === labelField {
                labelDelegate.cancelCurrentEdit(in: labelField)
                alert.window.makeFirstResponder(nil)
                return nil
            }
            return event
        }

        func refreshOK() {
            okButton?.isEnabled = FootnoteLabelPolicy.normalized(labelField.stringValue) != nil
                && !noteView.string.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        }
        refreshOK()
        var tokens: [NSObjectProtocol] = []
        tokens.append(NotificationCenter.default.addObserver(
            forName: NSControl.textDidChangeNotification,
            object: labelField,
            queue: .main
        ) { _ in refreshOK() })
        tokens.append(NotificationCenter.default.addObserver(
            forName: NSText.didChangeNotification,
            object: noteView,
            queue: .main
        ) { _ in refreshOK() })

        alert.beginSheetModal(for: window) { response in
            if let keyMonitor {
                NSEvent.removeMonitor(keyMonitor)
            }
            tokens.forEach { NotificationCenter.default.removeObserver($0) }
            guard response == .alertFirstButtonReturn else {
                completion(nil, nil)
                return
            }
            let label = FootnoteLabelPolicy.normalized(labelField.stringValue)
            let note = noteView.string.trimmingCharacters(in: .whitespacesAndNewlines)
            guard let label, !note.isEmpty else {
                completion(nil, nil)
                return
            }
            completion(label, note)
        }
    }

    func presentFootnoteDefinitionMissingAlert() {
        guard let window = webView?.window else { return }
        let alert = NSAlert()
        alert.messageText = L10n.t("未找到该注释的定义！")
        alert.alertStyle = .warning
        alert.addButton(withTitle: L10n.t("好"))
        alert.beginSheetModal(for: window)
    }

    // MARK: - 重复编号检查（对应 Windows FootnoteLabelExistsAsync）

    private func footnoteLabelExists(_ label: String, completion: @escaping (Bool) -> Void) {
        requestSnapshot { [weak self] result in
            guard let self else { return }
            switch result {
            case .success(let markdown):
                completion(Self.footnoteLabelExists(in: markdown, label: label))
            case .failure:
                self.statusText = L10n.t("无法检查注释编号是否重复")
                completion(false)
            }
        }
    }

    static func footnoteLabelExists(in markdown: String, label: String) -> Bool {
        let pattern = "(?m)^ {0,3}\\[\\^([^\\]\\r\\n]+)\\]:"
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return false }
        let range = NSRange(markdown.startIndex..., in: markdown)
        let trimmed = label.trimmingCharacters(in: .whitespacesAndNewlines)
        for match in regex.matches(in: markdown, range: range) {
            guard let valueRange = Range(match.range(at: 1), in: markdown) else { continue }
            if String(markdown[valueRange]).trimmingCharacters(in: .whitespacesAndNewlines)
                .caseInsensitiveCompare(trimmed) == .orderedSame {
                return true
            }
        }
        return false
    }

    private func footnotePayload(_ dict: [String: String]) -> String? {
        guard let data = try? JSONSerialization.data(withJSONObject: dict),
              let text = String(data: data, encoding: .utf8) else { return nil }
        return text
    }
}
