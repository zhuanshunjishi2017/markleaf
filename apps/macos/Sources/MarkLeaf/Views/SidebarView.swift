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
    private let searchField = NSSearchField()
    private let searchScroll = NSScrollView()
    private let searchResults = WorkspaceSearchResultsView()
    private let searchService = WorkspaceSearchService()
    private var isSearching = false
    private var tabTransitionGeneration = 0

    /// Exposes the native search field to @testable layout regression tests.
    var searchFieldForTesting: NSSearchField { searchField }

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
            systemSymbolName: "doc.badge.plus",
            accessibilityDescription: nil
        )
        headerOpenFolderButton.title = ""
        headerOpenFolderButton.imagePosition = .imageOnly
        headerOpenFolderButton.bezelStyle = .rounded
        headerOpenFolderButton.controlSize = .regular
        headerOpenFolderButton.target = self
        headerOpenFolderButton.action = #selector(newMarkdownFileFromHeader)
        headerOpenFolderButton.translatesAutoresizingMaskIntoConstraints = false
        headerOpenFolderButton.widthAnchor.constraint(equalToConstant: 32).isActive = true

        searchField.translatesAutoresizingMaskIntoConstraints = false
        // 使用 small 的搜索控件，避免 regular 在 Retina 下撑满工具栏；
        // 通过显式高度把它从“略小”调整到与其他控件视觉接近。
        searchField.controlSize = .small
        searchField.sendsSearchStringImmediately = true
        searchField.target = self
        searchField.action = #selector(searchChanged(_:))
        searchField.heightAnchor.constraint(equalToConstant: 26).isActive = true
        searchField.widthAnchor.constraint(greaterThanOrEqualToConstant: 72).isActive = true
        searchField.setAccessibilityLabel(localize("搜索"))

        // Keep navigation and search on separate rows so localized labels never
        // compete for the same horizontal space.
        let navigationRow = NSStackView(views: [tabControl, NSView(), headerOpenFolderButton])
        navigationRow.orientation = .horizontal
        navigationRow.spacing = 6
        navigationRow.alignment = .centerY

        let searchRow = NSStackView(views: [searchField])
        searchRow.orientation = .horizontal
        searchRow.alignment = .width

        let header = NSStackView(views: [navigationRow, searchRow])
        header.orientation = .vertical
        header.spacing = 6
        header.alignment = .width
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
        outlineTree.configure(session: session) { [weak session] heading in
            session?.scrollToPosition(heading.position)
        }

        // 树放入滚动容器，Auto Layout 固定填满；两棵常驻，用 isHidden 切换
        workspaceScroll.documentView = workspaceTree
        workspaceScroll.hasVerticalScroller = true
        workspaceScroll.drawsBackground = false
        workspaceScroll.translatesAutoresizingMaskIntoConstraints = false
        outlineScroll.documentView = outlineTree
        outlineScroll.hasVerticalScroller = true
        outlineScroll.drawsBackground = false
        outlineScroll.translatesAutoresizingMaskIntoConstraints = false
        searchResults.configure()
        searchResults.onActivate = { [weak self] result in
            guard let self else { return }
            // 对齐 Windows：先退出搜索模式，再打开文件，并在工作区树中定位到该文件。
            self.endSearch()
            self.session.openWorkspaceEntry(result.entry)
            self.workspaceTree.revealPath(result.entry.path)
        }
        searchScroll.documentView = searchResults
        searchScroll.hasVerticalScroller = true
        searchScroll.drawsBackground = false
        searchScroll.translatesAutoresizingMaskIntoConstraints = false
        containerView.addSubview(workspaceScroll)
        containerView.addSubview(outlineScroll)
        containerView.addSubview(searchScroll)
        NSLayoutConstraint.activate([
            workspaceScroll.leadingAnchor.constraint(equalTo: containerView.leadingAnchor),
            workspaceScroll.trailingAnchor.constraint(equalTo: containerView.trailingAnchor),
            workspaceScroll.topAnchor.constraint(equalTo: containerView.topAnchor),
            workspaceScroll.bottomAnchor.constraint(equalTo: containerView.bottomAnchor),
            outlineScroll.leadingAnchor.constraint(equalTo: containerView.leadingAnchor),
            outlineScroll.trailingAnchor.constraint(equalTo: containerView.trailingAnchor),
            outlineScroll.topAnchor.constraint(equalTo: containerView.topAnchor),
            outlineScroll.bottomAnchor.constraint(equalTo: containerView.bottomAnchor),
            searchScroll.leadingAnchor.constraint(equalTo: containerView.leadingAnchor),
            searchScroll.trailingAnchor.constraint(equalTo: containerView.trailingAnchor),
            searchScroll.topAnchor.constraint(equalTo: containerView.topAnchor),
            searchScroll.bottomAnchor.constraint(equalTo: containerView.bottomAnchor),
        ])
        outlineScroll.isHidden = true
        searchScroll.isHidden = true

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
        session.onOutlineSelectionChanged = { [weak self] in
            DispatchQueue.main.async { self?.outlineSelectionChanged() }
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
        let newMarkdownTitle = localize("新建 Markdown 文件")
        headerOpenFolderButton.toolTip = newMarkdownTitle
        headerOpenFolderButton.setAccessibilityLabel(newMarkdownTitle)
        emptyStateLabel.stringValue = localize("暂未打开工作区")
        emptyStateOpenFolderButton.title = openFolderTitle
        searchField.placeholderString = localize(session.sidebarTabIndex == 1 ? "搜索大纲" : "搜索")
        searchField.setAccessibilityLabel(localize("搜索"))
    }

    @objc private func tabChanged() {
        endSearch()
        searchField.stringValue = ""
        showTab(tabControl.selectedSegment)
    }

    /// 外部（视图菜单）切换标签。
    /// 视图状态同步用——外部调用默认持久化；窗口布局同步（applyViewState）传 persist: false，
    /// 避免“写设置 → onChange → 再次 applyViewState”形成递归。
    func selectTab(_ index: Int, persist: Bool = true) {
        let effectiveIndex = session.outlineDetached ? 0 : index
        tabControl.setEnabled(!session.outlineDetached, forSegment: 1)
        tabControl.selectedSegment = effectiveIndex
        showTab(effectiveIndex, persist: persist)
    }

    /// 外部（视图菜单）切换树/列表模式。
    func setWorkspaceMode(listMode: Bool) {
        workspaceTree.setListMode(listMode)
        if session.workspaceListMode != listMode {
            session.setWorkspaceListMode(listMode)
        } else if listMode, session.workspaceRoot != nil, session.workspaceDocuments.isEmpty {
            // 启动时列表模式已持久化：确保文档列表完成首次扫描。
            session.scanWorkspaceDocuments()
        }
        showTab(tabControl.selectedSegment, persist: false)
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
        if !workspaceActive {
            searchService.cancel()
        }
        if searchField.stringValue.isEmpty {
            endSearch()
        }
        headerOpenFolderButton.isHidden = !workspaceActive
        updateEmptyStateVisibility(hasWorkspace: session.workspaceRoot != nil)
        if workspaceActive {
            workspaceChanged()
        } else {
            outlineChanged()
        }
        searchField.placeholderString = localize(workspaceActive ? "搜索" : "搜索大纲")
        // 交叉淡入淡出切换；快速反向切换时旧 completion 不得覆盖新状态。
        tabTransitionGeneration += 1
        let transition = tabTransitionGeneration
        workspaceScroll.isHidden = false
        outlineScroll.isHidden = false
        if window == nil {
            workspaceScroll.alphaValue = workspaceActive ? 1 : 0
            outlineScroll.alphaValue = workspaceActive ? 0 : 1
            workspaceScroll.isHidden = !workspaceActive
            outlineScroll.isHidden = workspaceActive
            return
        }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.18
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            workspaceScroll.animator().alphaValue = workspaceActive ? 1 : 0
            outlineScroll.animator().alphaValue = workspaceActive ? 0 : 1
        } completionHandler: { [weak self] in
            guard let self,
                  SidebarTabTransitionPolicy.shouldApplyCompletion(
                    transition: transition,
                    currentTransition: self.tabTransitionGeneration
                  ) else { return }
            self.workspaceScroll.isHidden = !workspaceActive
            self.outlineScroll.isHidden = workspaceActive
            self.searchScroll.isHidden = !self.isSearching || self.session.sidebarTabIndex == 1
        }
    }

    private func workspaceChanged() {
        updateEmptyStateVisibility(hasWorkspace: session.workspaceRoot != nil)
        workspaceTree.reloadData(activePath: session.documentURL?.path)
    }

    func updateEmptyStateVisibility(hasWorkspace: Bool) {
        emptyStateView.isHidden = !(session.sidebarTabIndex == 0 && !hasWorkspace)
        searchField.isEnabled = session.sidebarTabIndex == 1 || hasWorkspace
        if !hasWorkspace && session.sidebarTabIndex == 0 && isSearching {
            endSearch()
        }
    }

    func outlineChanged() {
        outlineTree.reloadData(activePosition: session.activeOutlinePosition)
    }

    func outlineSelectionChanged() {
        outlineTree.synchronizeSelection(to: session.activeOutlinePosition)
    }

    @objc private func searchChanged(_ sender: NSSearchField) {
        let query = sender.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !query.isEmpty else {
            endSearch()
            return
        }
        isSearching = true
        // 搜索进行中先显示“搜索中…”，搜索完成后再按结果数量显示列表或“无搜索结果”。
        searchResults.setSearching()
        if session.sidebarTabIndex == 1 {
            workspaceScroll.isHidden = true
            outlineScroll.isHidden = false
            searchScroll.isHidden = true
            outlineTree.setFilter(query)
        } else if let root = session.workspaceRoot {
            workspaceScroll.isHidden = true
            outlineScroll.isHidden = true
            searchScroll.isHidden = false
            searchService.search(root: root, query: query) { [weak self] results in
                guard let self, self.searchField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines) == query else { return }
                self.searchResults.setResults(results)
            }
        } else {
            workspaceScroll.isHidden = true
            outlineScroll.isHidden = true
            searchScroll.isHidden = false
            searchResults.setResults([])
        }
    }

    private func endSearch() {
        searchService.cancel()
        isSearching = false
        // 对齐 Windows：退出搜索模式时清空搜索框文字。
        searchField.stringValue = ""
        searchResults.setResults([])
        outlineTree.setFilter("")
        searchScroll.isHidden = true
        let workspaceActive = session.sidebarTabIndex == 0
        workspaceScroll.isHidden = !workspaceActive
        outlineScroll.isHidden = workspaceActive
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

    @objc private func newMarkdownFileFromHeader() {
        guard let root = session.workspaceRoot else {
            openFolder()
            return
        }
        let directory: URL
        if let documentURL = session.documentURL,
           documentURL.path.hasPrefix(root + "/") {
            directory = documentURL.deletingLastPathComponent()
        } else {
            directory = URL(fileURLWithPath: root, isDirectory: true)
        }
        session.createWorkspaceFile(at: directory, kind: .markdown)
    }
}

