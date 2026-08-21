import AppKit

/// 独立的「自定义状态栏」窗口（对应 Windows StatusBarSettingsDialog）。
/// 以模态窗口运行，确定后把选择写回 SettingsService；取消不保存。
final class StatusBarSettingsWindowController: NSWindowController {
    private let sidebarToggleCheck = NSButton(checkboxWithTitle: L10n.t("显示侧栏切换按钮"), target: nil, action: nil)
    private let commandStatusCheck = NSButton(checkboxWithTitle: L10n.t("显示命令状态"), target: nil, action: nil)
    private let commandModePopup = NSPopUpButton()
    private let wordCountCheck = NSButton(checkboxWithTitle: L10n.t("显示字符数"), target: nil, action: nil)
    private let blockTypeCheck = NSButton(checkboxWithTitle: L10n.t("显示块类型"), target: nil, action: nil)
    private let positionCheck = NSButton(checkboxWithTitle: L10n.t("显示光标位置"), target: nil, action: nil)
    private let encodingCheck = NSButton(checkboxWithTitle: L10n.t("显示编码"), target: nil, action: nil)
    private let newLineCheck = NSButton(checkboxWithTitle: L10n.t("显示换行符"), target: nil, action: nil)
    private let modeToggleCheck = NSButton(checkboxWithTitle: L10n.t("显示源码/可视化切换"), target: nil, action: nil)
    private let zoomCheck = NSButton(checkboxWithTitle: L10n.t("显示缩放比例"), target: nil, action: nil)
    private(set) var accepted = false

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 460, height: 420),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("自定义状态栏")
        window.center()
        super.init(window: window)
        buildContent()
        loadSettings()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func buildContent() {
        guard let window else { return }
        commandModePopup.addItems(withTitles: [
            L10n.t("始终显示"), L10n.t("临时显示"), L10n.t("隐藏"),
        ])

        let optionsTitle = NSTextField(labelWithString: L10n.t("显示项目"))
        optionsTitle.font = .boldSystemFont(ofSize: 13)
        optionsTitle.textColor = .labelColor

        let checks = NSGridView(views: [
            [sidebarToggleCheck, wordCountCheck],
            [commandStatusCheck, blockTypeCheck],
            [positionCheck, encodingCheck],
            [newLineCheck, modeToggleCheck],
            [zoomCheck, NSView()],
        ])
        checks.rowSpacing = 10
        checks.columnSpacing = 28
        checks.column(at: 0).xPlacement = .leading
        checks.column(at: 1).xPlacement = .leading

        let optionsSection = NSStackView(views: [optionsTitle, checks])
        optionsSection.orientation = .vertical
        optionsSection.alignment = .leading
        optionsSection.spacing = 10

        let commandTitle = NSTextField(labelWithString: L10n.t("命令状态显示方式"))
        commandTitle.font = .boldSystemFont(ofSize: 13)
        commandTitle.textColor = .labelColor
        let modeRow = NSStackView(views: [
            commandTitle,
            commandModePopup,
        ])
        modeRow.orientation = .horizontal
        modeRow.spacing = 12
        modeRow.alignment = .centerY
        commandModePopup.setContentHuggingPriority(.required, for: .horizontal)
        commandModePopup.setContentCompressionResistancePriority(.required, for: .horizontal)

        let contentColumn = NSStackView(views: [optionsSection, modeRow])
        contentColumn.orientation = .vertical
        contentColumn.alignment = .leading
        contentColumn.spacing = 18

        let cancel = NSButton(title: L10n.t("取消"), target: self, action: #selector(cancelAction))
        let ok = NSButton(title: L10n.t("确定"), target: self, action: #selector(okAction))
        ok.keyEquivalent = "\r"
        let buttons = NSStackView(views: [NSView(), cancel, ok])
        buttons.orientation = .horizontal

        let root = NSView()
        root.translatesAutoresizingMaskIntoConstraints = false
        contentColumn.translatesAutoresizingMaskIntoConstraints = false
        buttons.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(contentColumn)
        root.addSubview(buttons)
        window.contentView = root

        let horizontalInset: CGFloat = 24
        let topInset: CGFloat = 22
        let bottomInset: CGFloat = 18
        let sectionSpacing: CGFloat = 18
        let contentWidth = max(contentColumn.fittingSize.width, buttons.fittingSize.width)
        let contentHeight = topInset + contentColumn.fittingSize.height
            + sectionSpacing + buttons.fittingSize.height + bottomInset
        window.setContentSize(NSSize(
            width: contentWidth + horizontalInset * 2,
            height: contentHeight
        ))
        NSLayoutConstraint.activate([
            contentColumn.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: horizontalInset),
            contentColumn.topAnchor.constraint(equalTo: root.topAnchor, constant: topInset),
            contentColumn.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -horizontalInset),
            buttons.leadingAnchor.constraint(greaterThanOrEqualTo: root.leadingAnchor, constant: horizontalInset),
            buttons.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -horizontalInset),
            buttons.topAnchor.constraint(equalTo: contentColumn.bottomAnchor, constant: sectionSpacing),
            buttons.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -bottomInset),
        ])
    }

    private func loadSettings() {
        let statusBar = SettingsService.shared.settings.statusBar
        sidebarToggleCheck.state = statusBar.sidebarToggleVisible ? .on : .off
        commandStatusCheck.state = statusBar.commandStatusVisible ? .on : .off
        commandModePopup.selectItem(at: statusBar.commandDisplayMode == .always ? 0
                                    : statusBar.commandDisplayMode == .temporary ? 1 : 2)
        wordCountCheck.state = statusBar.wordCountVisible ? .on : .off
        blockTypeCheck.state = statusBar.blockTypeVisible ? .on : .off
        positionCheck.state = statusBar.positionVisible ? .on : .off
        encodingCheck.state = statusBar.encodingVisible ? .on : .off
        newLineCheck.state = statusBar.newLineVisible ? .on : .off
        modeToggleCheck.state = statusBar.modeToggleVisible ? .on : .off
        zoomCheck.state = statusBar.zoomVisible ? .on : .off
    }

    func runModal() -> Bool {
        guard let window else { return false }
        NSApp.runModal(for: window)
        window.orderOut(nil)
        return accepted
    }

    @objc private func okAction() {
        SettingsService.shared.update { settings in
            settings.statusBar.sidebarToggleVisible = sidebarToggleCheck.state == .on
            settings.statusBar.commandStatusVisible = commandStatusCheck.state == .on
            settings.statusBar.commandDisplayMode = switch commandModePopup.indexOfSelectedItem {
            case 1: .temporary
            case 2: .hidden
            default: .always
            }
            settings.statusBar.wordCountVisible = wordCountCheck.state == .on
            settings.statusBar.blockTypeVisible = blockTypeCheck.state == .on
            settings.statusBar.positionVisible = positionCheck.state == .on
            settings.statusBar.encodingVisible = encodingCheck.state == .on
            settings.statusBar.newLineVisible = newLineCheck.state == .on
            settings.statusBar.modeToggleVisible = modeToggleCheck.state == .on
            settings.statusBar.zoomVisible = zoomCheck.state == .on
        }
        accepted = true
        NSApp.stopModal()
    }

    @objc private func cancelAction() {
        accepted = false
        NSApp.stopModal()
    }
}
