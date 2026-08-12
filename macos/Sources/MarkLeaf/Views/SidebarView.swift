import AppKit

/// 侧边栏：工作区文件树（对应 C# WorkspaceTreeView + SidebarTabBar）。
final class SidebarView: NSView {
    static let emptyStateIdentifier = NSUserInterfaceItemIdentifier("Sidebar.emptyState")

    let session: EditorSession
    private let localize: (String) -> String
    private let persistSidebarTab: (String) -> Void

    let tabControl: NSSegmentedControl
    let headerOpenFolderButton = NSButton()
    let emptyStateLabel: NSTextField
    let emptyStateOpenFolderButton: NSButton
    let emptyStateView: NSStackView
    private let containerView = NSView()
    private let workspaceTree = WorkspaceTreeView()
    private let outlineTree = OutlineTreeView()
    private let workspaceScroll = NSScrollView()
    private let outlineScroll = NSScrollView()

    init(
        session: EditorSession,
        persistSidebarTab: @escaping (String) -> Void = { tab in
            if SettingsService.shared.settings.sidebarTab != tab {
                SettingsService.shared.update { $0.sidebarTab = tab }
            }
        },
        localize: @escaping (String) -> String = { L10n.t($0) }
    ) {
        self.session = session
        self.localize = localize
        self.persistSidebarTab = persistSidebarTab
        tabControl = NSSegmentedControl(
            labels: [localize("工作区"), localize("大纲")],
            trackingMode: .selectOne,
            target: nil,
            action: nil
        )
        emptyStateLabel = NSTextField(labelWithString: localize("暂未打开工作区"))
        emptyStateOpenFolderButton = NSButton(
            title: localize("打开文件夹"),
            target: nil,
            action: nil
        )
        emptyStateView = NSStackView()
        super.init(frame: .zero)

        tabControl.selectedSegment = 0
        tabControl.controlSize = .regular
        tabControl.font = .systemFont(ofSize: 13)
        tabControl.target = self
        tabControl.action = #selector(tabChanged)

        headerOpenFolderButton.image = NSImage(
            systemSymbolName: "folder.badge.plus",
            accessibilityDescription: nil
        )
        headerOpenFolderButton.title = ""
        headerOpenFolderButton.imagePosition = .imageOnly
        headerOpenFolderButton.bezelStyle = .rounded
        headerOpenFolderButton.controlSize = .regular
        headerOpenFolderButton.target = self
        headerOpenFolderButton.action = #selector(openFolder)
        headerOpenFolderButton.translatesAutoresizingMaskIntoConstraints = false
        headerOpenFolderButton.widthAnchor.constraint(equalToConstant: 32).isActive = true

        let header = NSStackView(views: [tabControl, NSView(), headerOpenFolderButton])
        header.orientation = .horizontal
        header.spacing = 6
        header.alignment = .centerY
        header.translatesAutoresizingMaskIntoConstraints = false

        emptyStateLabel.alignment = .center
        emptyStateLabel.textColor = .secondaryLabelColor
        emptyStateLabel.font = .systemFont(ofSize: 12)

        emptyStateOpenFolderButton.bezelStyle = .rounded
        emptyStateOpenFolderButton.controlSize = .regular
        emptyStateOpenFolderButton.target = self
        emptyStateOpenFolderButton.action = #selector(openFolder)

        emptyStateView.orientation = .vertical
        emptyStateView.alignment = .centerX
        emptyStateView.spacing = 10
        emptyStateView.identifier = Self.emptyStateIdentifier
        emptyStateView.addArrangedSubview(emptyStateLabel)
        emptyStateView.addArrangedSubview(emptyStateOpenFolderButton)
        emptyStateView.translatesAutoresizingMaskIntoConstraints = false

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
        addSubview(emptyStateView)
        NSLayoutConstraint.activate([
            header.topAnchor.constraint(equalTo: topAnchor, constant: 11),
            header.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 6),
            header.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -6),
            containerView.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 4),
            containerView.leadingAnchor.constraint(equalTo: leadingAnchor),
            containerView.trailingAnchor.constraint(equalTo: trailingAnchor),
            containerView.bottomAnchor.constraint(equalTo: bottomAnchor),
            emptyStateView.centerXAnchor.constraint(equalTo: containerView.centerXAnchor),
            emptyStateView.centerYAnchor.constraint(equalTo: containerView.centerYAnchor),
        ])

        session.onWorkspaceChanged = { [weak self] in
            DispatchQueue.main.async { self?.workspaceChanged() }
        }
        session.onOutlineChanged = { [weak self] in
            DispatchQueue.main.async { self?.outlineChanged() }
        }
        applyLanguage()
        showTab(session.sidebarTabIndex, persist: false)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    /// 界面语言切换：更新分段标签、打开文件夹控件与空状态文案。
    func applyLanguage() {
        tabControl.setLabel(localize("工作区"), forSegment: 0)
        tabControl.setLabel(localize("大纲"), forSegment: 1)
        let openFolderTitle = localize("打开文件夹")
        headerOpenFolderButton.toolTip = openFolderTitle
        headerOpenFolderButton.setAccessibilityLabel(openFolderTitle)
        emptyStateLabel.stringValue = localize("暂未打开工作区")
        emptyStateOpenFolderButton.title = openFolderTitle
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

    private func showTab(_ index: Int, persist: Bool = true) {
        tabControl.selectedSegment = index
        // 先同步会话标签索引：workspaceChanged/outlineChanged 会读取它判断占位文案
        session.sidebarTabIndex = index
        if persist {
            let tab = index == 1 ? "outline" : "workspace"
            persistSidebarTab(tab)
        }
        let workspaceActive = index == 0
        headerOpenFolderButton.isHidden = !workspaceActive
        updateEmptyStateVisibility(hasWorkspace: session.workspaceRoot != nil)
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
        updateEmptyStateVisibility(hasWorkspace: session.workspaceRoot != nil)
        workspaceTree.reloadData()
    }

    func updateEmptyStateVisibility(hasWorkspace: Bool) {
        emptyStateView.isHidden = !(session.sidebarTabIndex == 0 && !hasWorkspace)
    }

    private func outlineChanged() {
        outlineTree.reloadData()
    }

    @objc private func openFolder() {
        let panel = NSOpenPanel()
        panel.title = localize("打开工作区文件夹")
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

final class FinderWorkspaceRowView: NSTableRowView {
    override var isEmphasized: Bool {
        get { false }
        set { super.isEmphasized = false }
    }
}

enum SidebarTreePresentation {
    static let rowFont = NSFont.systemFont(ofSize: 13, weight: .regular)

    static func apply(to outlineView: NSOutlineView) {
        outlineView.rowSizeStyle = .medium
        outlineView.selectionHighlightStyle = .sourceList
        outlineView.rowHeight = 26
        outlineView.backgroundColor = .clear
        outlineView.columnAutoresizingStyle = .uniformColumnAutoresizingStyle
    }
}

class WorkspaceTreeView: NSOutlineView, NSOutlineViewDataSource, NSOutlineViewDelegate {
    static let localDragPasteboardType = NSPasteboard.PasteboardType("com.markleaf.workspace-entry-path")
    private weak var session: EditorSession?
    private var childrenCache: [String: [WorkspaceEntry]] = [:]
    /// 进行中的子目录扫描（强引用，避免扫描器在异步任务执行前被释放导致子目录永远不加载）。
    private var activeScanners: [String: WorkspaceScanner] = [:]
    private let queue = DispatchQueue(label: "com.markleaf.tree")

    private var listMode = false
    private var lastDirectoryNameClick: (path: String, timestamp: TimeInterval)?
    private var beganDraggingEntryDuringMouseDown = false
    func configure(session: EditorSession) {
        self.session = session
        let column = NSTableColumn(identifier: .init("name"))
        column.title = ""
        addTableColumn(column)
        outlineTableColumn = column
        headerView = nil
        dataSource = self
        delegate = self
        SidebarTreePresentation.apply(to: self)
        registerForDraggedTypes([.fileURL, Self.localDragPasteboardType])
        setDraggingSourceOperationMask(.move, forLocal: true)
        setDraggingSourceOperationMask(.copy, forLocal: false)
    }

    func setListMode(_ listMode: Bool) {
        self.listMode = listMode
        reloadData()
    }

    /// 树形模式：目录名称双击切换展开/收起，三角按钮保留系统单击行为。
    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let row = row(at: point)
        let fileToActivate: WorkspaceEntry?
        if event.clickCount == 1, row >= 0 {
            fileToActivate = item(atRow: row) as? WorkspaceEntry
        } else {
            fileToActivate = nil
        }
        beganDraggingEntryDuringMouseDown = false
        var directoryRowToToggle: Int?
        var clickedDirectoryName = false
        if row >= 0, !listMode,
           let item = item(atRow: row),
           let entry = item as? WorkspaceEntry, entry.isDirectory {
            let outlineFrame = frameOfOutlineCell(atRow: row)
            // 名称单击只选择，双击才切换展开状态；三角按钮仍保留系统单击行为。
            if !outlineFrame.contains(point) {
                clickedDirectoryName = true
                let isRepeatedClick = lastDirectoryNameClick.map {
                    let elapsed = event.timestamp - $0.timestamp
                    return $0.path == entry.path && elapsed >= 0 && elapsed <= NSEvent.doubleClickInterval
                } ?? false
                let isSecondClick = event.clickCount >= 2 || isRepeatedClick
                if isSecondClick {
                    directoryRowToToggle = row
                    lastDirectoryNameClick = nil
                } else {
                    lastDirectoryNameClick = (entry.path, event.timestamp)
                }
            }
        }
        if !clickedDirectoryName {
            lastDirectoryNameClick = nil
        }
        if let row = directoryRowToToggle {
            performNativeDisclosureClick(atRow: row)
            return
        }
        super.mouseDown(with: event)
        guard !beganDraggingEntryDuringMouseDown,
              let entry = fileToActivate,
              !entry.isDirectory
        else { return }
        activateWorkspaceEntry(entry)
    }

    override func beginDraggingSession(
        with items: [NSDraggingItem],
        event: NSEvent,
        source: NSDraggingSource
    ) -> NSDraggingSession {
        beganDraggingEntryDuringMouseDown = true
        return super.beginDraggingSession(with: items, event: event, source: source)
    }

    func activateWorkspaceEntry(_ entry: WorkspaceEntry) {
        session?.openWorkspaceEntry(entry)
    }

    /// 将目录名称点击转发到系统 disclosure control，复用三角按钮原生展开/收起动画。
    private func performNativeDisclosureClick(atRow row: Int) {
        let outlineFrame = frameOfOutlineCell(atRow: row)
        guard !outlineFrame.isEmpty,
              let item = item(atRow: row)
        else { return }
        let wasExpanded = isItemExpanded(item)
        if let disclosureButton = hitTest(NSPoint(x: outlineFrame.midX, y: outlineFrame.midY)) as? NSButton {
            disclosureButton.state = wasExpanded ? .on : .off
            performNativeDisclosureClick(disclosureButton)
            if isItemExpanded(item) != wasExpanded { return }
        }
        if wasExpanded {
            collapseItem(item)
        } else {
            expandItem(item)
        }
    }

    func performNativeDisclosureClick(_ disclosureButton: NSButton) {
        disclosureButton.performClick(nil)
    }

    override func reloadData() {
        activeScanners.values.forEach { $0.cancel() }
        activeScanners.removeAll()
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

    func outlineView(_ outlineView: NSOutlineView, pasteboardWriterForItem item: Any) -> NSPasteboardWriting? {
        guard let entry = item as? WorkspaceEntry else { return nil }
        beganDraggingEntryDuringMouseDown = true
        let pasteboardItem = NSPasteboardItem()
        pasteboardItem.setString(URL(fileURLWithPath: entry.path).absoluteString, forType: .fileURL)
        pasteboardItem.setString(entry.path, forType: Self.localDragPasteboardType)
        return pasteboardItem
    }

    override func draggingSession(
        _ session: NSDraggingSession,
        sourceOperationMaskFor context: NSDraggingContext
    ) -> NSDragOperation {
        context == .withinApplication ? .move : .copy
    }

    func dropTargetDirectory(for item: Any?, workspaceRoot: String?) -> URL? {
        guard let workspaceRoot else { return nil }
        guard let item else { return URL(fileURLWithPath: workspaceRoot, isDirectory: true) }
        guard let entry = item as? WorkspaceEntry, entry.isDirectory else { return nil }
        return URL(fileURLWithPath: entry.path, isDirectory: true)
    }

    func outlineView(
        _ outlineView: NSOutlineView,
        validateDrop info: NSDraggingInfo,
        proposedItem item: Any?,
        proposedChildIndex index: Int
    ) -> NSDragOperation {
        guard info.draggingPasteboard.string(forType: Self.localDragPasteboardType) != nil,
              dropTargetDirectory(for: item, workspaceRoot: session?.workspaceRoot) != nil
        else { return [] }
        setDropItem(item, dropChildIndex: NSOutlineViewDropOnItemIndex)
        return .move
    }

    func outlineView(
        _ outlineView: NSOutlineView,
        acceptDrop info: NSDraggingInfo,
        item: Any?,
        childIndex index: Int
    ) -> Bool {
        guard let sourcePath = info.draggingPasteboard.string(forType: Self.localDragPasteboardType),
              let target = dropTargetDirectory(for: item, workspaceRoot: session?.workspaceRoot)
        else { return false }
        do {
            try session?.moveWorkspaceEntry(from: URL(fileURLWithPath: sourcePath), toDirectory: target)
            return true
        } catch {
            session?.presentError(L10n.f("无法移动工作区项目：%@", error.localizedDescription))
            return false
        }
    }

    // MARK: - Delegate

    func outlineView(_ outlineView: NSOutlineView, rowViewForItem item: Any) -> NSTableRowView? {
        FinderWorkspaceRowView()
    }

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
        cell.textField?.font = SidebarTreePresentation.rowFont
        cell.imageView?.image = NSWorkspace.shared.icon(forFile: entry.path)
        return cell
    }

    func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool {
        return item is WorkspaceEntry
    }

    func outlineView(_ outlineView: NSOutlineView, menuFor event: NSEvent) -> NSMenu? {
        let point = convert(event.locationInWindow, from: nil)
        let row = row(at: point)
        guard row >= 0, let entry = item(atRow: row) as? WorkspaceEntry else { return nil }
        selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)

        let menu = NSMenu()
        if !entry.isDirectory {
            menu.addItem(item(L10n.t("打开"), #selector(openEntry(_:)), entry))
            menu.addItem(.separator())
        }
        menu.addItem(item(L10n.t("在 Finder 中显示"), #selector(revealInFinder(_:)), entry))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("刷新工作区"), #selector(refreshWorkspace(_:)), nil))
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

    func reloadDirectoryChildren(_ entry: WorkspaceEntry) {
        reloadItem(entry, reloadChildren: true)
    }

    // MARK: - 懒加载子目录

    private func children(for entry: WorkspaceEntry) -> [WorkspaceEntry] {
        if let cached = childrenCache[entry.path] {
            return cached
        }
        let scanner = WorkspaceScanner(root: entry.path) { [weak self] entries in
            DispatchQueue.main.async {
                guard let self else { return }
                self.childrenCache[entry.path] = entries
                self.activeScanners[entry.path] = nil
                self.reloadDirectoryChildren(entry)
            }
        }
        activeScanners[entry.path] = scanner
        scanner.scan()
        return []
    }
}

// MARK: - 大纲树

final class OutlineTreeView: NSOutlineView, NSOutlineViewDataSource, NSOutlineViewDelegate {
    static let headingLeadingConstraintIdentifier = "OutlineHeadingLeading"
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
        SidebarTreePresentation.apply(to: self)
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
            let leading = textField.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 2)
            leading.identifier = Self.headingLeadingConstraintIdentifier
            NSLayoutConstraint.activate([
                leading,
                textField.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -2),
                textField.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
            ])
            return cell
        }()
        cell.textField?.stringValue = heading.text
        cell.textField?.font = SidebarTreePresentation.rowFont
        let indent = CGFloat(max(0, heading.level - 1)) * 12
        cell.constraints.first {
            $0.identifier == Self.headingLeadingConstraintIdentifier
        }?.constant = 2 + indent
        return cell
    }

    /// 与 Workspace 共用非强调的 source-list 选中样式，呈现系统灰色阴影而非蓝色高亮。
    func outlineView(_ outlineView: NSOutlineView, rowViewForItem item: Any) -> NSTableRowView? {
        FinderWorkspaceRowView()
    }

    func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool {
        if let heading = item as? OutlineHeading {
            session?.scrollToPosition(heading.position)
        }
        return item is OutlineHeading
    }

}