/// 独立大纲：在编辑器右侧显示，与左侧工作区并存。
final class DetachedOutlineView: NSView {
    private let session: EditorSession
    private let titleLabel = NSTextField(labelWithString: L10n.t("大纲"))
    private let searchField = NSSearchField()
    private let outlineTree = OutlineTreeView()

    init(session: EditorSession) {
        self.session = session
        super.init(frame: .zero)

        titleLabel.font = .systemFont(ofSize: 13, weight: .semibold)
        searchField.placeholderString = L10n.t("搜索大纲")
        searchField.controlSize = .small
        searchField.translatesAutoresizingMaskIntoConstraints = false
        searchField.heightAnchor.constraint(equalToConstant: 26).isActive = true
        searchField.sendsSearchStringImmediately = true
        searchField.target = self
        searchField.action = #selector(searchChanged)

        outlineTree.configure(session: session)
        let scroll = NSScrollView()
        scroll.documentView = outlineTree
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false

        let header = NSStackView(views: [titleLabel, searchField])
        header.orientation = .vertical
        header.alignment = .width
        header.spacing = 7
        header.translatesAutoresizingMaskIntoConstraints = false
        scroll.translatesAutoresizingMaskIntoConstraints = false
        addSubview(header)
        addSubview(scroll)
        NSLayoutConstraint.activate([
            header.topAnchor.constraint(equalTo: topAnchor, constant: 11),
            header.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 6),
            header.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -6),
            scroll.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 4),
            scroll.leadingAnchor.constraint(equalTo: leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
        reload()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func reload() {
        outlineTree.reloadData(activePosition: session.activeOutlinePosition)
    }

    func synchronizeSelection() {
        outlineTree.synchronizeSelection(to: session.activeOutlinePosition)
    }

    func applyLanguage() {
        titleLabel.stringValue = L10n.t("大纲")
        searchField.placeholderString = L10n.t("搜索大纲")
    }

    @objc private func searchChanged() {
        outlineTree.setFilter(searchField.stringValue)
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
    static let selectedRowFont = NSFont.systemFont(ofSize: 13, weight: .bold)

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
    private var activeScannerTokens: [String: UUID] = [:]
    private let queue = DispatchQueue(label: "com.markleaf.tree")

    private var listMode = false
    private var pendingRevealPath: String?
    private var revealContinuationScheduled = false
    private var lastDirectoryNameClick: (path: String, timestamp: TimeInterval)?
    private var beganDraggingEntryDuringMouseDown = false
    private lazy var staleRowPlaceholder = WorkspaceEntry(name: "", path: "", isDirectory: false)
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
        rowHeight = listMode ? 54 : 26
        reloadData()
    }

    /// 树形模式：名称单击只选中，仅连续两次点击同一目录名称（双击）才切换展开/收起。
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
        var shouldToggleSelectedDirectory = false
        if row >= 0, !listMode,
           let item = item(atRow: row),
           let entry = item as? WorkspaceEntry, entry.isDirectory {
            let outlineFrame = frameOfOutlineCell(atRow: row)
            // 名称单击只选择；只有连续两次点击同一目录（真双击）才切换展开状态。
            if !outlineFrame.contains(point) {
                clickedDirectoryName = true
                if let last = lastDirectoryNameClick,
                   last.path == entry.path,
                   event.timestamp >= last.timestamp,
                   event.timestamp - last.timestamp <= NSEvent.doubleClickInterval {
                    shouldToggleSelectedDirectory = true
                    lastDirectoryNameClick = nil
                } else {
                    lastDirectoryNameClick = (entry.path, event.timestamp)
                }
            }
        }
        if !clickedDirectoryName {
            lastDirectoryNameClick = nil
        }
        if row >= 0 {
            selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
        }
        super.mouseDown(with: event)
        if clickedDirectoryName, shouldToggleSelectedDirectory, row >= 0 {
            directoryRowToToggle = row
        }
        if let row = directoryRowToToggle {
            performNativeDisclosureClick(atRow: row)
        }
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

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 36 || event.keyCode == 76 { // Return / Enter
            guard selectedRow >= 0, let entry = item(atRow: selectedRow) as? WorkspaceEntry else {
                super.keyDown(with: event)
                return
            }
            if entry.isDirectory {
                if isItemExpanded(entry) {
                    collapseItem(entry)
                } else {
                    expandItem(entry)
                }
            } else {
                activateWorkspaceEntry(entry)
            }
            return
        }
        super.keyDown(with: event)
    }

