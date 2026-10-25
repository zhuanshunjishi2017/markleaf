import AppKit

final class VisualSettingsWindowController: NSWindowController, NSTextFieldDelegate {
    private let lineHeightField = NSTextField(string: "1.60")
    private let fontSizeField = NSTextField(string: "16")
    private let maxWidthField = NSTextField(string: "820")
    private let autoSpacingCheck = NSButton(
        checkboxWithTitle: L10n.t("中西文与数字之间自动添加空格"),
        target: nil,
        action: nil
    )
    private let adaptiveJustificationCheck = NSButton(
        checkboxWithTitle: L10n.t("自适应两端对齐"), target: nil, action: nil
    )
    private var monitors: [BoundedTextFieldMonitor] = []
    private var editingOriginals: [NSTextField: String] = [:]
    private var keyMonitor: Any?
    private weak var okButton: NSButton?
    private(set) var lineHeight: Double
    private(set) var fontSize: Int
    private(set) var maxContentWidth: Int
    private(set) var visualCjkAutoSpacing: Bool
    private(set) var visualAdaptiveJustification: Bool
    private(set) var accepted = false

    init(
        lineHeight: Double,
        fontSize: Int,
        maxContentWidth: Int,
        visualCjkAutoSpacing: Bool,
        visualAdaptiveJustification: Bool
    ) {
        self.lineHeight = lineHeight
        self.fontSize = fontSize
        self.maxContentWidth = maxContentWidth
        self.visualCjkAutoSpacing = visualCjkAutoSpacing
        self.visualAdaptiveJustification = visualAdaptiveJustification
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 480, height: 220),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = L10n.t("可视化设置")
        FloatingWindowChrome.configure(window, classification: .navigation)
        window.center()
        super.init(window: window)

        lineHeightField.stringValue = String(format: "%.2f", lineHeight)
        fontSizeField.stringValue = "\(fontSize)"
        maxWidthField.stringValue = "\(maxContentWidth)"
        autoSpacingCheck.state = visualCjkAutoSpacing ? .on : .off
        adaptiveJustificationCheck.state = visualAdaptiveJustification ? .on : .off
        for field in [lineHeightField, fontSizeField, maxWidthField] {
            field.bezelStyle = .roundedBezel
            field.alignment = .center
            field.delegate = self
        }
        monitors = [
            BoundedTextFieldMonitor(
                field: lineHeightField,
                fractionDigits: 2,
                upperBound: Double(AppSettings.visualLineHeightRange.upperBound),
                onChange: { [weak self] in self?.refreshOKButton() }
            ),
            BoundedTextFieldMonitor(
                field: fontSizeField,
                fractionDigits: 0,
                upperBound: Double(AppSettings.visualFontSizeRange.upperBound),
                onChange: { [weak self] in self?.refreshOKButton() }
            ),
            BoundedTextFieldMonitor(
                field: maxWidthField,
                fractionDigits: 0,
                upperBound: Double(AppSettings.visualMaxContentWidthRange.upperBound),
                onChange: { [weak self] in self?.refreshOKButton() }
            ),
        ]

        let fieldWidth: CGFloat = 150
        lineHeightField.widthAnchor.constraint(equalToConstant: fieldWidth).isActive = true
        fontSizeField.widthAnchor.constraint(equalToConstant: fieldWidth).isActive = true
        maxWidthField.widthAnchor.constraint(equalToConstant: fieldWidth).isActive = true

