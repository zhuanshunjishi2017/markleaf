import AppKit

/// Unified color/typography settings. The session owns persistence and editor
/// updates; this controller only coordinates the native controls.
final class ThemeSettingsWindowController: NSWindowController, NSWindowDelegate,
    NSTableViewDataSource, NSTableViewDelegate
{
    var onClose: (() -> Void)?

    private let model: ThemeSettingsModel
    private let onOptionalFonts: () -> Void
    private let onAddTheme: () -> Void
    private let onOpenThemeFolder: () -> Void
    /// 缺字体判定；默认查询真实字体安装状态，测试可注入固定结果。
    private let missingPacksProvider: ((String) -> [OptionalFontPack])?
    private let fontInstaller = OptionalFontInstaller()

    private let segmentedControl = NSSegmentedControl(
        labels: [L10n.t("颜色主题"), L10n.t("排版样式")],
        trackingMode: .selectOne,
        target: nil,
        action: nil
    )
    private let colorsPage = NSView()
    private let stylesPage = NSView()
    private let themeTable = ThemeSettingsTableView()
    private let styleTable = ThemeSettingsTableView()
    private let followCheck = NSButton(
        checkboxWithTitle: L10n.t("与操作系统同步"),
        target: nil,
        action: nil
    )
    private let lightThemePopup = NSPopUpButton()
    private let darkThemePopup = NSPopUpButton()
    private let lightThemeLabel = NSTextField(labelWithString: L10n.t("默认浅色主题"))
    private let darkThemeLabel = NSTextField(labelWithString: L10n.t("默认深色主题"))
    private let missingFontsLabel = NSTextField(wrappingLabelWithString: "")
    private let addThemeButton = NSButton(title: L10n.t("添加主题…"), target: nil, action: nil)
    private let openThemeFolderButton = NSButton(title: L10n.t("打开主题文件夹…"), target: nil, action: nil)
    private let fontsButton = NSButton(title: L10n.t("安装可选字体…"), target: nil, action: nil)
    private var isRefreshing = false

    init(
        sessionProvider: @escaping () -> (any ThemeSettingsSession)?,
        onOptionalFonts: @escaping () -> Void,
        onAddTheme: @escaping () -> Void = {},
        onOpenThemeFolder: @escaping () -> Void = {},
        missingPacksProvider: ((String) -> [OptionalFontPack])? = nil
    ) {
        model = ThemeSettingsModel(sessionProvider: sessionProvider)
        self.onOptionalFonts = onOptionalFonts
        self.onAddTheme = onAddTheme
        self.onOpenThemeFolder = onOpenThemeFolder
        self.missingPacksProvider = missingPacksProvider
        let window = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 760, height: 520),
            styleMask: [.titled, .closable, .resizable],
            backing: .buffered,
            defer: false
        )
        window.title = L10n.t("主题设置")
        window.isReleasedWhenClosed = false
        window.contentMinSize = NSSize(width: 680, height: 460)
        window.center()
        super.init(window: window)
        window.delegate = self
        buildContent()
        showPage(0)
        refresh()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func showWindow(_ sender: Any?) {
        refresh()
        super.showWindow(sender)
        window?.makeFirstResponder(segmentedControl.selectedSegment == 0 ? themeTable : styleTable)
    }

    func refresh() {
        guard !isRefreshing else { return }
        isRefreshing = true
        defer { isRefreshing = false }
        model.refresh()
        themeTable.reloadData()
        styleTable.reloadData()
        select(table: themeTable, index: model.selectedThemeRow)
        select(table: styleTable, index: model.selectedStyleIndex)
        followCheck.state = model.followsSystem ? .on : .off
        rebuildPopup(lightThemePopup, themes: model.lightThemes, selected: model.selectedLightDefaultIndex)
        rebuildPopup(darkThemePopup, themes: model.darkThemes, selected: model.selectedDarkDefaultIndex)
        lightThemePopup.isEnabled = model.followsSystem
        darkThemePopup.isEnabled = model.followsSystem
        themeTable.isEnabled = model.hasSession && !model.followsSystem
        styleTable.isEnabled = model.hasSession
        refreshMissingFonts()
    }

    private func buildContent() {
        guard let window else { return }
        segmentedControl.target = self
        segmentedControl.action = #selector(segmentChanged)
        segmentedControl.selectedSegment = 0

        let root = NSView()
        let content = NSView()
        for view in [segmentedControl, content] {
            view.translatesAutoresizingMaskIntoConstraints = false
            root.addSubview(view)
        }
        NSLayoutConstraint.activate([
            segmentedControl.topAnchor.constraint(equalTo: root.topAnchor, constant: 14),
            segmentedControl.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            segmentedControl.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            content.topAnchor.constraint(equalTo: segmentedControl.bottomAnchor, constant: 12),
            content.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 18),
            content.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            content.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -54),
        ])

        buildColorsPage()
        buildStylesPage()
        for page in [colorsPage, stylesPage] {
            page.translatesAutoresizingMaskIntoConstraints = false
            content.addSubview(page)
            NSLayoutConstraint.activate([
                page.topAnchor.constraint(equalTo: content.topAnchor),
                page.leadingAnchor.constraint(equalTo: content.leadingAnchor),
                page.trailingAnchor.constraint(equalTo: content.trailingAnchor),
                page.bottomAnchor.constraint(equalTo: content.bottomAnchor),
            ])
        }

        let closeButton = NSButton(title: L10n.t("关闭"), target: self, action: #selector(closeWindow))
        closeButton.keyEquivalent = "\u{1b}"
        closeButton.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(closeButton)
        NSLayoutConstraint.activate([
            closeButton.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -18),
            closeButton.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
        ])
        window.contentView = root
    }

    private func buildColorsPage() {
        let scroll = tableScroll(
            themeTable,
            title: L10n.t("颜色主题"),
            id: "theme-colors",
            rowHeight: 44
        )
        followCheck.target = self
        followCheck.action = #selector(toggleFollowSystem)
        lightThemePopup.target = self
        lightThemePopup.action = #selector(changeLightDefault)
        darkThemePopup.target = self
        darkThemePopup.action = #selector(changeDarkDefault)
        lightThemeLabel.textColor = .secondaryLabelColor
        darkThemeLabel.textColor = .secondaryLabelColor

        let defaults = NSStackView(views: [
            lightThemeLabel, lightThemePopup,
            darkThemeLabel, darkThemePopup,
        ])
        defaults.orientation = .horizontal
        defaults.spacing = 8

        for view in [scroll, followCheck, defaults] {
            view.translatesAutoresizingMaskIntoConstraints = false
            colorsPage.addSubview(view)
        }
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: colorsPage.topAnchor),
            scroll.leadingAnchor.constraint(equalTo: colorsPage.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: colorsPage.trailingAnchor),
            followCheck.topAnchor.constraint(equalTo: scroll.bottomAnchor, constant: 12),
            followCheck.leadingAnchor.constraint(equalTo: colorsPage.leadingAnchor),
            defaults.topAnchor.constraint(equalTo: followCheck.bottomAnchor, constant: 8),
            defaults.leadingAnchor.constraint(equalTo: colorsPage.leadingAnchor),
            defaults.bottomAnchor.constraint(lessThanOrEqualTo: colorsPage.bottomAnchor),
        ])

        let actions = NSStackView(views: [addThemeButton, openThemeFolderButton])
        actions.orientation = .horizontal
        actions.spacing = 8
        actions.translatesAutoresizingMaskIntoConstraints = false
        colorsPage.addSubview(actions)
        NSLayoutConstraint.activate([
            actions.trailingAnchor.constraint(equalTo: colorsPage.trailingAnchor),
            actions.centerYAnchor.constraint(equalTo: defaults.centerYAnchor),
        ])
        addThemeButton.target = self
        addThemeButton.action = #selector(addTheme)
        openThemeFolderButton.target = self
        openThemeFolderButton.action = #selector(openThemeFolder)
    }

    private func buildStylesPage() {
        let scroll = tableScroll(
            styleTable,
            title: L10n.t("排版样式"),
            id: "theme-styles",
            rowHeight: 28
        )
        missingFontsLabel.textColor = .secondaryLabelColor
        fontsButton.target = self
        fontsButton.action = #selector(showOptionalFonts)
        for view in [scroll, missingFontsLabel, fontsButton] {
            view.translatesAutoresizingMaskIntoConstraints = false
            stylesPage.addSubview(view)
        }
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: stylesPage.topAnchor),
            scroll.leadingAnchor.constraint(equalTo: stylesPage.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: stylesPage.trailingAnchor),
            missingFontsLabel.topAnchor.constraint(equalTo: scroll.bottomAnchor, constant: 12),
            missingFontsLabel.leadingAnchor.constraint(equalTo: stylesPage.leadingAnchor),
            missingFontsLabel.trailingAnchor.constraint(equalTo: stylesPage.trailingAnchor),
            fontsButton.topAnchor.constraint(equalTo: missingFontsLabel.bottomAnchor, constant: 8),
            fontsButton.leadingAnchor.constraint(equalTo: stylesPage.leadingAnchor),
            fontsButton.bottomAnchor.constraint(lessThanOrEqualTo: stylesPage.bottomAnchor),
        ])
    }

    private func tableScroll(
        _ table: NSTableView,
        title: String,
        id: String,
        rowHeight: CGFloat
    ) -> NSScrollView {
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
        table.rowHeight = rowHeight
        table.intercellSpacing = NSSize(width: 0, height: 0)
        table.target = self
        table.doubleAction = #selector(applySelection(_:))
        table.action = #selector(applySelection(_:))
        table.floatsGroupRows = false
        let scroll = NSScrollView()
        scroll.documentView = table
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        return scroll
    }

    private func select(table: NSTableView, index: Int?) {
        table.selectRowIndexes(index.map { IndexSet(integer: $0) } ?? [], byExtendingSelection: false)
        if let index { table.scrollRowToVisible(index) }
    }

    private func rebuildPopup(_ popup: NSPopUpButton, themes: [ColorThemeInfo], selected: Int?) {
        popup.removeAllItems()
        popup.addItems(withTitles: themes.map { L10n.t($0.displayName) })
        if let selected, themes.indices.contains(selected) {
            popup.selectItem(at: selected)
        }
    }

    private func refreshMissingFonts() {
        let missing = model.currentStyleID.map(missingPacks(for:)) ?? []
        missingFontsLabel.isHidden = missing.isEmpty
        missingFontsLabel.stringValue = missing.isEmpty
            ? ""
            : L10n.f("当前排版缺少 %d 个字体包。", missing.count)
    }

    private func showPage(_ index: Int) {
        colorsPage.isHidden = index != 0
        stylesPage.isHidden = index != 1
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        tableView === themeTable ? model.colorRows.count : model.styles.count
    }

    func tableView(_ tableView: NSTableView, isGroupRow row: Int) -> Bool {
        guard tableView === themeTable, model.colorRows.indices.contains(row) else { return false }
        if case .group = model.colorRows[row] { return true }
        return false
    }

    func tableView(_ tableView: NSTableView, shouldSelectRow row: Int) -> Bool {
        !self.tableView(tableView, isGroupRow: row)
    }

    /// 行高必须由 delegate 明确给出：仅设置 rowHeight 会被表格样式覆盖，
    /// 导致 34pt 的主题预览在 24pt 行里互相重叠。
    func tableView(_ tableView: NSTableView, heightOfRow row: Int) -> CGFloat {
        if tableView === themeTable {
            return self.tableView(tableView, isGroupRow: row) ? 20 : 46
        }
        return 28
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        if tableView === themeTable {
            guard model.colorRows.indices.contains(row) else { return nil }
            switch model.colorRows[row] {
            case .group(let title):
                return groupCell(title)
            case .theme(let index):
                guard model.themes.indices.contains(index) else { return nil }
                return themeCell(model.themes[index])
            }
        }
        guard model.styles.indices.contains(row) else { return nil }
        return styleCell(model.styles[row])
    }

    private func groupCell(_ title: String) -> NSView {
        let cell = NSTableCellView()
        let label = NSTextField(labelWithString: L10n.t(title))
        label.font = .systemFont(ofSize: 12, weight: .semibold)
        label.textColor = .secondaryLabelColor
        label.setAccessibilityLabel(label.stringValue)
        label.translatesAutoresizingMaskIntoConstraints = false
        cell.addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
            label.bottomAnchor.constraint(equalTo: cell.bottomAnchor, constant: -2),
        ])
        cell.textField = label
        return cell
    }

    /// 排版样式行：名称 + 可选字体缺失徽标（点击打开可选字体窗口）。
    private func styleCell(_ style: StyleDefinition) -> NSView {
        let cell = NSTableCellView()
        let label = NSTextField(labelWithString: L10n.t(style.displayName))
        label.setAccessibilityLabel(label.stringValue)
        label.translatesAutoresizingMaskIntoConstraints = false
        cell.addSubview(label)
        cell.textField = label
        var trailing = cell.trailingAnchor
        var trailingPadding: CGFloat = -4

        let missing = missingPacks(for: style.id)
        if !missing.isEmpty {
            let badge = NSButton(title: L10n.t("缺字体"), target: self, action: #selector(showOptionalFonts))
            badge.bezelStyle = .inline
            badge.controlSize = .small
            badge.font = .systemFont(ofSize: 10, weight: .medium)
            badge.contentTintColor = .systemOrange
            badge.toolTip = L10n.f(
                "当前排版“%@”缺少字体包：%@。",
                L10n.t(style.displayName),
                missing.map { L10n.t($0.displayName) }.joined(separator: "、")
            )
            badge.translatesAutoresizingMaskIntoConstraints = false
            cell.addSubview(badge)
            NSLayoutConstraint.activate([
                badge.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -4),
                badge.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            trailing = badge.leadingAnchor
            trailingPadding = -6
        }
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
            label.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            label.trailingAnchor.constraint(lessThanOrEqualTo: trailing, constant: trailingPadding),
        ])
        return cell
    }

    private func missingPacks(for styleID: String) -> [OptionalFontPack] {
        if let missingPacksProvider { return missingPacksProvider(styleID) }
        return CurrentStyleFontNotice.missingPacks(
            styleID: styleID,
            packs: OptionalFontCatalog.packs,
            statuses: OptionalFontCatalog.packs.map(fontInstaller.status(for:))
        )
    }

    private func themeCell(_ theme: ColorThemeInfo) -> NSView {
        let cell = NSTableCellView()
        let swatch = ThemeSwatchView(theme: theme)
        let label = NSTextField(labelWithString: L10n.t(theme.displayName))
        label.lineBreakMode = .byTruncatingTail
        label.setAccessibilityLabel(label.stringValue)
        for view in [swatch, label] {
            view.translatesAutoresizingMaskIntoConstraints = false
            cell.addSubview(view)
        }
        NSLayoutConstraint.activate([
            swatch.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 4),
            swatch.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            swatch.widthAnchor.constraint(equalToConstant: 64),
            swatch.heightAnchor.constraint(equalToConstant: 36),
            label.leadingAnchor.constraint(equalTo: swatch.trailingAnchor, constant: 8),
            label.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -4),
            label.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
        ])
        cell.textField = label
        return cell
    }

    fileprivate static func cssColor(in css: String, names: [String]) -> NSColor? {
        for name in names {
            let pattern = #"--\#(name)\s*:\s*(#[0-9a-fA-F]{3,8}|rgb[a]?\([^)]+\));"#
            guard let regex = try? NSRegularExpression(pattern: pattern),
                  let match = regex.firstMatch(in: css, range: NSRange(css.startIndex..., in: css)),
                  let range = Range(match.range(at: 1), in: css) else { continue }
            let raw = String(css[range])
            if raw.hasPrefix("#") {
                let hex = String(raw.dropFirst())
                if let value = UInt64(hex, radix: 16) {
                    let r = CGFloat((value >> 16) & 0xff) / 255
                    let g = CGFloat((value >> 8) & 0xff) / 255
                    let b = CGFloat(value & 0xff) / 255
                    return NSColor(red: r, green: g, blue: b, alpha: 1)
                }
            }
            let numbers = raw
                .replacingOccurrences(of: "rgba(", with: "")
                .replacingOccurrences(of: "rgb(", with: "")
                .trimmingCharacters(in: CharacterSet(charactersIn: ")"))
                .split(separator: ",")
                .compactMap { Double($0.trimmingCharacters(in: .whitespaces)) }
            if numbers.count >= 3 {
                return NSColor(
                    red: CGFloat(numbers[0] / 255),
                    green: CGFloat(numbers[1] / 255),
                    blue: CGFloat(numbers[2] / 255),
                    alpha: 1
                )
            }
        }
        return nil
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard let table = notification.object as? NSTableView else { return }
        applySelection(table)
    }

    @objc private func applySelection(_ table: NSTableView) {
        guard !isRefreshing else { return }
        if table === themeTable {
            guard let index = model.themeIndex(atRow: table.selectedRow) else { return }
            model.selectTheme(at: index)
        } else {
            model.selectStyle(at: table.selectedRow)
        }
        refresh()
    }

    @objc private func segmentChanged() {
        showPage(segmentedControl.selectedSegment)
    }

    @objc private func toggleFollowSystem() {
        model.setFollowSystem(followCheck.state == .on)
        refresh()
    }

    @objc private func changeLightDefault() {
        model.selectLightDefault(at: lightThemePopup.indexOfSelectedItem)
        refresh()
    }

    @objc private func changeDarkDefault() {
        model.selectDarkDefault(at: darkThemePopup.indexOfSelectedItem)
        refresh()
    }

    @objc private func showOptionalFonts() { onOptionalFonts() }
    @objc private func addTheme() { onAddTheme() }
    @objc private func openThemeFolder() { onOpenThemeFolder() }
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

