import AppKit

final class FontSettingsWindowController: NSWindowController, NSTextFieldDelegate {
    private var cjkField: FontField!
    private var westernField: FontField!
    private let sizeField = NSTextField(string: "14")
    private let cjkLanguagePopup = NSPopUpButton()
    private var sizeFieldEditingOriginal: String?
    private var fontSettingsKeyMonitor: Any?
    private(set) var cjkFontFamily: String
    private(set) var westernFontFamily: String
    private(set) var fontSize: Int
    private(set) var cjkLanguageTag: CJKLanguageTag
    private(set) var accepted = false

    init(
        cjkFontFamily: String,
        westernFontFamily: String,
        fontSize: Int,
        cjkLanguageTag: CJKLanguageTag
    ) {
        self.cjkFontFamily = cjkFontFamily
        self.westernFontFamily = westernFontFamily
        self.fontSize = fontSize
        self.cjkLanguageTag = cjkLanguageTag
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 500, height: 230),
            styleMask: [.titled, .closable], backing: .buffered, defer: false
        )
        window.title = L10n.t("字体设置")
        window.center()
        super.init(window: window)

        cjkField = FontField(fontName: cjkFontFamily) { [weak self] in self?.cjkFontFamily = $0 }
        westernField = FontField(fontName: westernFontFamily) { [weak self] in self?.westernFontFamily = $0 }
        sizeField.stringValue = "\(fontSize)"
        // 与偏好设置里的数值字段保持一致：圆角样式 + 失焦校验，不使用会拒绝中间输入的格式化器。
        sizeField.bezelStyle = .roundedBezel
        sizeField.alignment = .center
        sizeField.widthAnchor.constraint(equalToConstant: 80).isActive = true
        sizeField.delegate = self
        cjkLanguagePopup.addItems(withTitles: [
            L10n.t("简体中文"), L10n.t("繁体中文"), L10n.t("日文字形"), L10n.t("韩文字形"),
        ])
        cjkLanguagePopup.selectItem(at: CJKLanguageTag.allCases.firstIndex(of: cjkLanguageTag) ?? 0)

        let form = NSGridView(views: [
            [NSTextField(labelWithString: L10n.t("中文字体")), cjkField],
            [NSTextField(labelWithString: L10n.t("西文字体")), westernField],
            [NSTextField(labelWithString: L10n.t("基础字号")), sizeField],
            [NSTextField(labelWithString: L10n.t("汉字优先字型")), cjkLanguagePopup],
        ])
        form.rowSpacing = 12
        form.columnSpacing = 12
        let cancel = NSButton(title: L10n.t("取消"), target: self, action: #selector(cancelAction))
        let ok = NSButton(title: L10n.t("确定"), target: self, action: #selector(okAction))
        ok.keyEquivalent = "\r"
        let buttons = NSStackView(views: [NSView(), cancel, ok])
        buttons.orientation = .horizontal
        let stack = NSStackView(views: [form, buttons])
        stack.orientation = .vertical
        stack.spacing = 18
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 16, right: 20)
        stack.translatesAutoresizingMaskIntoConstraints = false
        // 按内容自适应窗口大小，避免 500×230 的固定尺寸让设置项之外显得空旷。
        let fitting = stack.fittingSize
        window.contentView = stack
        window.setContentSize(fitting)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor),
            stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor),
            stack.bottomAnchor.constraint(equalTo: window.contentView!.bottomAnchor),
        ])

        // 编辑文本框时按 Esc 取消本次修改并还原原值；未在编辑时 Esc 关闭窗口。
        fontSettingsKeyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.window === self.window, event.keyCode == 53 else { return event }
            if let field = self.editingTextField {
                if let original = self.sizeFieldEditingOriginal {
                    field.stringValue = original
                }
                self.sizeFieldEditingOriginal = nil
                self.window?.makeFirstResponder(nil)
                return nil
            }
            self.cancelAction()
            return nil
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    deinit {
        if let fontSettingsKeyMonitor {
            NSEvent.removeMonitor(fontSettingsKeyMonitor)
        }
    }

    func runModal() -> Bool {
        guard let window else { return false }
        NSApp.runModal(for: window)
        window.orderOut(nil)
        return accepted
    }

    /// 当前正在编辑的文本框（含字段编辑器场景）。
    private var editingTextField: NSTextField? {
        guard let responder = window?.firstResponder else { return nil }
        if let field = responder as? NSTextField {
            return field
        }
        if let editor = responder as? NSTextView {
            return editor.delegate as? NSTextField
        }
        return nil
    }

    func controlTextDidBeginEditing(_ notification: Notification) {
        guard let field = notification.object as? NSTextField else { return }
        sizeFieldEditingOriginal = field.stringValue
    }

    func controlTextDidEndEditing(_ notification: Notification) {
        guard let field = notification.object as? NSTextField else { return }
        let original = sizeFieldEditingOriginal
        sizeFieldEditingOriginal = nil
        guard let value = Int(field.stringValue.trimmingCharacters(in: .whitespaces)),
              AppSettings.sourceFontSizeRange.contains(value) else {
            // 焦点离开时立即校验：无效值弹窗提示并还原为编辑前的有效值。
            if let original {
                field.stringValue = original
            }
            presentInvalidFontSizeAlert()
            return
        }
        field.stringValue = "\(value)"
    }

    private func presentInvalidFontSizeAlert() {
        let alert = NSAlert()
        alert.messageText = L10n.f(
            "“%@”需要填写有效的数值（%@）",
            L10n.t("基础字号"),
            "\(AppSettings.sourceFontSizeRange.lowerBound)–\(AppSettings.sourceFontSizeRange.upperBound)"
        )
        alert.alertStyle = .warning
        alert.addButton(withTitle: L10n.t("好"))
        if let window {
            alert.beginSheetModal(for: window)
        }
    }

    @objc private func okAction() {
        guard let value = Int(sizeField.stringValue.trimmingCharacters(in: .whitespaces)),
              AppSettings.sourceFontSizeRange.contains(value) else {
            presentInvalidFontSizeAlert()
            return
        }
        fontSize = value
        if cjkLanguagePopup.indexOfSelectedItem >= 0,
           cjkLanguagePopup.indexOfSelectedItem < CJKLanguageTag.allCases.count {
            cjkLanguageTag = CJKLanguageTag.allCases[cjkLanguagePopup.indexOfSelectedItem]
        }
        accepted = true
        NSApp.stopModal()
    }

    @objc private func cancelAction() {
        accepted = false
        NSApp.stopModal()
    }
}
