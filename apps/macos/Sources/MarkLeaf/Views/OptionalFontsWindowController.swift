import AppKit

/// 可选字体管理器：只安装目录中经过许可、摘要和字体内部名称校验的字体包。
final class OptionalFontsWindowController: NSWindowController, NSWindowDelegate,
    NSTableViewDataSource, NSTableViewDelegate
{
    var onClose: (() -> Void)?

    private let packs = OptionalFontCatalog.packs
    private let installer: OptionalFontInstaller
    private var statuses: [OptionalFontPackStatus] = []
    private var isBusy = false

    private let tableView = NSTableView()
    private let progressIndicator = NSProgressIndicator()
    private let progressLabel = NSTextField(labelWithString: "")
    private let installSelectedButton = NSButton(
        title: L10n.t("安装所选"), target: nil, action: nil)
    private let installAllButton = NSButton(
        title: L10n.t("安装全部可用字体"), target: nil, action: nil)
    private let uninstallButton = NSButton(
        title: L10n.t("卸载所选"), target: nil, action: nil)
    private let licenseButton = NSButton(
        title: L10n.t("查看许可证"), target: nil, action: nil)

    init(installer: OptionalFontInstaller = OptionalFontInstaller()) {
        self.installer = installer
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 760, height: 430),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = L10n.t("安装可选字体")
        window.isReleasedWhenClosed = false
        window.contentMinSize = NSSize(width: 680, height: 380)
        window.center()
        super.init(window: window)
        window.delegate = self
        buildContent()
        reloadStatuses()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func showWindow(_ sender: Any?) {
        reloadStatuses()
        super.showWindow(sender)
    }

    private func buildContent() {
        guard let window else { return }

        let description = NSTextField(wrappingLabelWithString: L10n.t(
            "按需为当前 macOS 用户安装排版样式使用的字体。安装前会校验下载内容和字体内部名称。"
        ))
        description.textColor = .secondaryLabelColor
        description.translatesAutoresizingMaskIntoConstraints = false

        let styleColumn = NSTableColumn(identifier: .init("style"))
        styleColumn.title = L10n.t("所用排版样式")
        styleColumn.width = 190
        let packColumn = NSTableColumn(identifier: .init("pack"))
        packColumn.title = L10n.t("字体包")
        packColumn.width = 225
        let statusColumn = NSTableColumn(identifier: .init("status"))
        statusColumn.title = L10n.t("状态")
        statusColumn.width = 305
        tableView.addTableColumn(styleColumn)
        tableView.addTableColumn(packColumn)
        tableView.addTableColumn(statusColumn)
        tableView.dataSource = self
        tableView.delegate = self
        tableView.rowSizeStyle = .medium
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.allowsMultipleSelection = false
        tableView.headerView = NSTableHeaderView()

        let scroll = NSScrollView()
        scroll.documentView = tableView
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        scroll.translatesAutoresizingMaskIntoConstraints = false

        progressIndicator.style = .spinning
        progressIndicator.controlSize = .small
        progressIndicator.isDisplayedWhenStopped = false
        progressIndicator.translatesAutoresizingMaskIntoConstraints = false
        progressLabel.textColor = .secondaryLabelColor
        progressLabel.lineBreakMode = .byTruncatingTail
        progressLabel.translatesAutoresizingMaskIntoConstraints = false

        installSelectedButton.target = self
        installSelectedButton.action = #selector(installSelected)
        installAllButton.target = self
        installAllButton.action = #selector(installAll)
        uninstallButton.target = self
        uninstallButton.action = #selector(uninstallSelected)
        licenseButton.target = self
        licenseButton.action = #selector(openLicense)

        let closeButton = NSButton(
            title: L10n.t("关闭"), target: self, action: #selector(closeWindow))
        closeButton.keyEquivalent = "\r"

        let leadingButtons = NSStackView(views: [
            installSelectedButton, installAllButton, uninstallButton, licenseButton,
        ])
        leadingButtons.orientation = .horizontal
        leadingButtons.spacing = 8
        leadingButtons.translatesAutoresizingMaskIntoConstraints = false
        closeButton.translatesAutoresizingMaskIntoConstraints = false

        let root = NSView()
        root.addSubview(description)
        root.addSubview(scroll)
        root.addSubview(progressIndicator)
        root.addSubview(progressLabel)
        root.addSubview(leadingButtons)
        root.addSubview(closeButton)
        window.contentView = root

        NSLayoutConstraint.activate([
            description.topAnchor.constraint(equalTo: root.topAnchor, constant: 18),
            description.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            description.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            scroll.topAnchor.constraint(equalTo: description.bottomAnchor, constant: 14),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            scroll.bottomAnchor.constraint(equalTo: progressLabel.topAnchor, constant: -12),
            progressIndicator.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 20),
            progressIndicator.centerYAnchor.constraint(equalTo: progressLabel.centerYAnchor),
            progressLabel.leadingAnchor.constraint(equalTo: progressIndicator.trailingAnchor, constant: 7),
            progressLabel.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            progressLabel.bottomAnchor.constraint(equalTo: leadingButtons.topAnchor, constant: -11),
            progressLabel.heightAnchor.constraint(equalToConstant: 18),
            leadingButtons.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            leadingButtons.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16),
            closeButton.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            closeButton.centerYAnchor.constraint(equalTo: leadingButtons.centerYAnchor),
            closeButton.leadingAnchor.constraint(greaterThanOrEqualTo: leadingButtons.trailingAnchor, constant: 12),
            closeButton.widthAnchor.constraint(equalToConstant: 82),
        ])
    }

    // MARK: - Table

    func numberOfRows(in tableView: NSTableView) -> Int {
        packs.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row >= 0, row < packs.count, let tableColumn else { return nil }
        let identifier = NSUserInterfaceItemIdentifier("font-\(tableColumn.identifier.rawValue)")
        let cell = (tableView.makeView(withIdentifier: identifier, owner: self) as? NSTableCellView) ?? {
            let cell = NSTableCellView()
            cell.identifier = identifier
            let text = NSTextField(labelWithString: "")
            text.translatesAutoresizingMaskIntoConstraints = false
            text.lineBreakMode = .byTruncatingTail
            cell.addSubview(text)
            cell.textField = text
            NSLayoutConstraint.activate([
                text.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 5),
                text.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -5),
                text.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()

        let pack = packs[row]
        switch tableColumn.identifier.rawValue {
        case "style":
            cell.textField?.stringValue = styleDisplayName(for: pack.styleID)
            cell.textField?.textColor = .labelColor
        case "pack":
            cell.textField?.stringValue = pack.displayName
            cell.textField?.textColor = .labelColor
        default:
            let status = statuses.indices.contains(row) ? statuses[row] : .missing
            cell.textField?.stringValue = statusText(status)
            cell.toolTip = statusText(status)
            if case .unavailable = status {
                cell.textField?.textColor = .secondaryLabelColor
            } else {
                cell.textField?.textColor = .labelColor
            }
        }
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        updateButtons()
    }

    // MARK: - Actions

    @objc private func installSelected() {
        guard let index = selectedIndex else { return }
        beginInstall(packs: [packs[index]])
    }

    @objc private func installAll() {
        let pending = packs.enumerated().compactMap { index, pack -> OptionalFontPack? in
            guard statuses.indices.contains(index) else { return nil }
            return OptionalFontPresentation.actions(for: pack, status: statuses[index]).canInstall
                ? pack : nil
        }
        beginInstall(packs: pending)
    }

    private func beginInstall(packs selectedPacks: [OptionalFontPack]) {
        guard !isBusy, !selectedPacks.isEmpty else { return }
        let names = selectedPacks.map { "• \($0.displayName) — \($0.licenseName ?? "")" }.joined(separator: "\n")
        let alert = NSAlert()
        alert.alertStyle = .informational
        alert.messageText = L10n.t("安装字体")
        alert.informativeText = L10n.t(
            "这些字体将为当前 macOS 用户注册，其他应用也可以使用。继续即表示你同意相应字体许可证。"
        ) + "\n\n" + names
        alert.addButton(withTitle: L10n.t("继续安装"))
        alert.addButton(withTitle: L10n.t("取消"))
        present(alert: alert) { [weak self] response in
            guard response == .alertFirstButtonReturn else { return }
            self?.performInstall(packs: selectedPacks)
        }
    }

    private func performInstall(packs selectedPacks: [OptionalFontPack]) {
        setBusy(true)
        installer.install(packs: selectedPacks) { [weak self] progress in
            self?.show(progress: progress)
        } completion: { [weak self] result in
            guard let self else { return }
            self.setBusy(false)
            self.reloadStatuses()
            switch result {
            case .success:
                self.progressLabel.stringValue = L10n.t("字体安装完成")
                self.showResultAlert(
                    title: L10n.t("字体安装完成"),
                    message: L10n.t("请重启 MarkLeaf，以确保编辑器和导出使用新字体。"),
                    style: .informational
                )
            case .failure(let error):
                self.progressLabel.stringValue = L10n.t("字体安装失败")
                self.showResultAlert(
                    title: L10n.t("字体安装失败"),
                    message: errorMessage(error),
                    style: .critical
                )
            }
        }
    }

    @objc private func uninstallSelected() {
        guard !isBusy, let index = selectedIndex else { return }
        let pack = packs[index]
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = L10n.t("卸载所选字体？")
        alert.informativeText = L10n.f("将移除 MarkLeaf 管理的“%@”字体文件。", pack.displayName)
        alert.addButton(withTitle: L10n.t("卸载"))
        alert.addButton(withTitle: L10n.t("取消"))
        present(alert: alert) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self else { return }
            self.setBusy(true)
            self.progressLabel.stringValue = L10n.t("正在卸载字体…")
            self.installer.uninstall(pack: pack) { result in
                self.setBusy(false)
                self.reloadStatuses()
                switch result {
                case .success:
                    self.progressLabel.stringValue = L10n.t("字体卸载完成")
                case .failure(let error):
                    self.progressLabel.stringValue = L10n.t("字体卸载失败")
                    self.showResultAlert(
                        title: L10n.t("字体卸载失败"),
                        message: self.errorMessage(error),
                        style: .critical
                    )
                }
            }
        }
    }

    @objc private func openLicense() {
        guard let index = selectedIndex, let url = packs[index].licenseURL else { return }
        NSWorkspace.shared.open(url)
    }

    @objc private func closeWindow() {
        window?.close()
    }

    // MARK: - State

    private var selectedIndex: Int? {
        let row = tableView.selectedRow
        return row >= 0 && row < packs.count ? row : nil
    }

    private func reloadStatuses() {
        statuses = packs.map(installer.status(for:))
        tableView.reloadData()
        updateButtons()
    }

    private func updateButtons() {
        let selectedActions: OptionalFontActionState? = selectedIndex.flatMap { index in
            guard statuses.indices.contains(index) else { return nil }
            return OptionalFontPresentation.actions(for: packs[index], status: statuses[index])
        }
        installSelectedButton.isEnabled = !isBusy && (selectedActions?.canInstall ?? false)
        uninstallButton.isEnabled = !isBusy && (selectedActions?.canUninstall ?? false)
        licenseButton.isEnabled = !isBusy && (selectedActions?.canOpenLicense ?? false)
        installAllButton.isEnabled = !isBusy && packs.enumerated().contains { index, pack in
            statuses.indices.contains(index)
                && OptionalFontPresentation.actions(for: pack, status: statuses[index]).canInstall
        }
    }

    private func setBusy(_ busy: Bool) {
        isBusy = busy
        if busy {
            progressIndicator.startAnimation(nil)
        } else {
            progressIndicator.stopAnimation(nil)
        }
        updateButtons()
    }

    private func show(progress: OptionalFontInstallProgress) {
        switch progress {
        case .downloading(let pack, let index, let total):
            progressLabel.stringValue = L10n.f("正在下载 %@（%d/%d）…", pack.displayName, index, total)
        case .validating(let pack, let index, let total):
            progressLabel.stringValue = L10n.f("正在校验并安装 %@（%d/%d）…", pack.displayName, index, total)
        case .installed(let pack, let index, let total):
            progressLabel.stringValue = L10n.f("已安装 %@（%d/%d）", pack.displayName, index, total)
        }
    }

    private func styleDisplayName(for id: String) -> String {
        switch id {
        case "latex": return "LaTeX"
        case "notebook": return L10n.t("手记")
        case "retro-print": return L10n.t("印刷品(铅字排印)")
        default: return id
        }
    }

    private func statusText(_ status: OptionalFontPackStatus) -> String {
        switch status {
        case .unavailable(let reason): return L10n.t(reason)
        case .installedByMarkLeaf: return L10n.t("已由 MarkLeaf 安装")
        case .installedExternally: return L10n.t("已由其他来源安装")
        case .partial: return L10n.t("安装不完整，可重新安装")
        case .missing: return L10n.t("未安装")
        }
    }

    private func errorMessage(_ error: Error) -> String {
        switch error {
        case OptionalFontInstallerError.digestMismatch:
            return L10n.t("下载文件校验失败，未安装任何字体。")
        case OptionalFontInstallerError.unexpectedPostScriptNames,
             OptionalFontInstallerError.invalidFontData:
            return L10n.t("字体内部名称与清单不一致，未安装任何字体。")
        case OptionalFontInstallerError.httpStatus(let status):
            return L10n.f("字体下载失败（HTTP %d）。", status)
        case OptionalFontInstallerError.registrationFailed:
            return L10n.t("macOS 无法注册字体，已回滚本次安装。")
        case OptionalFontCatalogError.unavailablePack:
            return L10n.t("该字体包暂不可自动安装。")
        default:
            return L10n.t("下载或安装字体时发生错误，请稍后重试。")
        }
    }

    private func present(alert: NSAlert, completion: @escaping (NSApplication.ModalResponse) -> Void) {
        if let window {
            alert.beginSheetModal(for: window, completionHandler: completion)
        } else {
            completion(alert.runModal())
        }
    }

    private func showResultAlert(title: String, message: String, style: NSAlert.Style) {
        let alert = NSAlert()
        alert.alertStyle = style
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: L10n.t("好"))
        present(alert: alert) { _ in }
    }

    func windowWillClose(_ notification: Notification) {
        installer.cancel()
        onClose?()
    }
}