        let labelTitles = [L10n.t("基础行高"), L10n.t("基础字号"), L10n.t("最大内容宽度")]
        let labelWidth = ceil(labelTitles.map {
            ($0 as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 13)]).width
        }.max() ?? 0)
        let rowSpacing: CGFloat = 12
        let groupWidth = labelWidth + rowSpacing + fieldWidth

        func labeledRow(_ title: String, _ field: NSView) -> NSStackView {
            let row = NSStackView(views: [NSTextField(labelWithString: title), field])
            row.orientation = .horizontal
            row.alignment = .centerY
            row.spacing = rowSpacing
            row.translatesAutoresizingMaskIntoConstraints = false
            row.widthAnchor.constraint(equalToConstant: groupWidth).isActive = true
            if let label = row.arrangedSubviews.first as? NSTextField {
                label.alignment = .right
                label.widthAnchor.constraint(equalToConstant: labelWidth).isActive = true
            }
            return row
        }

        let lineHeightRow = labeledRow(L10n.t("基础行高"), lineHeightField)
        let fontSizeRow = labeledRow(L10n.t("基础字号"), fontSizeField)
        let maxWidthRow = labeledRow(L10n.t("最大内容宽度"), maxWidthField)

        let autoSpacingRow = NSStackView(views: [autoSpacingCheck])
        autoSpacingRow.orientation = .horizontal
        autoSpacingRow.alignment = .centerX
        autoSpacingRow.translatesAutoresizingMaskIntoConstraints = false
        autoSpacingRow.widthAnchor.constraint(equalToConstant: groupWidth).isActive = true

        let adaptiveJustificationRow = NSStackView(views: [adaptiveJustificationCheck])
        adaptiveJustificationRow.orientation = .horizontal
        adaptiveJustificationRow.translatesAutoresizingMaskIntoConstraints = false
        adaptiveJustificationRow.widthAnchor.constraint(equalToConstant: groupWidth).isActive = true

        let form = NSStackView(views: [
            lineHeightRow,
            fontSizeRow,
            maxWidthRow,
            autoSpacingRow,
            adaptiveJustificationRow,
        ])
        form.orientation = .vertical
        form.alignment = .leading
        form.spacing = 14
        form.translatesAutoresizingMaskIntoConstraints = false

        let cancel = NSButton(title: L10n.t("取消"), target: self, action: #selector(cancelAction))
        let ok = NSButton(title: L10n.t("确定"), target: self, action: #selector(okAction))
        ok.keyEquivalent = "\r"
        self.okButton = ok
        let buttons = NSStackView(views: [NSView(), cancel, ok])
        buttons.orientation = .horizontal
        buttons.spacing = 10
        buttons.translatesAutoresizingMaskIntoConstraints = false
        buttons.widthAnchor.constraint(equalToConstant: groupWidth).isActive = true

        let stack = NSStackView(views: [form, buttons])
        stack.orientation = .vertical
        stack.alignment = .centerX
        stack.spacing = 16
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 16, right: 20)
        stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView = stack
        window.setContentSize(stack.fittingSize)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor),
            stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor),
            stack.bottomAnchor.constraint(equalTo: window.contentView!.bottomAnchor),
        ])
        refreshOKButton()

        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.window === self.window, event.keyCode == 53 else { return event }
            if let field = self.editingTextField {
                if let original = self.editingOriginals[field] {
                    field.stringValue = original
                }
                self.editingOriginals.removeValue(forKey: field)
                self.window?.makeFirstResponder(nil)
                return nil
            }
            self.cancelAction()
            return nil
        }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    deinit {
        if let keyMonitor {
            NSEvent.removeMonitor(keyMonitor)
        }
    }

    func runModal() -> Bool {
        guard let window else { return false }
        NSApp.runModal(for: window)
        window.orderOut(nil)
        return accepted
    }

    private var editingTextField: NSTextField? {
        guard let responder = window?.firstResponder else { return nil }
        if let field = responder as? NSTextField { return field }
        if let editor = responder as? NSTextView { return editor.delegate as? NSTextField }
        return nil
    }

    func controlTextDidChange(_ notification: Notification) {
        refreshOKButton()
    }

    func controlTextDidBeginEditing(_ notification: Notification) {
        guard let field = notification.object as? NSTextField else { return }
        editingOriginals[field] = field.stringValue
    }

    func controlTextDidEndEditing(_ notification: Notification) {
        guard let field = notification.object as? NSTextField else { return }
        let original = editingOriginals.removeValue(forKey: field)
        if let message = invalidMessage(for: field) {
            if let original { field.stringValue = original }
            let alert = NSAlert()
            alert.messageText = message
            alert.alertStyle = .warning
            alert.addButton(withTitle: L10n.t("好"))
            if let window { alert.beginSheetModal(for: window) }
            return
        }
        refreshOKButton()
    }

    private func invalidMessage(for field: NSTextField) -> String? {
        func doubleMessage(_ range: ClosedRange<Double>, _ label: String) -> String? {
            guard let value = BoundedTextFieldMonitor.value(field.stringValue),
                  range.contains(value) else {
                return L10n.f("“%@”需要填写有效的数值（%@）", L10n.t(label), "\(range.lowerBound)–\(range.upperBound)")
            }
            return nil
        }
        func intMessage(_ range: ClosedRange<Int>, _ label: String) -> String? {
            guard let value = Int(field.stringValue.trimmingCharacters(in: .whitespaces)),
                  range.contains(value) else {
                return L10n.f("“%@”需要填写有效的数值（%@）", L10n.t(label), "\(range.lowerBound)–\(range.upperBound)")
            }
            return nil
        }
        if field === lineHeightField { return doubleMessage(AppSettings.visualLineHeightRange, "基础行高") }
        if field === fontSizeField { return intMessage(AppSettings.visualFontSizeRange, "基础字号") }
        if field === maxWidthField { return intMessage(AppSettings.visualMaxContentWidthRange, "最大内容宽度") }
        return nil
    }

    private func refreshOKButton() {
        okButton?.isEnabled = [lineHeightField, fontSizeField, maxWidthField]
            .allSatisfy { invalidMessage(for: $0) == nil }
    }

    @objc private func okAction() {
        guard let lineHeight = BoundedTextFieldMonitor.value(lineHeightField.stringValue),
              AppSettings.visualLineHeightRange.contains(lineHeight),
              let fontSize = Int(fontSizeField.stringValue.trimmingCharacters(in: .whitespaces)),
              AppSettings.visualFontSizeRange.contains(fontSize),
              let maxContentWidth = Int(maxWidthField.stringValue.trimmingCharacters(in: .whitespaces)),
              AppSettings.visualMaxContentWidthRange.contains(maxContentWidth) else {
            presentInvalidValuesAlert()
            return
        }
        self.lineHeight = lineHeight
        self.fontSize = fontSize
        self.maxContentWidth = maxContentWidth
        self.visualCjkAutoSpacing = autoSpacingCheck.state == .on
        self.visualAdaptiveJustification = adaptiveJustificationCheck.state == .on
        accepted = true
        NSApp.stopModal()
    }

    @objc private func cancelAction() {
        accepted = false
        NSApp.stopModal()
    }

    private func presentInvalidValuesAlert() {
        let alert = NSAlert()
        alert.messageText = L10n.t("请填写有效的可视化设置")
        alert.alertStyle = .warning
        alert.addButton(withTitle: L10n.t("好"))
        if let window { alert.beginSheetModal(for: window) }
    }
}
