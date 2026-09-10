import AppKit

final class MarkdownBehaviorSettingsWindowController: NSWindowController {
    private(set) var model: MarkdownBehaviorSettingsModel
    private let escapeLiteralSymbolsCheck = NSButton(
        checkboxWithTitle: L10n.t("转义文本中的 Markdown 符号"), target: nil, action: nil)
    private let escapeMarkdownLiteralSymbolsCheck = NSButton(
        checkboxWithTitle: L10n.t("转义 Markdown 字面量符号"), target: nil, action: nil)
    private let exitBlockOnEmptyEnterCheck = NSButton(
        checkboxWithTitle: L10n.t("空行回车退出块"), target: nil, action: nil)
    private let useShiftEnterHardBreakCheck = NSButton(
        checkboxWithTitle: L10n.t("Shift+Enter 插入硬换行"), target: nil, action: nil)
    private let codeFencePopup = NSPopUpButton()
    private let emphasisMarkerPopup = NSPopUpButton()
    private let bulletMarkerPopup = NSPopUpButton()
    private let checkboxStack = NSStackView()
    private(set) var accepted = false

    init(settings: AppSettings) {
        model = MarkdownBehaviorSettingsModel(settings: settings)
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 500, height: 320),
            styleMask: [.titled, .closable], backing: .buffered, defer: false
        )
        window.title = L10n.t("Markdown 行为")
        window.center()
        super.init(window: window)
        buildContent()
        syncControls()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func runModal() -> Bool {
        guard let window else { return false }
        NSApp.runModal(for: window)
        window.orderOut(nil)
        return accepted
    }

    private func buildContent() {
        codeFencePopup.addItems(withTitles: [L10n.t("反引号 `"), L10n.t("波浪号 ~")])
        emphasisMarkerPopup.addItems(withTitles: [L10n.t("星号 *"), L10n.t("下划线 _")])
        bulletMarkerPopup.addItems(withTitles: [L10n.t("短横线 -"), L10n.t("星号 *"), L10n.t("加号 +")])

        let form = NSGridView(views: [
            [NSTextField(labelWithString: L10n.t("代码围栏")), codeFencePopup],
            [NSTextField(labelWithString: L10n.t("强调标记")), emphasisMarkerPopup],
            [NSTextField(labelWithString: L10n.t("列表标记")), bulletMarkerPopup],
        ])
        form.rowSpacing = 12
        form.columnSpacing = 12
        let popupWidth = [codeFencePopup, emphasisMarkerPopup, bulletMarkerPopup]
            .map { $0.fittingSize.width }
            .max() ?? 0
        for popup in [codeFencePopup, emphasisMarkerPopup, bulletMarkerPopup] {
            popup.widthAnchor.constraint(equalToConstant: popupWidth).isActive = true
        }

        checkboxStack.orientation = .vertical
        checkboxStack.alignment = .leading
        checkboxStack.spacing = 12
        checkboxStack.addArrangedSubview(escapeLiteralSymbolsCheck)
        checkboxStack.addArrangedSubview(escapeMarkdownLiteralSymbolsCheck)
        checkboxStack.addArrangedSubview(exitBlockOnEmptyEnterCheck)
        checkboxStack.addArrangedSubview(useShiftEnterHardBreakCheck)
        checkboxStack.translatesAutoresizingMaskIntoConstraints = false
        form.translatesAutoresizingMaskIntoConstraints = false

        let checkboxArea = NSView()
        checkboxArea.addSubview(checkboxStack)
        let formArea = NSView()
        formArea.addSubview(form)

        let cancel = NSButton(title: L10n.t("取消"), target: self, action: #selector(cancelAction))
        let ok = NSButton(title: L10n.t("确定"), target: self, action: #selector(okAction))
        ok.keyEquivalent = "\r"
        let buttons = NSStackView(views: [NSView(), cancel, ok])
        buttons.orientation = .horizontal
        let stack = NSStackView(views: [checkboxArea, formArea, buttons])
        stack.orientation = .vertical
        stack.alignment = .width
        stack.spacing = 18
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 16, right: 20)
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.layoutSubtreeIfNeeded()
        let fitting = stack.fittingSize
        window?.contentView = stack
        NSLayoutConstraint.activate([
            checkboxStack.topAnchor.constraint(equalTo: checkboxArea.topAnchor),
            checkboxStack.bottomAnchor.constraint(equalTo: checkboxArea.bottomAnchor),
            checkboxStack.centerXAnchor.constraint(equalTo: checkboxArea.centerXAnchor),
            checkboxStack.leadingAnchor.constraint(greaterThanOrEqualTo: checkboxArea.leadingAnchor),
            checkboxStack.trailingAnchor.constraint(lessThanOrEqualTo: checkboxArea.trailingAnchor),
            form.topAnchor.constraint(equalTo: formArea.topAnchor),
            form.bottomAnchor.constraint(equalTo: formArea.bottomAnchor),
            form.centerXAnchor.constraint(equalTo: formArea.centerXAnchor),
            form.leadingAnchor.constraint(greaterThanOrEqualTo: formArea.leadingAnchor),
            form.trailingAnchor.constraint(lessThanOrEqualTo: formArea.trailingAnchor),
        ])
        let contentSize = NSSize(width: max(fitting.width, 360), height: fitting.height)
        window?.setContentSize(contentSize)
        window?.contentMinSize = contentSize
    }

    private func syncControls() {
        escapeLiteralSymbolsCheck.state = model.escapeLiteralSymbols ? .on : .off
        escapeMarkdownLiteralSymbolsCheck.state = model.escapeMarkdownLiteralSymbols ? .on : .off
        exitBlockOnEmptyEnterCheck.state = model.exitBlockOnEmptyEnter ? .on : .off
        useShiftEnterHardBreakCheck.state = model.useShiftEnterHardBreak ? .on : .off
        codeFencePopup.selectItem(at: model.markdownCodeFenceIsTilde ? 1 : 0)
        emphasisMarkerPopup.selectItem(at: model.markdownEmphasisMarkerIsUnderscore ? 1 : 0)
        bulletMarkerPopup.selectItem(at: ["dash", "asterisk", "plus"].firstIndex(of: model.markdownBulletMarker) ?? 0)
    }

    private func syncModel() {
        model.escapeLiteralSymbols = escapeLiteralSymbolsCheck.state == .on
        model.escapeMarkdownLiteralSymbols = escapeMarkdownLiteralSymbolsCheck.state == .on
        model.exitBlockOnEmptyEnter = exitBlockOnEmptyEnterCheck.state == .on
        model.useShiftEnterHardBreak = useShiftEnterHardBreakCheck.state == .on
        model.markdownCodeFenceIsTilde = codeFencePopup.indexOfSelectedItem == 1
        model.markdownEmphasisMarkerIsUnderscore = emphasisMarkerPopup.indexOfSelectedItem == 1
        model.markdownBulletMarker = ["dash", "asterisk", "plus"][
            max(0, min(2, bulletMarkerPopup.indexOfSelectedItem))
        ]
    }

    @objc private func okAction() {
        syncModel()
        accepted = true
        NSApp.stopModal()
    }

    @objc private func cancelAction() {
        accepted = false
        NSApp.stopModal()
    }
}
