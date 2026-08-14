import AppKit

/// 快捷键参考窗口：表格列出 快捷键 | 功能。
final class ShortcutWindowController: NSWindowController, NSTableViewDataSource, NSTableViewDelegate {
    private let shortcuts: [(String, String)] = [
        ("⌘N", L10n.t("新建文档")),
        ("⇧⌘N", L10n.t("新建窗口")),
        ("⌘O", L10n.t("打开…")),
        ("⌘S", L10n.t("保存")),
        ("⇧⌘S", L10n.t("另存为…")),
        ("⇧⌘E", L10n.t("导出…")),
        ("⌘Z / ⇧⌘Z", L10n.t("撤销 / 重做")),
        ("⌘X / ⌘C / ⌘V", L10n.t("剪切 / 拷贝 / 粘贴")),
        ("⌘F / ⌥⌘F", L10n.t("查找 / 替换")),
        ("⌘B / ⌘I / ⌘U", L10n.t("加粗 / 斜体 / 下划线")),
        ("⇧⌘C", L10n.t("吸附格式刷")),
        ("⇧⌘V", L10n.t("应用格式刷")),
        ("⌘K", L10n.t("插入超链接")),
        ("⌘1 – ⌘6", L10n.t("标题 1 – 6")),
        ("⌥⌘U", L10n.t("源码模式")),
        ("⌘+ / ⌘- / ⌘0", L10n.t("放大 / 缩小 / 100%")),
        ("⌥⌘S", L10n.t("复制为 Markdown 源码")),
        ("⌘,", L10n.t("偏好设置")),
    ]

    private let tableView = NSTableView()

    init() {
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 420),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("快捷键")
        window.isReleasedWhenClosed = false
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
        // 表头与正文对齐一致：快捷键列右对齐，功能列左对齐
        keyColumn.headerCell.alignment = .right
        let descColumn = NSTableColumn(identifier: .init("desc"))
        descColumn.title = L10n.t("功能")
        descColumn.width = 210
        descColumn.headerCell.alignment = .left
        tableView.addTableColumn(keyColumn)
        tableView.addTableColumn(descColumn)
        tableView.dataSource = self
        tableView.delegate = self
        tableView.rowSizeStyle = .medium
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.selectionHighlightStyle = .none

        let scroll = NSScrollView()
        scroll.documentView = tableView
        scroll.hasVerticalScroller = true
        scroll.translatesAutoresizingMaskIntoConstraints = false

        let doneButton = NSButton(title: L10n.t("好"), target: self, action: #selector(closeWindow))
        doneButton.keyEquivalent = "\r"
        doneButton.translatesAutoresizingMaskIntoConstraints = false

        let root = NSView()
        root.addSubview(scroll)
        root.addSubview(doneButton)
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: root.topAnchor, constant: 14),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 14),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            scroll.bottomAnchor.constraint(equalTo: doneButton.topAnchor, constant: -12),
            doneButton.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -14),
            doneButton.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
            doneButton.widthAnchor.constraint(equalToConstant: 80),
        ])
        window.contentView = root
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        shortcuts.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row < shortcuts.count, let column = tableColumn else { return nil }
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
        let (key, desc) = shortcuts[row]
        if column.identifier.rawValue == "key" {
            cell.textField?.stringValue = key
            cell.textField?.font = .monospacedSystemFont(ofSize: 13, weight: .regular)
            cell.textField?.alignment = .right
        } else {
            cell.textField?.stringValue = desc
            cell.textField?.font = .systemFont(ofSize: 13)
            cell.textField?.alignment = .left
        }
        return cell
    }

    @objc private func closeWindow() {
        window?.close()
    }
}
