import AppKit

/// Two native lists share one live session binding; the session owns all changes.
final class ThemeSettingsWindowController: NSWindowController, NSWindowDelegate,
    NSTableViewDataSource, NSTableViewDelegate
{
    var onClose: (() -> Void)?
    private let model: ThemeSettingsModel
    private let onOptionalFonts: () -> Void
    private let themeTable = ThemeSettingsTableView()
    private let styleTable = ThemeSettingsTableView()
    private let followSystemLabel = NSTextField(labelWithString: L10n.t("与操作系统同步"))
    private var isRefreshing = false

    init(sessionProvider: @escaping () -> (any ThemeSettingsSession)?, onOptionalFonts: @escaping () -> Void) {
        model = ThemeSettingsModel(sessionProvider: sessionProvider)
        self.onOptionalFonts = onOptionalFonts
        // A panel keeps the editor as main window while the settings window is key.
        let window = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 720, height: 480),
            styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        window.title = L10n.t("主题设置")
        window.isReleasedWhenClosed = false
        window.contentMinSize = NSSize(width: 620, height: 360)
        window.center()
        super.init(window: window)
        window.delegate = self
        buildContent()
        refresh()
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func showWindow(_ sender: Any?) {
        refresh()
        super.showWindow(sender)
        window?.makeFirstResponder(model.canSelectTheme ? themeTable : styleTable)
    }

    func refresh() {
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }
        model.refresh()
        themeTable.reloadData()
        styleTable.reloadData()
        for (table, index) in [(themeTable, model.selectedThemeIndex), (styleTable, model.selectedStyleIndex)] {
            table.selectRowIndexes(index.map { IndexSet(integer: $0) } ?? [], byExtendingSelection: false)
            if let index { table.scrollRowToVisible(index) }
        }
        themeTable.isEnabled = model.canSelectTheme
        styleTable.isEnabled = model.hasSession
        followSystemLabel.isHidden = !model.followsSystem
    }

    private func buildContent() {
        guard let window else { return }
        let description = NSTextField(labelWithString: L10n.t("选择后立即应用。"))
        description.textColor = .secondaryLabelColor
        followSystemLabel.textColor = .secondaryLabelColor
        let columns = NSStackView(views: [
            list(themeTable, title: L10n.t("颜色主题"), id: "theme-colors"),
            list(styleTable, title: L10n.t("排版样式"), id: "theme-styles"),
        ])
        columns.orientation = .horizontal
        columns.distribution = .fillEqually
        columns.alignment = .height
        columns.spacing = 16
        let fontsButton = NSButton(title: L10n.t("安装可选字体…"), target: self, action: #selector(showOptionalFonts))
        let closeButton = NSButton(title: L10n.t("关闭"), target: self, action: #selector(closeWindow))
        closeButton.keyEquivalent = "\u{1b}"
        let root = NSView()
        for view in [description, followSystemLabel, columns, fontsButton, closeButton] {
            view.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(view)
        }
        window.contentView = root
        NSLayoutConstraint.activate([
            description.topAnchor.constraint(equalTo: root.topAnchor, constant: 18),
            description.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            followSystemLabel.topAnchor.constraint(equalTo: description.bottomAnchor, constant: 6),
            followSystemLabel.leadingAnchor.constraint(equalTo: description.leadingAnchor),
            followSystemLabel.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -18),
            columns.topAnchor.constraint(equalTo: followSystemLabel.bottomAnchor, constant: 14),
            columns.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            columns.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            columns.bottomAnchor.constraint(equalTo: fontsButton.topAnchor, constant: -16),
            fontsButton.leadingAnchor.constraint(equalTo: columns.leadingAnchor),
            fontsButton.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16),
            closeButton.trailingAnchor.constraint(equalTo: columns.trailingAnchor),
            closeButton.centerYAnchor.constraint(equalTo: fontsButton.centerYAnchor),
            closeButton.leadingAnchor.constraint(greaterThanOrEqualTo: fontsButton.trailingAnchor, constant: 12),
        ])
    }

    private func list(_ table: NSTableView, title: String, id: String) -> NSView {
        table.identifier = NSUserInterfaceItemIdentifier(id)
        table.setAccessibilityLabel(title)
        let column = NSTableColumn(identifier: .init(id))
        column.title = title
        column.minWidth = 240
        column.resizingMask = .autoresizingMask
        table.addTableColumn(column)
        table.columnAutoresizingStyle = .lastColumnOnlyAutoresizingStyle
        table.dataSource = self
        table.delegate = self
        table.allowsMultipleSelection = false
        table.allowsEmptySelection = true
        table.usesAlternatingRowBackgroundColors = true
        table.rowSizeStyle = .medium
        table.target = self
        table.doubleAction = #selector(applySelection(_:))
        table.action = #selector(applySelection(_:))
        let scroll = NSScrollView()
        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        return scroll
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        tableView === themeTable ? model.themes.count : model.styles.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let names = tableView === themeTable ? model.themes.map(\.displayName) : model.styles.map(\.displayName)
        guard names.indices.contains(row) else { return nil }
        let label = NSTextField(labelWithString: L10n.t(names[row]))
        label.lineBreakMode = .byTruncatingTail
        label.toolTip = label.stringValue
        label.setAccessibilityLabel(label.stringValue)
        return label
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard let table = notification.object as? NSTableView else { return }
        applySelection(table)
    }

    @objc private func applySelection(_ table: NSTableView) {
        guard !isRefreshing else { return }
        if table === themeTable { model.selectTheme(at: table.selectedRow) }
        else { model.selectStyle(at: table.selectedRow) }
        refresh()
    }

    @objc private func showOptionalFonts() { onOptionalFonts() }
    @objc private func closeWindow() { window?.close() }
    func windowWillClose(_ notification: Notification) { onClose?() }
}

private final class ThemeSettingsTableView: NSTableView {
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 36 || event.keyCode == 76 {
            if let action { sendAction(action, to: target) }
            return
        }
        super.keyDown(with: event)
    }
}