/// 颜色主题预览缩略图：模拟一页文档（标题条 + 正文线 + 顶部强调色），
/// 对应 Windows 配色方案对话框里的主题色块。
final class ThemeSwatchView: NSView {
    private let accent = NSView()
    private let titleBar = NSView()
    private let line1 = NSView()
    private let line2 = NSView()

    init(theme: ColorThemeInfo) {
        super.init(frame: .zero)
        wantsLayer = true
        layer?.cornerRadius = 5
        layer?.borderWidth = 1
        layer?.borderColor = NSColor.separatorColor.cgColor
        layer?.masksToBounds = true
        layer?.backgroundColor = Self.color(
            in: theme.css,
            names: ["bg-primary", "bg-secondary"],
            fallback: theme.isDark ? .init(white: 0.13, alpha: 1) : .white
        ).cgColor

        let text = Self.color(in: theme.css, names: ["text-primary"], fallback: theme.isDark ? .white : .black)
        let secondary = Self.color(
            in: theme.css,
            names: ["text-secondary", "text-tertiary"],
            fallback: text.withAlphaComponent(0.55)
        )
        let highlight = Self.color(
            in: theme.css,
            names: ["theme-light", "icon", "highlight"],
            fallback: .controlAccentColor
        )
        for view in [accent, titleBar, line1, line2] {
            view.wantsLayer = true
            addSubview(view)
        }
        accent.layer?.backgroundColor = highlight.cgColor
        titleBar.layer?.backgroundColor = text.cgColor
        line1.layer?.backgroundColor = secondary.cgColor
        line2.layer?.backgroundColor = secondary.withAlphaComponent(0.7).cgColor
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func layout() {
        super.layout()
        let width = bounds.width
        let top = bounds.height - 5
        accent.frame = NSRect(x: 6, y: top - 3, width: 12, height: 3)
        titleBar.frame = NSRect(x: 6, y: top - 12, width: width * 0.46, height: 4)
        line1.frame = NSRect(x: 6, y: top - 20, width: width * 0.74, height: 3)
        line2.frame = NSRect(x: 6, y: top - 27, width: width * 0.58, height: 3)
    }

    private static func color(in css: String, names: [String], fallback: NSColor) -> NSColor {
        ThemeSettingsWindowController.cssColor(in: css, names: names) ?? fallback
    }
}
