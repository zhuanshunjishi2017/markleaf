import AppKit

/// 快捷键窗口：列出可自定义命令，支持录制新快捷键、清除、恢复默认、全部恢复默认。
final class ShortcutWindowController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate {
    private let entries = ShortcutCatalog.entries
    private var recordingCommand: String?
    private var recordingMonitor: Any?

    private let tableView = NSTableView()
    private let statusLabel = NSTextField(labelWithString: "")
    private let changeButton = NSButton(title: L10n.t("更改…"), target: nil, action: nil)
    private let clearButton = NSButton(title: L10n.t("清除"), target: nil, action: nil)
    private let restoreButton = NSButton(title: L10n.t("恢复默认"), target: nil, action: nil)
    private let resetAllButton = NSButton(title: L10n.t("全部恢复默认"), target: nil, action: nil)

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 520, height: 480),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("快捷键")
        window.isReleasedWhenClosed = false
        window.contentMinSize = NSSize(width: 520, height: 480)
        window.center()
        super.init(window: window)
        buildContent()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func buildContent() {
        guard let window else { return }

        let keyColumn = NSTableColumn(identifier: .init("key"))
        keyColumn.title = L10n.t("快捷键")
        keyColumn.width = 170
        keyColumn.headerCell.alignment = .right
        let descColumn = NSTableColumn(identifier: .init("desc"))
        descColumn.title = L10n.t("功能")
        descColumn.width = 300
        descColumn.headerCell.alignment = .left
        tableView.addTableColumn(keyColumn)
        tableView.addTableColumn(descColumn)
        tableView.dataSource = self
        tableView.delegate = self
        tableView.rowSizeStyle = .medium
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.allowsMultipleSelection = false

        let scroll = NSScrollView()
        scroll.documentView = tableView
        scroll.hasVerticalScroller = true
        scroll.translatesAutoresizingMaskIntoConstraints = false

        statusLabel.font = .systemFont(ofSize: 12)
        statusLabel.textColor = .secondaryLabelColor
        statusLabel.translatesAutoresizingMaskIntoConstraints = false

        changeButton.target = self
        changeButton.action = #selector(startRecording)
        clearButton.target = self
        clearButton.action = #selector(clearShortcut)
        restoreButton.target = self
        restoreButton.action = #selector(restoreDefault)
        resetAllButton.target = self
        resetAllButton.action = #selector(resetAll)
        let doneButton = NSButton(title: L10n.t("好"), target: self, action: #selector(closeWindow))
        doneButton.keyEquivalent = "\r"
        doneButton.translatesAutoresizingMaskIntoConstraints = false

        let buttonRow = NSStackView(views: [changeButton, clearButton, restoreButton, resetAllButton])
        buttonRow.orientation = .horizontal
        buttonRow.spacing = 8
        buttonRow.translatesAutoresizingMaskIntoConstraints = false

        let root = NSView()
        root.addSubview(scroll)
        root.addSubview(statusLabel)
        root.addSubview(buttonRow)
        root.addSubview(doneButton)
        window.contentView = root
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: root.topAnchor, constant: 14),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            scroll.bottomAnchor.constraint(equalTo: statusLabel.topAnchor, constant: -10),
            statusLabel.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            statusLabel.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            statusLabel.bottomAnchor.constraint(equalTo: buttonRow.topAnchor, constant: -8),
            buttonRow.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            buttonRow.trailingAnchor.constraint(lessThanOrEqualTo: doneButton.leadingAnchor, constant: -12),
            buttonRow.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
            doneButton.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            doneButton.centerYAnchor.constraint(equalTo: buttonRow.centerYAnchor),
            doneButton.widthAnchor.constraint(equalToConstant: 80),
        ])
        // 关键：给内容区固定尺寸约束，否则 Auto Layout 会按子视图最小 fitting size
        // 把窗口塌缩成窄条（与 FindPanelController 同款做法）。
        if let contentView = window.contentView {
            contentView.widthAnchor.constraint(equalToConstant: 520).isActive = true
            contentView.heightAnchor.constraint(equalToConstant: 480).isActive = true
        }
    }

    // MARK: - 表格

    func numberOfRows(in tableView: NSTableView) -> Int {
        entries.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row < entries.count, let column = tableColumn else { return nil }
        let id = NSUserInterfaceItemIdentifier("cell")
        let cell = (tableView.makeView(withIdentifier: id, owner: self) as? NSTableCellView) ?? {
            let cell = NSTableCellView()
            cell.identifier = id
            let text = NSTextField(labelWithString: "")
            text.translatesAutoresizingMaskIntoConstraints = false
            text.lineBreakMode = .byTruncatingTail
            cell.addSubview(text)
            cell.textField = text
            NSLayoutConstraint.activate([
                text.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
                text.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -4),
                text.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()
        let entry = entries[row]
        if column.identifier.rawValue == "key" {
            let (key, mask) = ShortcutSettings.shared.effectiveKey(for: entry)
            cell.textField?.stringValue = ShortcutDisplay.string(key: key, mask: mask)
            cell.textField?.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
            cell.textField?.alignment = .right
        } else {
            cell.textField?.stringValue = L10n.t(entry.titleKey)
            cell.textField?.font = .systemFont(ofSize: 13)
            cell.textField?.alignment = .left
        }
        return cell
    }

    // MARK: - 录制

    @objc private func startRecording() {
        guard let command = selectedCommand() else {
            statusLabel.stringValue = L10n.t("请先选择要更改的命令")
            return
        }
        recordingCommand = command
        statusLabel.stringValue = L10n.t("请按新快捷键…（Esc 取消）")
        recordingMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.recordingCommand != nil else { return event }
            self.handleRecordedKey(event)
            return nil
        }
    }

    private func handleRecordedKey(_ event: NSEvent) {
        guard let command = recordingCommand else { return }
        recordingCommand = nil
        if let monitor = recordingMonitor {
            NSEvent.removeMonitor(monitor)
            recordingMonitor = nil
        }
        if event.keyCode == 53 { // Esc
            statusLabel.stringValue = ""
            return
        }
        let mask = event.modifierFlags.intersection([.command, .option, .control, .shift])
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        switch ShortcutSettings.validate(key: key, mask: mask, for: command) {
        case .none:
            ShortcutSettings.shared.set(
                ShortcutSettings.Binding(key: key, modifiers: mask.rawValue),
                for: command)
            NativeMenuBuilder.refreshIfNeeded()
            tableView.reloadData()
            statusLabel.stringValue = ""
        case .invalid:
            statusLabel.stringValue = L10n.t("不支持的快捷键组合")
        case .systemReserved:
            statusLabel.stringValue = L10n.t("该快捷键为系统保留")
        case .duplicate(let other):
            let title = L10n.t(ShortcutCatalog.entry(for: other)?.titleKey ?? other)
            statusLabel.stringValue = L10n.f("快捷键已被「%@」使用", title)
        }
    }

    // MARK: - 操作

    @objc private func clearShortcut() {
        guard let command = selectedCommand() else { return }
        ShortcutSettings.shared.set(nil, for: command)
        NativeMenuBuilder.refreshIfNeeded()
        tableView.reloadData()
    }

    @objc private func restoreDefault() {
        clearShortcut()
    }

    @objc private func resetAll() {
        ShortcutSettings.shared.resetAll()
        NativeMenuBuilder.refreshIfNeeded()
        tableView.reloadData()
    }

    @objc private func closeWindow() {
        window?.close()
    }

    private func selectedCommand() -> String? {
        guard tableView.selectedRow >= 0, tableView.selectedRow < entries.count else { return nil }
        return entries[tableView.selectedRow].command
    }
}
