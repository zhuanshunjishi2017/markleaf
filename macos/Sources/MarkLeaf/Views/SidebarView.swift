import AppKit

/// 侧边栏：工作区文件树（对应 C# WorkspaceTreeView + SidebarTabBar）。
final class SidebarView: NSView {
    let session: EditorSession

    private let tabControl = NSSegmentedControl(labels: [L10n.t("工作区"), L10n.t("大纲")], trackingMode: .selectOne, target: nil, action: nil)
    private let containerView = NSView()
    private let workspaceTree = WorkspaceTreeView()
    private let outlineTree = OutlineTreeView()
    private let workspaceScroll = NSScrollView()
    private let outlineScroll = NSScrollView()
    private let openFolderButton = NSButton(title: L10n.t("打开文件夹"), target: nil, action: nil)
    private let placeholder = NSTextField(labelWithString: "暂未打开工作区\n点击“打开文件夹”开始")

    init(session: EditorSession) {
        self.session = session
        super.init(frame: .zero)

        tabControl.selectedSegment = 0
        tabControl.controlSize = .regular
        tabControl.font = .systemFont(ofSize: 13)
        tabControl.target = self
        tabControl.action = #selector(tabChanged)

        openFolderButton.bezelStyle = .rounded
        openFolderButton.controlSize = .regular
        openFolderButton.target = self
        openFolderButton.action = #selector(openFolder)
        let header = NSStackView(views: [tabControl, NSView(), openFolderButton])
        header.orientation = .horizontal
        header.spacing = 6
        header.alignment = .centerY
        header.translatesAutoresizingMaskIntoConstraints = false

        placeholder.alignment = .center
        placeholder.textColor = .secondaryLabelColor
        placeholder.font = .systemFont(ofSize: 12)
        placeholder.translatesAutoresizingMaskIntoConstraints = false

        containerView.translatesAutoresizingMaskIntoConstraints = false
        workspaceTree.configure(session: session)
        outlineTree.configure(session: session)

        // 树放入滚动容器，Auto Layout 固定填满；两棵常驻，用 isHidden 切换
        workspaceScroll.documentView = workspaceTree
        workspaceScroll.hasVerticalScroller = true
        workspaceScroll.drawsBackground = false
        workspaceScroll.translatesAutoresizingMaskIntoConstraints = false
        outlineScroll.documentView = outlineTree
        outlineScroll.hasVerticalScroller = true
        outlineScroll.drawsBackground = false
        outlineScroll.translatesAutoresizingMaskIntoConstraints = false
        containerView.addSubview(workspaceScroll)
        containerView.addSubview(outlineScroll)
        NSLayoutConstraint.activate([
            workspaceScroll.leadingAnchor.constraint(equalTo: containerView.leadingAnchor),
            workspaceScroll.trailingAnchor.constraint(equalTo: containerView.trailingAnchor),
            workspaceScroll.topAnchor.constraint(equalTo: containerView.topAnchor),
            workspaceScroll.bottomAnchor.constraint(equalTo: containerView.bottomAnchor),
            outlineScroll.leadingAnchor.constraint(equalTo: containerView.leadingAnchor),
            outlineScroll.trailingAnchor.constraint(equalTo: containerView.trailingAnchor),
            outlineScroll.topAnchor.constraint(equalTo: containerView.topAnchor),
            outlineScroll.bottomAnchor.constraint(equalTo: containerView.bottomAnchor),
        ])
        outlineScroll.isHidden = true

        addSubview(header)
        addSubview(containerView)
        addSubview(placeholder)
        NSLayoutConstraint.activate([
            header.topAnchor.constraint(equalTo: topAnchor, constant: 11),
            header.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 6),
            header.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -6),
            containerView.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 4),
            containerView.leadingAnchor.constraint(equalTo: leadingAnchor),
            containerView.trailingAnchor.constraint(equalTo: trailingAnchor),
            containerView.bottomAnchor.constraint(equalTo: bottomAnchor),
            placeholder.centerXAnchor.constraint(equalTo: containerView.centerXAnchor),
            placeholder.centerYAnchor.constraint(equalTo: containerView.centerYAnchor),
        ])

        session.onWorkspaceChanged = { [weak self] in
            DispatchQueue.main.async { self?.workspaceChanged() }
        }
        session.onOutlineChanged = { [weak self] in
            DispatchQueue.main.async { self?.outlineChanged() }
        }
        showTab(0)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    /// 界面语言切换：更新分段标签、打开文件夹按钮与占位文案。
    func applyLanguage() {
        tabControl.setLabel(L10n.t("工作区"), forSegment: 0)
        tabControl.setLabel(L10n.t("大纲"), forSegment: 1)
        openFolderButton.title = L10n.t("打开文件夹")
        placeholder.stringValue = L10n.t("暂未打开工作区\n点击“打开文件夹”开始")
    }

    @objc private func tabChanged() {
        showTab(tabControl.selectedSegment)
    }

    /// 外部（视图菜单）切换标签。
    func selectTab(_ index: Int) {
        tabControl.selectedSegment = index
        showTab(index)
    }

    /// 外部（视图菜单）切换树/列表模式。
    func setWorkspaceMode(listMode: Bool) {
        workspaceTree.setListMode(listMode)
        if listMode {
            session.setWorkspaceListMode(true)
        } else {
            session.setWorkspaceListMode(false)
        }
        showTab(tabControl.selectedSegment)
    }

    private func showTab(_ index: Int) {
        tabControl.selectedSegment = index
        // 先同步会话标签索引：workspaceChanged/outlineChanged 会读取它判断占位文案
        session.sidebarTabIndex = index
        let workspaceActive = index == 0
        openFolderButton.isHidden = !workspaceActive
        placeholder.isHidden = !(workspaceActive && session.workspaceRoot == nil)
        if workspaceActive {
            workspaceChanged()
        } else {
            outlineChanged()
        }
        // 交叉淡入淡出切换
        workspaceScroll.isHidden = false
        outlineScroll.isHidden = false
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.18
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            workspaceScroll.animator().alphaValue = workspaceActive ? 1 : 0
            outlineScroll.animator().alphaValue = workspaceActive ? 0 : 1
        } completionHandler: { [weak self] in
            self?.workspaceScroll.isHidden = !workspaceActive
            self?.outlineScroll.isHidden = workspaceActive
        }
    }

    private func workspaceChanged() {
        let hasWorkspace = session.workspaceRoot != nil
        placeholder.isHidden = !(session.sidebarTabIndex == 0 && !hasWorkspace)
        workspaceTree.reloadData()
    }

    private func outlineChanged() {
        outlineTree.reloadData()
    }

    @objc private func openFolder() {
        let panel = NSOpenPanel()
        panel.title = "打开工作区文件夹"
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = false
        guard let window = window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.session.loadWorkspace(url.path)
        }
    }
}

