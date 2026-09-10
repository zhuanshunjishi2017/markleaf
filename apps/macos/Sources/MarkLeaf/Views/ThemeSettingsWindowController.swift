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
        onOpenThemeFolder: @escaping () -> Void = {}
    ) {
        model = ThemeSettingsModel(sessionProvider: sessionProvider)
        self.onOptionalFonts = onOptionalFonts
        self.onAddTheme = onAddTheme
        self.onOpenThemeFolder = onOpenThemeFolder
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
        select(table: themeTable, index: model.selectedThemeIndex)
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
        let scroll = tableScroll(themeTable, title: L10n.t("颜色主题"), id: "theme-colors")
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
        let scroll = tableScroll(styleTable, title: L10n.t("排版样式"), id: "theme-styles")
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

    private func tableScroll(_ table: NSTableView, title: String, id: String) -> NSScrollView {
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
        let missing = CurrentStyleFontNotice.missingPacks(
            styleID: model.currentStyleID,
            packs: OptionalFontCatalog.packs,
            statuses: OptionalFontCatalog.packs.map(fontInstaller.status(for:))
        )
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
        tableView === themeTable ? model.themes.count : model.styles.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        if tableView === themeTable {
            guard model.themes.indices.contains(row) else { return nil }
            return themeCell(model.themes[row])
        }
        guard model.styles.indices.contains(row) else { return nil }
        let label = NSTextField(labelWithString: L10n.t(model.styles[row].displayName))
        label.setAccessibilityLabel(label.stringValue)
        return label
    }

    private func themeCell(_ theme: ColorThemeInfo) -> NSView {
        let cell = NSTableCellView()
        let swatch = NSView()
        swatch.wantsLayer = true
        swatch.layer?.cornerRadius = 4
        swatch.layer?.borderWidth = 1
        swatch.layer?.borderColor = NSColor.separatorColor.cgColor
        swatch.layer?.backgroundColor = swatchColor(for: theme).cgColor
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
            swatch.widthAnchor.constraint(equalToConstant: 16),
            swatch.heightAnchor.constraint(equalToConstant: 16),
            label.leadingAnchor.constraint(equalTo: swatch.trailingAnchor, constant: 8),
            label.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -4),
            label.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
        ])
        cell.textField = label
        return cell
    }

    private func swatchColor(for theme: ColorThemeInfo) -> NSColor {
        if let color = Self.cssColor(in: theme.css, names: ["bg-primary", "bg-secondary", "text-primary"]) {
            return color
        }
        return theme.isDark ? .darkGray : .white
    }

    private static func cssColor(in css: String, names: [String]) -> NSColor? {
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
            model.selectTheme(at: table.selectedRow)
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