    func activateWorkspaceEntry(_ entry: WorkspaceEntry) {
        session?.openWorkspaceEntry(entry)
    }

    /// 在工作区树中定位文件：逐级展开祖先目录（懒加载完成后继续），最后选中文件行。
    /// 对齐 Windows RevealPathInTreeAsync。
    func revealPath(_ filePath: String) {
        pendingRevealPath = filePath
        continuePendingReveal()
    }

    private func continuePendingReveal() {
        guard let targetPath = pendingRevealPath,
              let root = session?.workspaceRoot
        else { return }

        if listMode {
            guard let entry = session?.workspaceDocuments.first(where: {
                normalizedPath($0.path) == normalizedPath(targetPath)
            }) else { return }
            pendingRevealPath = nil
            selectEntry(entry)
            return
        }

        let rootEntries = (session?.workspaceTree ?? []).map {
            WorkspaceRevealEntry(path: $0.path, isDirectory: $0.isDirectory)
        }
        let cachedChildren = childrenCache.mapValues { entries in
            entries.map { WorkspaceRevealEntry(path: $0.path, isDirectory: $0.isDirectory) }
        }
        switch WorkspaceSearchPolicy.nextRevealStep(
            root: root,
            target: targetPath,
            rootEntries: rootEntries,
            childrenByDirectory: cachedChildren
        ) {
        case .waitingForRoot:
            return
        case .loadDirectory(let path, let expandedDirectories):
            guard prepareExpandedDirectories(expandedDirectories) else { return }
            guard let directoryEntry = cachedEntry(at: path) else { return }
            _ = children(for: directoryEntry)
        case .selectFile(let path, let expandedDirectories):
            guard prepareExpandedDirectories(expandedDirectories) else { return }
            guard let file = cachedEntry(at: path) else { return }
            if selectEntry(file) {
                pendingRevealPath = nil
            } else {
                scheduleRevealContinuation()
            }
        case .invalid:
            pendingRevealPath = nil
        }
    }