// MARK: - 工作区文件树

final class WorkspaceTreeView: NSOutlineView, NSOutlineViewDataSource, NSOutlineViewDelegate {
    private weak var session: EditorSession?
    private var childrenCache: [String: [WorkspaceEntry]] = [:]
    private let queue = DispatchQueue(label: "com.markleaf.tree")

    private var listMode = false

    func configure(session: EditorSession) {
        self.session = session
        let column = NSTableColumn(identifier: .init("name"))
        column.title = ""
        addTableColumn(column)
        outlineTableColumn = column
        headerView = nil
        dataSource = self
        delegate = self
        rowSizeStyle = .medium
        selectionHighlightStyle = .sourceList
        rowHeight = 26
        backgroundColor = .clear
        columnAutoresizingStyle = .uniformColumnAutoresizingStyle
    }

    func setListMode(_ listMode: Bool) {
        self.listMode = listMode
        reloadData()
    }

    override func reloadData() {
        childrenCache.removeAll()
        super.reloadData()
    }

    private func rootEntries() -> [WorkspaceEntry] {
        if listMode {
            return session?.workspaceDocuments ?? []
        }
        return session?.workspaceTree ?? []
    }

    // MARK: - DataSource

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        if let entry = item as? WorkspaceEntry, entry.isDirectory {
            return children(for: entry).count
        }
        return item == nil ? rootEntries().count : 0
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        if let entry = item as? WorkspaceEntry {
            return children(for: entry)[index]
        }
        return rootEntries()[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        if listMode { return false }
        return (item as? WorkspaceEntry)?.isDirectory ?? false
    }

    // MARK: - Delegate

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let entry = item as? WorkspaceEntry else { return nil }
        let id = NSUserInterfaceItemIdentifier("cell")
        let cell = (outlineView.makeView(withIdentifier: id, owner: self) as? NSTableCellView) ?? {
            let cell = NSTableCellView()
            cell.identifier = id
            let imageView = NSImageView()
            imageView.translatesAutoresizingMaskIntoConstraints = false
            let textField = NSTextField(labelWithString: "")
            textField.translatesAutoresizingMaskIntoConstraints = false
            textField.lineBreakMode = .byTruncatingTail
            cell.addSubview(imageView)
            cell.addSubview(textField)
            cell.imageView = imageView
            cell.textField = textField
            NSLayoutConstraint.activate([
                imageView.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 6),
                imageView.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
                imageView.widthAnchor.constraint(equalToConstant: 16),
                imageView.heightAnchor.constraint(equalToConstant: 16),
                textField.leadingAnchor.constraint(equalTo: imageView.trailingAnchor, constant: 4),
                textField.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -2),
                textField.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()
        cell.textField?.stringValue = entry.name
        cell.imageView?.image = NSWorkspace.shared.icon(forFile: entry.path)
        return cell
    }

