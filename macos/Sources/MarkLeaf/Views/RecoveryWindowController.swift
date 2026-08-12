import AppKit

enum RecoveryWindowCopy {
    private static let singular = "检测到 1 个未保存的文档。请选择要恢复的快照："
    private static let plural = "检测到 %d 个未保存的文档。请选择要恢复的快照："

    static func introduction(snapshotCount: Int, language: String) -> String {
        if snapshotCount == 1 {
            return L10n.translate(singular, language: language)
        }
        return L10n.format(plural, language: language, arguments: [snapshotCount])
    }
}

/// 恢复未保存的文件对话框（对应 Windows RecoveryDialog）。
final class RecoveryWindowController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate {
    private var snapshots: [RecoverySnapshot]
    private let language: String
    private let tableView = NSTableView()
    private let timeFormatter = DateFormatter()
    let introductionLabel: NSTextField

    init(
        snapshots: [RecoverySnapshot],
        language: String = SettingsService.shared.settings.displayLanguage
    ) {
        self.snapshots = snapshots
        self.language = language
        introductionLabel = NSTextField(labelWithString: RecoveryWindowCopy.introduction(
            snapshotCount: snapshots.count,
            language: language
        ))
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 560, height: 360),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = L10n.translate("恢复未保存的文档", language: language)
        window.isReleasedWhenClosed = false
        window.center()
        super.init(window: window)

        timeFormatter.dateFormat = "yyyy-MM-dd HH:mm:ss"

        introductionLabel.font = .systemFont(ofSize: 12)
        introductionLabel.translatesAutoresizingMaskIntoConstraints = false

        let column1 = NSTableColumn(identifier: .init("name"))
        column1.title = L10n.translate("名称", language: language)
        column1.width = 200
        let column2 = NSTableColumn(identifier: .init("time"))
        column2.title = L10n.translate("时间", language: language)
        column2.width = 150
        let column3 = NSTableColumn(identifier: .init("path"))
        column3.title = L10n.translate("原路径", language: language)
        column3.width = 160
        tableView.addTableColumn(column1)
        tableView.addTableColumn(column2)
        tableView.addTableColumn(column3)
        tableView.dataSource = self
        tableView.delegate = self
        tableView.rowSizeStyle = .small
        tableView.usesAlternatingRowBackgroundColors = true

        let scroll = NSScrollView()
        scroll.documentView = tableView
        scroll.hasVerticalScroller = true
        scroll.translatesAutoresizingMaskIntoConstraints = false

        let saveButton = NSButton(title: L10n.translate("另存为…", language: language), target: self, action: #selector(saveAs))
        saveButton.keyEquivalent = "\r"
        let discardButton = NSButton(title: L10n.translate("全部丢弃", language: language), target: self, action: #selector(discardAll))
        discardButton.bezelStyle = .rounded
        let cancelButton = NSButton(title: L10n.translate("取消", language: language), target: self, action: #selector(cancel))
        cancelButton.bezelStyle = .rounded

        let buttons = NSStackView(views: [saveButton, discardButton, cancelButton])
        buttons.orientation = .horizontal
        buttons.spacing = 10
        buttons.translatesAutoresizingMaskIntoConstraints = false

        let root = NSView()
        root.addSubview(introductionLabel)
        root.addSubview(scroll)
        root.addSubview(buttons)
        NSLayoutConstraint.activate([
            introductionLabel.topAnchor.constraint(equalTo: root.topAnchor, constant: 14),
            introductionLabel.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            introductionLabel.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -16),
            scroll.topAnchor.constraint(equalTo: introductionLabel.bottomAnchor, constant: 10),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            scroll.bottomAnchor.constraint(equalTo: buttons.topAnchor, constant: -12),
            buttons.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            buttons.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
        ])
        window.contentView = root
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    // MARK: - Table

    func numberOfRows(in tableView: NSTableView) -> Int {
        snapshots.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row < snapshots.count, let column = tableColumn else { return nil }
        let snapshot = snapshots[row]
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
                text.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 2),
                text.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -2),
                text.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()
        switch column.identifier.rawValue {
        case "name":
            cell.textField?.stringValue = snapshot.displayName ?? L10n.translate("未命名文档", language: language)
        case "time":
            cell.textField?.stringValue = timeFormatter.string(from: snapshot.timestamp)
        default:
            cell.textField?.stringValue = snapshot.documentPath ?? L10n.translate("（未保存）", language: language)
        }
        return cell
    }

    // MARK: - Actions

    @objc private func saveAs() {
        let row = tableView.selectedRow
        guard row >= 0, row < snapshots.count else {
            let alert = NSAlert()
            alert.messageText = L10n.t("请先选择一个要恢复的文档")
            alert.alertStyle = .warning
            alert.addButton(withTitle: L10n.t("好"))
            alert.beginSheetModal(for: window!)
            return
        }
        let snapshot = snapshots[row]
        let panel = NSSavePanel()
        panel.title = L10n.t("恢复并另存为")
        let base = snapshot.documentPath?.isEmpty == false
            ? (snapshot.documentPath! as NSString).lastPathComponent
            : (snapshot.displayName ?? L10n.t("恢复的文档")) + ".md"
        panel.nameFieldStringValue = base
        guard let window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            do {
                try snapshot.markdown.write(to: url, atomically: true, encoding: .utf8)
                AppWindowManager.shared.openDocumentInFrontWindow(url)
                RecoveryService.shared.delete(documentId: snapshot.documentId)
                AppLog.info("已恢复并另存: \(url.path)")
            } catch {
                AppLog.error("恢复另存失败: \(error.localizedDescription)")
            }
            self?.close()
        }
    }

    @objc private func discardAll() {
        RecoveryService.discardAll()
        close()
    }

    @objc private func cancel() {
        close()
    }
}