    private func prepareExpandedDirectories(_ paths: [String]) -> Bool {
        let entries = Dictionary(uniqueKeysWithValues: paths.compactMap { path in
            cachedEntry(at: path).map { (normalizedPath(path), $0) }
        })
        let visible = Set(entries.compactMap { path, entry in
            row(forItem: entry) >= 0 ? path : nil
        })
        let expanded = Set(entries.compactMap { path, entry in
            isItemExpanded(entry) ? path : nil
        })
        switch WorkspaceSearchPolicy.nextExpansionStep(
            directories: paths,
            visibleDirectories: visible,
            expandedDirectories: expanded
        ) {
        case .waitingForVisibleDirectory:
            scheduleRevealContinuation()
            return false
        case .expandDirectory(let path):
            guard let entry = entries[path] else {
                scheduleRevealContinuation()
                return false
            }
            expandItem(entry)
            scheduleRevealContinuation()
            return false
        case .ready:
            return true
        }
    }

    private func scheduleRevealContinuation() {
        guard !revealContinuationScheduled else { return }
        revealContinuationScheduled = true
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.revealContinuationScheduled = false
            self.continuePendingReveal()
        }
    }

    private func cachedEntry(at path: String) -> WorkspaceEntry? {
        let entries = (session?.workspaceTree ?? []) + childrenCache.values.flatMap { $0 }
        return entries.first(where: {
            WorkspaceTreeDataSourcePolicy.shouldRestoreSelection(
                activePath: path,
                entryPath: $0.path
            )
        })
    }

    private func normalizedPath(_ path: String) -> String {
        URL(fileURLWithPath: path).standardizedFileURL.path
    }

    @discardableResult
    private func selectEntry(_ entry: WorkspaceEntry) -> Bool {
        let row = row(forItem: entry)
        guard row >= 0 else { return false }
        selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
        scrollRowToVisible(row)
        return true
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
        reloadData(activePath: nil)
    }

    /// 重载树时保留当前打开文档的选中状态；AppKit 可能在异步扫描期间继续请求旧行。
    func reloadData(activePath: String?) {
        activeScanners.values.forEach { $0.cancel() }
        activeScanners.removeAll()
        activeScannerTokens.removeAll()
        childrenCache.removeAll()
        super.reloadData()
        if let activePath {
            pendingRevealPath = activePath
        }
        scheduleRevealContinuation()
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
            let entries = children(for: entry)
            guard let safeIndex = WorkspaceTreeDataSourcePolicy.safeIndex(index, count: entries.count) else {
                return staleRowPlaceholder
            }
            return entries[safeIndex]
        }
        let entries = rootEntries()
        guard let safeIndex = WorkspaceTreeDataSourcePolicy.safeIndex(index, count: entries.count) else {
            return staleRowPlaceholder
        }
        return entries[safeIndex]
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
        let pasteboard = info.draggingPasteboard
        let isInternal = pasteboard.string(forType: Self.localDragPasteboardType) != nil
        let isExternalFile = pasteboard.availableType(from: [.fileURL]) != nil
        guard (isInternal || isExternalFile),
              dropTargetDirectory(for: item, workspaceRoot: session?.workspaceRoot) != nil
        else { return [] }
        setDropItem(item, dropChildIndex: NSOutlineViewDropOnItemIndex)
        return isInternal ? .move : .copy
    }

    func outlineView(
        _ outlineView: NSOutlineView,
        acceptDrop info: NSDraggingInfo,
        item: Any?,
        childIndex index: Int
    ) -> Bool {
        let pasteboard = info.draggingPasteboard
        guard let target = dropTargetDirectory(for: item, workspaceRoot: session?.workspaceRoot) else { return false }
        if let sourcePath = pasteboard.string(forType: Self.localDragPasteboardType) {
            do {
                try session?.moveWorkspaceEntry(from: URL(fileURLWithPath: sourcePath), toDirectory: target)
                return true
            } catch {
                session?.presentError(L10n.f("无法移动工作区项目：%@", error.localizedDescription))
                return false
            }
        }
        if pasteboard.availableType(from: [.fileURL]) != nil,
           let urls = pasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] {
            session?.importFiles(from: urls, to: target)
            return true
        }
        return false
    }

    // MARK: - Delegate

    func outlineView(_ outlineView: NSOutlineView, rowViewForItem item: Any) -> NSTableRowView? {
        FinderWorkspaceRowView()
    }

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let entry = item as? WorkspaceEntry else { return nil }
        if listMode {
            return listCell(outlineView, entry: entry)
        }
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
        let isSelected = outlineView.row(forItem: entry) == outlineView.selectedRow
        cell.textField?.font = isSelected
            ? SidebarTreePresentation.selectedRowFont
            : SidebarTreePresentation.rowFont
        cell.imageView?.image = NSWorkspace.shared.icon(forFile: entry.path)
        return cell
    }

    private func listCell(_ outlineView: NSOutlineView, entry: WorkspaceEntry) -> NSView? {
        let id = NSUserInterfaceItemIdentifier("listCell")
        let cell = (outlineView.makeView(withIdentifier: id, owner: self) as? WorkspaceListCellView) ?? {
            let cell = WorkspaceListCellView()
            cell.identifier = id
            return cell
        }()
        cell.nameLabel.stringValue = entry.name
        let isSelected = outlineView.row(forItem: entry) == outlineView.selectedRow
        cell.nameLabel.font = isSelected
            ? SidebarTreePresentation.selectedRowFont
            : NSFont.systemFont(ofSize: 13, weight: .medium)
        cell.folderLabel.stringValue = Self.folderName(for: entry, root: session?.workspaceRoot)
        cell.timeLabel.stringValue = WorkspaceDocumentTimeFormatter.format(
            Self.modificationDate(of: entry.path)
        )
        cell.imageView?.image = NSWorkspace.shared.icon(forFile: entry.path)
        return cell
    }

    private static func folderName(for entry: WorkspaceEntry, root: String?) -> String {
        guard let root else { return "" }
        let rootPath = URL(fileURLWithPath: root).standardizedFileURL.path
        let parent = URL(fileURLWithPath: entry.path).deletingLastPathComponent().path
        if parent == rootPath {
            return URL(fileURLWithPath: root).lastPathComponent
        }
        guard parent.hasPrefix(rootPath + "/") else { return parent }
        return String(parent.dropFirst(rootPath.count + 1))
    }

    private static func modificationDate(of path: String) -> Date {
        (try? FileManager.default.attributesOfItem(atPath: path)[.modificationDate] as? Date) ?? .distantPast
    }

    func outlineView(_ outlineView: NSOutlineView, shouldSelectItem item: Any) -> Bool {
        return item is WorkspaceEntry
    }

    func outlineViewSelectionDidChange(_ notification: Notification) {
        for row in 0..<numberOfRows {
            guard let cell = view(atColumn: 0, row: row, makeIfNecessary: false) else { continue }
            let selected = row == selectedRow
            if let listCell = cell as? WorkspaceListCellView {
                listCell.nameLabel.font = selected
                    ? SidebarTreePresentation.selectedRowFont
                    : NSFont.systemFont(ofSize: 13, weight: .medium)
            } else if let tableCell = cell as? NSTableCellView {
                tableCell.textField?.font = selected
                    ? SidebarTreePresentation.selectedRowFont
                    : SidebarTreePresentation.rowFont
            }
        }
    }

    func outlineView(_ outlineView: NSOutlineView, menuFor event: NSEvent) -> NSMenu? {
        let point = convert(event.locationInWindow, from: nil)
        let row = row(at: point)
        guard let root = session?.workspaceRoot else { return nil }
        let menu = NSMenu()
        guard row >= 0, let entry = item(atRow: row) as? WorkspaceEntry else {
            return backgroundMenu(in: URL(fileURLWithPath: root, isDirectory: true))
        }
        selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)

        if !entry.isDirectory {
            menu.addItem(item(L10n.t("打开"), #selector(openEntry(_:)), entry))
            menu.addItem(item(L10n.t("在新窗口中打开"), #selector(openInNewWindowEntry(_:)), entry))
            menu.addItem(.separator())
        }
        let targetDirectory = entry.isDirectory
            ? URL(fileURLWithPath: entry.path, isDirectory: true)
            : URL(fileURLWithPath: entry.path).deletingLastPathComponent()
        menu.addItem(popupItem(L10n.t("新建文件"), newFileMenu(in: targetDirectory)))
        menu.addItem(item(L10n.t("新建文件夹"), #selector(newFolder(_:)), targetDirectory.path))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("复制路径"), #selector(copyPath(_:)), entry))
        menu.addItem(item(L10n.t("在 Finder 中显示"), #selector(openLocation(_:)), entry))
        let isRoot = session?.workspaceRoot == entry.path
        if !isRoot {
            menu.addItem(.separator())
            menu.addItem(item(L10n.t("重命名"), #selector(renameEntry(_:)), entry))
            menu.addItem(item(L10n.t("删除"), #selector(deleteEntry(_:)), entry))
        }
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("树状视图"), #selector(switchTreeView(_:)), nil))
        menu.addItem(item(L10n.t("列表视图"), #selector(switchListView(_:)), nil))
        menu.addItem(popupItem(L10n.t("排序"), sortMenu()))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("刷新工作区"), #selector(refreshWorkspace(_:)), nil))
        menu.addItem(item(L10n.t("关闭工作区"), #selector(closeWorkspace(_:)), nil))
        return menu
    }

    private func backgroundMenu(in directory: URL) -> NSMenu {
        let menu = NSMenu()
        menu.addItem(popupItem(L10n.t("新建文件"), newFileMenu(in: directory)))
        menu.addItem(item(L10n.t("新建文件夹"), #selector(newFolder(_:)), directory.path))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("树状视图"), #selector(switchTreeView(_:)), nil))
        menu.addItem(item(L10n.t("列表视图"), #selector(switchListView(_:)), nil))
        menu.addItem(popupItem(L10n.t("排序"), sortMenu()))
        menu.addItem(.separator())
        menu.addItem(item(L10n.t("刷新工作区"), #selector(refreshWorkspace(_:)), nil))
        menu.addItem(item(L10n.t("关闭工作区"), #selector(closeWorkspace(_:)), nil))
        return menu
    }

    private func sortMenu() -> NSMenu {
        let menu = NSMenu(title: L10n.t("排序"))
        let order = session?.workspaceSortOrder ?? .modifiedTimeDescending
        let byName = item(L10n.t("按文件名"), #selector(sortByName(_:)), nil)
        byName.state = order == .fileNameAscending || order == .fileNameDescending ? .on : .off
        let byTime = item(L10n.t("按修改时间"), #selector(sortByModifiedTime(_:)), nil)
        byTime.state = order == .modifiedTimeAscending || order == .modifiedTimeDescending ? .on : .off
        let ascending = item(L10n.t("升序"), #selector(sortAscending(_:)), nil)
        ascending.state = order == .fileNameAscending || order == .modifiedTimeAscending ? .on : .off
        let descending = item(L10n.t("降序"), #selector(sortDescending(_:)), nil)
        descending.state = order == .fileNameDescending || order == .modifiedTimeDescending ? .on : .off
        menu.addItem(byName)
        menu.addItem(byTime)
        menu.addItem(.separator())
        menu.addItem(ascending)
        menu.addItem(descending)
        return menu
    }

    private func popupItem(_ title: String, _ submenu: NSMenu) -> NSMenuItem {
        let menuItem = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        menuItem.submenu = submenu
        return menuItem
    }

    private func newFileMenu(in directory: URL) -> NSMenu {
        let menu = NSMenu(title: L10n.t("新建文件"))
        menu.addItem(item(L10n.t("Markdown 文件"), #selector(newMarkdownFile(_:)), directory.path))
        menu.addItem(item(L10n.t("文本文件"), #selector(newPlainTextFile(_:)), directory.path))
        return menu
    }

    @objc private func openEntry(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            session?.openWorkspaceEntry(entry)
        }
    }

    @objc private func openInNewWindowEntry(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            session?.openWorkspaceEntryInNewWindow(entry)
        }
    }

    @objc private func newMarkdownFile(_ sender: NSMenuItem) {
        if let path = sender.representedObject as? String {
            session?.createWorkspaceFile(at: URL(fileURLWithPath: path, isDirectory: true), kind: .markdown)
        }
    }

    @objc private func newPlainTextFile(_ sender: NSMenuItem) {
        if let path = sender.representedObject as? String {
            session?.createWorkspaceFile(at: URL(fileURLWithPath: path, isDirectory: true), kind: .plainText)
        }
    }

    @objc private func newFolder(_ sender: NSMenuItem) {
        if let path = sender.representedObject as? String {
            session?.createWorkspaceFolder(at: URL(fileURLWithPath: path, isDirectory: true))
        }
    }

    @objc private func copyPath(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            session?.copyWorkspaceEntryPath(entry)
        }
    }

    @objc private func openLocation(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            session?.openWorkspaceEntryInFinder(entry)
        }
    }

    @objc private func renameEntry(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            session?.renameWorkspaceEntry(entry)
        }
    }

    @objc private func deleteEntry(_ sender: NSMenuItem) {
        if let entry = sender.representedObject as? WorkspaceEntry {
            session?.deleteWorkspaceEntry(entry)
        }
    }

    @objc private func switchTreeView(_ sender: NSMenuItem) {
        session?.setWorkspaceListMode(false)
    }

    @objc private func switchListView(_ sender: NSMenuItem) {
        session?.setWorkspaceListMode(true)
    }

    @objc private func sortByName(_ sender: NSMenuItem) {
        let descending = session?.workspaceSortOrder == .fileNameDescending
        session?.setWorkspaceSortOrder(descending ? .fileNameAscending : .fileNameDescending)
    }

    @objc private func sortByModifiedTime(_ sender: NSMenuItem) {
        let descending = session?.workspaceSortOrder == .modifiedTimeDescending
        session?.setWorkspaceSortOrder(descending ? .modifiedTimeAscending : .modifiedTimeDescending)
    }

    @objc private func sortAscending(_ sender: NSMenuItem) {
        let order = session?.workspaceSortOrder ?? .modifiedTimeDescending
        switch order {
        case .fileNameAscending, .fileNameDescending:
            session?.setWorkspaceSortOrder(.fileNameAscending)
        case .modifiedTimeAscending, .modifiedTimeDescending:
            session?.setWorkspaceSortOrder(.modifiedTimeAscending)
        }
    }

    @objc private func sortDescending(_ sender: NSMenuItem) {
        let order = session?.workspaceSortOrder ?? .modifiedTimeDescending
        switch order {
        case .fileNameAscending, .fileNameDescending:
            session?.setWorkspaceSortOrder(.fileNameDescending)
        case .modifiedTimeAscending, .modifiedTimeDescending:
            session?.setWorkspaceSortOrder(.modifiedTimeDescending)
        }
    }

    @objc private func refreshWorkspace(_ sender: NSMenuItem) {
        if let root = session?.workspaceRoot {
            session?.loadWorkspace(root)
        }
    }

    @objc private func closeWorkspace(_ sender: NSMenuItem) {
        session?.closeWorkspace()
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
        if activeScanners[entry.path] != nil {
            return []
        }
        let token = UUID()
        let scanner = WorkspaceScanner(root: entry.path) { [weak self] entries in
            DispatchQueue.main.async {
                guard let self, self.activeScannerTokens[entry.path] == token else { return }
                self.childrenCache[entry.path] = entries
                self.activeScanners[entry.path] = nil
                self.activeScannerTokens[entry.path] = nil
                self.reloadDirectoryChildren(entry)
                self.scheduleRevealContinuation()
            }
        }
        activeScanners[entry.path] = scanner
        activeScannerTokens[entry.path] = token
        scanner.scan()
        return []
    }
}

/// 文档列表模式单元格：图标 + 文件名 / 所在文件夹 / 修改时间（对齐 Windows WorkspaceDocumentListView）。
final class WorkspaceListCellView: NSTableCellView {
    let nameLabel = NSTextField(labelWithString: "")
    let folderLabel = NSTextField(labelWithString: "")
    let timeLabel = NSTextField(labelWithString: "")

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        build()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func build() {
        let imageView = NSImageView()
        imageView.translatesAutoresizingMaskIntoConstraints = false
        imageView.imageScaling = .scaleProportionallyDown
        for label in [nameLabel, folderLabel, timeLabel] {
            label.translatesAutoresizingMaskIntoConstraints = false
            label.lineBreakMode = .byTruncatingTail
            label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        }
        nameLabel.font = .systemFont(ofSize: 13, weight: .medium)
        folderLabel.font = .systemFont(ofSize: 11)
        folderLabel.textColor = .secondaryLabelColor
        timeLabel.font = .systemFont(ofSize: 11)
        timeLabel.textColor = .secondaryLabelColor
        timeLabel.alignment = .right
        timeLabel.setContentCompressionResistancePriority(.required, for: .horizontal)
        addSubview(imageView)
        addSubview(nameLabel)
        addSubview(folderLabel)
        addSubview(timeLabel)
        self.imageView = imageView
        textField = nameLabel
        NSLayoutConstraint.activate([
            imageView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 6),
            imageView.centerYAnchor.constraint(equalTo: centerYAnchor),
            imageView.widthAnchor.constraint(equalToConstant: 18),
            imageView.heightAnchor.constraint(equalToConstant: 18),
            nameLabel.leadingAnchor.constraint(equalTo: imageView.trailingAnchor, constant: 4),
            nameLabel.topAnchor.constraint(equalTo: topAnchor, constant: 6),
            nameLabel.trailingAnchor.constraint(lessThanOrEqualTo: timeLabel.leadingAnchor, constant: -6),
            folderLabel.leadingAnchor.constraint(equalTo: imageView.trailingAnchor, constant: 4),
            folderLabel.topAnchor.constraint(equalTo: nameLabel.bottomAnchor, constant: 1),
            folderLabel.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -6),
            folderLabel.trailingAnchor.constraint(lessThanOrEqualTo: timeLabel.leadingAnchor, constant: -6),
            timeLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            timeLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
    }
}

// MARK: - 大纲树

final class OutlineTreeView: NSOutlineView, NSOutlineViewDataSource, NSOutlineViewDelegate, NSMenuDelegate {
    static let headingLeadingConstraintIdentifier = "OutlineHeadingLeading"
    private weak var session: EditorSession?
    private var onHeadingActivated: ((OutlineHeading) -> Void)?
    private var isSynchronizingSelection = false
    private var suppressScrollSyncUntil: Date?
    private var filter = ""
    private var roots: [OutlineNode] = []

    func setFilter(_ value: String) {
        filter = value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        reloadData(activePosition: session?.activeOutlinePosition)
    }

    private var visibleRoots: [OutlineNode] {
        guard !filter.isEmpty else { return roots }
        return OutlineHierarchy.flatten(roots)
            .filter { $0.heading.text.lowercased().contains(filter) }
            .map { OutlineNode(heading: $0.heading) }
    }

    func configure(
        session: EditorSession,
        onHeadingActivated: ((OutlineHeading) -> Void)? = nil
    ) {
        self.session = session
        self.onHeadingActivated = onHeadingActivated ?? { [weak session] heading in
            session?.scrollToPosition(heading.position)
        }
        let column = NSTableColumn(identifier: .init("heading"))
        column.title = ""
        addTableColumn(column)
        outlineTableColumn = column
        headerView = nil
        dataSource = self
        delegate = self
        SidebarTreePresentation.apply(to: self)
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: L10n.t("全部展开"), action: #selector(expandAllHeadings), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: L10n.t("全部折叠"), action: #selector(collapseAllHeadings), keyEquivalent: ""))
        menu.addItem(.separator())
        let locateItem = NSMenuItem(title: L10n.t("定位当前标题"), action: #selector(locateCurrentHeading), keyEquivalent: "")
        locateItem.isEnabled = session.activeOutlinePosition != nil
        menu.addItem(locateItem)
        for item in menu.items { item.target = self }
        menu.delegate = self
        self.menu = menu
    }

    func synchronizeSelection(to position: Int?) {
        if let until = suppressScrollSyncUntil, Date() < until {
            return
        }
        suppressScrollSyncUntil = nil
        isSynchronizingSelection = true
        defer { isSynchronizingSelection = false }
        guard let position,
              let row = (0..<numberOfRows).first(where: {
                  (item(atRow: $0) as? OutlineNode)?.heading.position == position
              })
        else {
            deselectAll(nil)
            return
        }
        selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
        scrollRowToVisible(row)
    }

    func reloadData(activePosition: Int?) {
        roots = OutlineHierarchy.makeNodes(session?.outlineHeadings ?? [])
        super.reloadData()
        if filter.isEmpty {
            expandItem(nil, expandChildren: true)
        }
        synchronizeSelection(to: activePosition)
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        let row = row(at: point)
        if row >= 0, selectedRow != row {
            selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
        }
        super.mouseDown(with: event)
    }

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        (item as? OutlineNode)?.children.count ?? visibleRoots.count
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        (item as? OutlineNode)?.children[index] ?? visibleRoots[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        (item as? OutlineNode)?.children.isEmpty == false
    }

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let heading = (item as? OutlineNode)?.heading else { return nil }
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
        cell.textField?.font = .systemFont(ofSize: 13, weight: heading.level <= 2 ? .semibold : .regular)
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
        item is OutlineNode
    }

    func outlineViewSelectionDidChange(_ notification: Notification) {
        guard !isSynchronizingSelection,
              selectedRow >= 0,
              let heading = (item(atRow: selectedRow) as? OutlineNode)?.heading
        else { return }
        // 点击标题会滚动编辑器，随后滚动位置会回同步一次；短暂抑制以避免覆盖本次高亮。
        suppressScrollSyncUntil = Date().addingTimeInterval(0.25)
        DispatchQueue.main.async { [weak self] in
            self?.onHeadingActivated?(heading)
        }
    }

    @objc private func expandAllHeadings() {
        expandItem(nil, expandChildren: true)
    }

    @objc private func collapseAllHeadings() {
        collapseItem(nil, collapseChildren: true)
    }

    @objc private func locateCurrentHeading() {
        synchronizeSelection(to: session?.activeOutlinePosition)
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.items.last?.isEnabled = session?.activeOutlinePosition != nil
    }

}