    func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool {
        if let entry = item as? WorkspaceEntry, !entry.isDirectory {
            session?.openWorkspaceEntry(entry)
        }
        return (item as? WorkspaceEntry)?.isDirectory == true
    }

    func outlineView(_ outlineView: NSOutlineView, menuFor event: NSEvent) -> NSMenu? {
        let point = convert(event.locationInWindow, from: nil)
        let row = row(at: point)
        guard row >= 0, let entry = item(atRow: row) as? WorkspaceEntry else { return nil }
        selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)

        let menu = NSMenu()
        if !entry.isDirectory {
            menu.addItem(item("打开", #selector(openEntry(_:)), entry))
            menu.addItem(.separator())
        }
        menu.addItem(item("在 Finder 中显示", #selector(revealInFinder(_:)), entry))
        menu.addItem(.separator())
        menu.addItem(item("刷新工作区", #selector(refreshWorkspace(_:)), nil))
        return menu
    }

    @objc private func openEntry(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            session?.openWorkspaceEntry(entry)
        }
    }

    @objc private func revealInFinder(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: entry.path)])
        }
    }

    @objc private func refreshWorkspace(_ sender: NSMenuItem) {
        if let root = session?.workspaceRoot {
            session?.loadWorkspace(root)
        }
    }

    private func item(_ title: String, _ action: Selector, _ object: Any?) -> NSMenuItem {
        let menuItem = NSMenuItem(title: title, action: action, keyEquivalent: "")
        menuItem.target = self
        menuItem.representedObject = object
        return menuItem
    }

    // MARK: - 懒加载子目录

    private func children(for entry: WorkspaceEntry) -> [WorkspaceEntry] {
        if let cached = childrenCache[entry.path] {
            return cached
        }
        let scanner = WorkspaceScanner(root: entry.path) { [weak self] entries in
            DispatchQueue.main.async {
                self?.childrenCache[entry.path] = entries
                self?.reloadItem(entry)
            }
        }
        scanner.scan()
        return []
    }
}

// MARK: - 大纲树

final class OutlineTreeView: NSOutlineView, NSOutlineViewDataSource, NSOutlineViewDelegate {
    private weak var session: EditorSession?

    func configure(session: EditorSession) {
        self.session = session
        let column = NSTableColumn(identifier: .init("heading"))
        column.title = ""
        addTableColumn(column)
        outlineTableColumn = column
        headerView = nil
        dataSource = self
        delegate = self
        rowSizeStyle = .large
        selectionHighlightStyle = .sourceList
        rowHeight = 28
        intercellSpacing = NSSize(width: 0, height: 3)
        backgroundColor = .clear
        columnAutoresizingStyle = .uniformColumnAutoresizingStyle
    }

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        item == nil ? (session?.outlineHeadings.count ?? 0) : 0
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        session?.outlineHeadings[index] as Any
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        false
    }

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let heading = item as? OutlineHeading else { return nil }
        let id = NSUserInterfaceItemIdentifier("heading")
        let cell = (outlineView.makeView(withIdentifier: id, owner: self) as? NSTableCellView) ?? {
            let cell = NSTableCellView()
            cell.identifier = id
            let textField = NSTextField(labelWithString: "")
            textField.translatesAutoresizingMaskIntoConstraints = false
            textField.lineBreakMode = .byTruncatingTail
            cell.addSubview(textField)
            cell.textField = textField
            NSLayoutConstraint.activate([
                textField.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 8),
                textField.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -2),
                textField.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()
        cell.textField?.stringValue = heading.text
        cell.textField?.font = .systemFont(ofSize: 13, weight: heading.level <= 2 ? .semibold : .regular)
        let indent = CGFloat(max(0, heading.level - 1)) * 12
        cell.textField?.constraints.first { $0.firstAttribute == .leading }?.constant = 2 + indent
        return cell
    }

    func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool {
        if let heading = item as? OutlineHeading {
            session?.scrollToPosition(heading.position)
        }
        return false
    }

}
