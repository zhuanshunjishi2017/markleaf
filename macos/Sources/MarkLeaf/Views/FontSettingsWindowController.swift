import AppKit

final class FontSettingsWindowController: NSWindowController {
    private var cjkField: FontField!
    private var westernField: FontField!
    private let sizeField = NSTextField(string: "14")
    private(set) var cjkFontFamily: String
    private(set) var westernFontFamily: String
    private(set) var fontSize: Int
    private(set) var accepted = false

    init(cjkFontFamily: String, westernFontFamily: String, fontSize: Int) {
        self.cjkFontFamily = cjkFontFamily
        self.westernFontFamily = westernFontFamily
        self.fontSize = fontSize
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
        sizeField.alignment = .center
        sizeField.widthAnchor.constraint(equalToConstant: 80).isActive = true

        let form = NSGridView(views: [
            [NSTextField(labelWithString: L10n.t("中文字体")), cjkField],
            [NSTextField(labelWithString: L10n.t("西文字体")), westernField],
            [NSTextField(labelWithString: L10n.t("基础字号")), sizeField],
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
        window.contentView = stack
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func runModal() -> Bool {
        guard let window else { return false }
        NSApp.runModal(for: window)
        window.orderOut(nil)
        return accepted
    }

    @objc private func okAction() {
        fontSize = min(max(Int(sizeField.stringValue) ?? 14, AppSettings.sourceFontSizeRange.lowerBound), AppSettings.sourceFontSizeRange.upperBound)
        accepted = true
        NSApp.stopModal()
    }

    @objc private func cancelAction() {
        accepted = false
        NSApp.stopModal()
    }
}
