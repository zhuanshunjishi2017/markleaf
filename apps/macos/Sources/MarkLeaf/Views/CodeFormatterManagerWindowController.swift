import AppKit

final class CodeFormatterManagerWindowController: NSWindowController, NSWindowDelegate,
    NSTableViewDataSource, NSTableViewDelegate {
    var onClose: (() -> Void)?
    var onDidChange: (() -> Void)?

    private let tools = ExternalCodeFormatterCatalog.tools
    private var paths: [String: String]
    private var lastKnownLanguages: [String]
    private var probeResults: [String: ExternalCodeFormatterProbeResult] = [:]
    private var activeObserver: NSObjectProtocol?
    private let tableView = NSTableView()
    private let statusLabel = NSTextField(labelWithString: "")
    private let chooseButton = NSButton(title: L10n.t("选择可执行文件…"), target: nil, action: nil)
    private let clearButton = NSButton(title: L10n.t("清除自定义路径"), target: nil, action: nil)
    private let probeButton = NSButton(title: L10n.t("探测"), target: nil, action: nil)
    private let sqlDialectPopup = NSPopUpButton()
    private let sqlDialectControls = NSStackView()

    init() {
        paths = SettingsService.shared.settings.codeFormatterPaths
        lastKnownLanguages = ExternalCodeFormatterCatalog.availableLanguages(configuredPaths: paths)
        let window = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 720, height: 400),
            styleMask: [.titled, .closable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = L10n.t("代码格式化器")
        window.isReleasedWhenClosed = false
        FloatingWindowChrome.configure(window, classification: .content)
        window.contentMinSize = NSSize(width: 620, height: 320)
        window.setContentSize(NSSize(width: 720, height: 400))
        super.init(window: window)
        window.delegate = self
        activeObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didBecomeActiveNotification,
            object: NSApp,
            queue: .main
        ) { [weak self] _ in
            self?.refresh()
        }
        buildContent()
        window.center()
        refresh()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    deinit {
        if let activeObserver {
            NotificationCenter.default.removeObserver(activeObserver)
        }
    }

    private func buildContent() {
        guard let window else { return }
        let description = NSTextField(wrappingLabelWithString: L10n.t(
            "可选择本机已安装的格式化器；未指定路径时，MarkLeaf 会在 PATH 中自动查找。"
        ))
        description.textColor = .secondaryLabelColor
        description.translatesAutoresizingMaskIntoConstraints = false

        let languageColumn = NSTableColumn(identifier: .init("languages"))
        languageColumn.title = L10n.t("语言")
        languageColumn.width = 210
        let toolColumn = NSTableColumn(identifier: .init("tool"))
        toolColumn.title = L10n.t("格式化器")
        toolColumn.width = 180
        let pathColumn = NSTableColumn(identifier: .init("path"))
        pathColumn.title = L10n.t("状态")
        pathColumn.width = 270
        tableView.addTableColumn(languageColumn)
        tableView.addTableColumn(toolColumn)
        tableView.addTableColumn(pathColumn)
        tableView.dataSource = self
        tableView.delegate = self
        tableView.rowSizeStyle = .medium
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.headerView = NSTableHeaderView()

        let scroll = NSScrollView()
        scroll.documentView = tableView
        CompactOverlayScrollView.configure(scroll)
        scroll.borderType = .bezelBorder
        scroll.translatesAutoresizingMaskIntoConstraints = false

        chooseButton.target = self
        chooseButton.action = #selector(chooseExecutable)
        clearButton.target = self
        clearButton.action = #selector(clearCustomPath)
        probeButton.target = self
        probeButton.action = #selector(runProbe)
        sqlDialectPopup.addItems(withTitles: SQLFormatterDialect.allCases.map(\.displayName))
        sqlDialectPopup.setAccessibilityLabel(L10n.t("SQL 方言"))
        sqlDialectPopup.target = self
        sqlDialectPopup.action = #selector(sqlDialectChanged)
        let sqlLabel = NSTextField(labelWithString: L10n.t("SQL 方言"))
        sqlLabel.setContentCompressionResistancePriority(.required, for: .horizontal)
        sqlDialectPopup.setContentCompressionResistancePriority(.required, for: .horizontal)
        sqlDialectControls.addArrangedSubview(sqlLabel)
        sqlDialectControls.addArrangedSubview(sqlDialectPopup)
        sqlDialectControls.orientation = .horizontal
        sqlDialectControls.alignment = .centerY
        sqlDialectControls.spacing = 6
        statusLabel.textColor = .secondaryLabelColor
        statusLabel.lineBreakMode = .byTruncatingTail
        statusLabel.usesSingleLineMode = true
        statusLabel.cell?.truncatesLastVisibleLine = true
        statusLabel.setContentHuggingPriority(.defaultLow, for: .horizontal)
        statusLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let closeButton = NSButton(title: L10n.t("关闭"), target: self, action: #selector(closeWindow))
        closeButton.keyEquivalent = "\r"
        closeButton.setContentHuggingPriority(.required, for: .horizontal)
        closeButton.setContentCompressionResistancePriority(.required, for: .horizontal)
        closeButton.translatesAutoresizingMaskIntoConstraints = false

        let spacer = NSView()
        spacer.setContentHuggingPriority(.defaultLow, for: .horizontal)
        spacer.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        let buttons = NSStackView(views: [chooseButton, clearButton, probeButton, sqlDialectControls, statusLabel, spacer, closeButton])
        buttons.orientation = .horizontal
        buttons.alignment = .centerY
        buttons.spacing = 10
        buttons.detachesHiddenViews = true
        buttons.translatesAutoresizingMaskIntoConstraints = false
        closeButton.widthAnchor.constraint(equalToConstant: 82).isActive = true
        let fixedControls = [chooseButton, clearButton, probeButton, sqlDialectControls] as [NSView]
        for control in fixedControls {
            control.setContentCompressionResistancePriority(.required, for: .horizontal)
        }
        // Measure the widest (SQL-visible) action row once, so translated labels
        // cannot squeeze controls or resize the window when selection changes.
        let minimumWidth = max(620, ceil(
            fixedControls.reduce(CGFloat(82), { $0 + $1.fittingSize.width })
                + buttons.spacing * CGFloat(buttons.views.count - 1) + 36
        ))

        let root = NSView()
        root.addSubview(description)
        root.addSubview(scroll)
        root.addSubview(buttons)
        window.contentView = root
        window.contentMinSize = NSSize(width: minimumWidth, height: 320)
        window.setContentSize(NSSize(width: max(window.contentLayoutRect.width, minimumWidth), height: 400))
        NSLayoutConstraint.activate([
            description.topAnchor.constraint(equalTo: root.topAnchor, constant: 18),
            description.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            description.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            scroll.topAnchor.constraint(equalTo: description.bottomAnchor, constant: 14),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            scroll.bottomAnchor.constraint(equalTo: buttons.topAnchor, constant: -14),
            buttons.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            buttons.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16),
            buttons.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            root.widthAnchor.constraint(greaterThanOrEqualToConstant: minimumWidth),
            root.heightAnchor.constraint(greaterThanOrEqualToConstant: 320),
        ])
    }

    func refresh() {
        sqlDialectPopup.selectItem(at: SQLFormatterDialect.allCases.firstIndex(
            of: SettingsService.shared.settings.sqlFormatterDialect
        ) ?? 0)
        paths = SettingsService.shared.settings.codeFormatterPaths
        let currentLanguages = ExternalCodeFormatterCatalog.availableLanguages(configuredPaths: paths)
        let availabilityDidChange = currentLanguages != lastKnownLanguages
        lastKnownLanguages = currentLanguages
        if availabilityDidChange {
            self.probeResults.removeAll()
        }
        let selectedRow = tableView.selectedRow
        tableView.reloadData()
        let rowToRestore = tools.indices.contains(selectedRow) ? selectedRow : 0
        tableView.selectRowIndexes(IndexSet(integer: rowToRestore), byExtendingSelection: false)
        updateControls()
        if availabilityDidChange {
            onDidChange?()
        }
    }

    private var selectedTool: ExternalCodeFormatterTool? {
        let row = tableView.selectedRow
        return row >= 0 && row < tools.count ? tools[row] : nil
    }

    @objc private func sqlDialectChanged() {
        let index = sqlDialectPopup.indexOfSelectedItem
        guard SQLFormatterDialect.allCases.indices.contains(index) else { return }
        SettingsService.shared.update { $0.sqlFormatterDialect = SQLFormatterDialect.allCases[index] }
        onDidChange?()
    }

    private func resolvedPath(for tool: ExternalCodeFormatterTool) -> String? {
        let custom = paths[tool.id]?.trimmedNonEmpty
        return custom ?? ExternalCodeFormatterCatalog.executablePath(tool.executableName)
    }

    private func status(for tool: ExternalCodeFormatterTool) -> ExternalCodeFormatterAvailability {
        ExternalCodeFormatterCatalog.availability(
            for: tool,
            configuredPaths: paths
        )
    }

    private func updateControls() {
        sqlDialectControls.isHidden = selectedTool?.languages.contains("sql") != true
        guard let tool = selectedTool else {
            chooseButton.isEnabled = false
            clearButton.isEnabled = false
            statusLabel.stringValue = L10n.t("请选择一个格式化器")
            return
        }
        chooseButton.isEnabled = true
        clearButton.isEnabled = paths[tool.id]?.isEmpty == false
        switch status(for: tool) {
        case .available(let launch):
            statusLabel.stringValue = launch.executablePath
        case .jarMissingJavaRuntime:
            statusLabel.stringValue = L10n.t("已配置 JAR，但缺少 Java 运行时")
        case .latexMissingPerlRuntime:
            statusLabel.stringValue = L10n.t("已安装，但缺少 Perl 模块 File::HomeDir")
        case .invalidCustomPath:
            statusLabel.stringValue = L10n.t("自定义路径无效")
        case .notInstalled:
            statusLabel.stringValue = L10n.t("未安装")
        }
    }

    @objc private func chooseExecutable() {
        guard let tool = selectedTool, let window else { return }
        let panel = NSOpenPanel()
        panel.title = L10n.t("选择格式化器")
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url else { return }
            SettingsService.shared.update { settings in
                settings.codeFormatterPaths[tool.id] = url.path
            }
            self.probeResults.removeAll()
            self.refresh()
            self.onDidChange?()
        }
    }

    @objc private func clearCustomPath() {
        guard let tool = selectedTool else { return }
        SettingsService.shared.update { settings in
            settings.codeFormatterPaths.removeValue(forKey: tool.id)
        }
        probeResults.removeAll()
        refresh()
        onDidChange?()
    }

    @objc private func closeWindow() {
        window?.performClose(nil)
    }

    @objc private func runProbe() {
        guard let tool = selectedTool else { return }
        probeResults.removeValue(forKey: tool.id)
        tableView.reloadData()
        updateControls()
        probeButton.isEnabled = false
        probeButton.title = L10n.t("探测中…")
        ExternalCodeFormatterProbeService.shared.probe(tool: tool) { [weak self] result in
            guard let self else { return }
            self.probeButton.isEnabled = true
            self.probeButton.title = L10n.t("探测")
            self.probeResults[result.id] = result
            self.tableView.reloadData()
            self.updateControls()
        }
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        tools.count
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        updateControls()
    }

    func windowWillClose(_ notification: Notification) {
        onClose?()
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row >= 0, row < tools.count, let tableColumn else { return nil }
        let tool = tools[row]
        if tableColumn.identifier.rawValue == "path" {
            let identifier = NSUserInterfaceItemIdentifier("formatter-status")
            let cell = (tableView.makeView(withIdentifier: identifier, owner: self) as? NSTableCellView) ?? {
                let cell = NSTableCellView()
                cell.identifier = identifier

                let text = NSTextField(labelWithString: "")
                text.translatesAutoresizingMaskIntoConstraints = false
                text.lineBreakMode = .byTruncatingMiddle
                text.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

                let websiteButton = NSButton(
                    title: L10n.t("官网"),
                    target: self,
                    action: #selector(openWebsite(_:))
                )
                websiteButton.controlSize = .small
                websiteButton.setAccessibilityIdentifier("formatter-website-link")
                websiteButton.setContentHuggingPriority(.required, for: .horizontal)
                websiteButton.setContentCompressionResistancePriority(.required, for: .horizontal)

                let stack = NSStackView(views: [text, websiteButton])
                stack.orientation = .horizontal
                stack.alignment = .centerY
                stack.spacing = 8
                stack.translatesAutoresizingMaskIntoConstraints = false
                cell.addSubview(stack)
                cell.textField = text
                NSLayoutConstraint.activate([
                    stack.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
                    stack.trailingAnchor.constraint(lessThanOrEqualTo: cell.trailingAnchor, constant: -4),
                    stack.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
                ])
                return cell
            }()

            let statusText = cell.textField!
            let websiteButton = cell.subviews.compactMap({ $0 as? NSStackView })
                .flatMap({ $0.arrangedSubviews })
                .compactMap({ $0 as? NSButton })
                .first!
            websiteButton.tag = row
            if let probe = probeResults[tool.id] {
                statusText.stringValue = probe.isSuccess
                    ? "✅ \(probe.detail)"
                    : "❌ \(probe.detail)"
                statusText.textColor = probe.isSuccess ? .labelColor : .systemRed
                statusText.toolTip = probe.detail
                websiteButton.isHidden = probe.isSuccess
                return cell
            }
            statusText.textColor = .labelColor
            statusText.toolTip = nil
            switch status(for: tool) {
            case .available(let launch):
            statusText.stringValue = launch.executablePath
                websiteButton.isHidden = true
            case .jarMissingJavaRuntime:
                statusText.stringValue = L10n.t("已配置 JAR，但缺少 Java 运行时")
                websiteButton.title = L10n.t("获取 Java…")
                websiteButton.isHidden = false
            case .latexMissingPerlRuntime:
                statusText.stringValue = L10n.t("已安装，但缺少 Perl 模块 File::HomeDir")
                websiteButton.title = L10n.t("安装依赖")
                websiteButton.isHidden = false
            case .invalidCustomPath:
                statusText.stringValue = L10n.t("自定义路径无效")
                websiteButton.title = L10n.t("官网")
                websiteButton.isHidden = false
            case .notInstalled:
                statusText.stringValue = L10n.t("未安装")
                websiteButton.title = L10n.t("官网")
                websiteButton.isHidden = false
            }
            return cell
        }

        let identifier = NSUserInterfaceItemIdentifier("formatter-\(tableColumn.identifier.rawValue)")
        let cell = (tableView.makeView(withIdentifier: identifier, owner: self) as? NSTableCellView) ?? {
            let cell = NSTableCellView()
            cell.identifier = identifier
            let text = NSTextField(labelWithString: "")
            text.translatesAutoresizingMaskIntoConstraints = false
            text.lineBreakMode = .byTruncatingMiddle
            cell.addSubview(text)
            cell.textField = text
            NSLayoutConstraint.activate([
                text.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
                text.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -4),
                text.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()
        cell.textField?.stringValue = switch tableColumn.identifier.rawValue {
        case "languages": tool.languages.joined(separator: ", ")
        case "tool": tool.displayName
        default:
            switch status(for: tool) {
            case .available(let launch): launch.executablePath
            case .jarMissingJavaRuntime: L10n.t("已配置 JAR，但缺少 Java 运行时")
            case .latexMissingPerlRuntime: L10n.t("已安装，但缺少 Perl 模块 File::HomeDir")
            case .invalidCustomPath: L10n.t("自定义路径无效")
            case .notInstalled: L10n.t("未安装")
            }
        }
        return cell
    }

    @objc private func openWebsite(_ sender: NSButton) {
        guard tools.indices.contains(sender.tag) else { return }
        let tool = tools[sender.tag]
        switch status(for: tool) {
        case .jarMissingJavaRuntime:
            NSWorkspace.shared.open(ExternalCodeFormatterCatalog.javaRuntimeDownloadURL)
        case .latexMissingPerlRuntime:
            NSWorkspace.shared.open(ExternalCodeFormatterCatalog.perlModuleInstallURL)
        default:
            NSWorkspace.shared.open(tool.homepageURL)
        }
    }
}

private extension String {
    var trimmedNonEmpty: String? {
        let value = trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }
}
