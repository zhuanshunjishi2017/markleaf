import AppKit

enum TabContextAction {
    case close
    case closeOthers
    case closeToRight
}

/// 右侧编辑区顶部的标签栏：文件名、脏状态圆点、关闭按钮。
/// 单击激活；关闭按钮关闭；完整路径走悬停提示；支持拖拽重排、溢出菜单与横向滚动。
final class TabBarController: NSView {
    var onActivate: ((DocumentTabID) -> Void)?
    var onClose: ((DocumentTabID) -> Void)?
    var onNewTab: (() -> Void)?
    var onContextAction: ((TabContextAction, DocumentTabID) -> Void)?
    var onReorder: ((Int, Int) -> Void)?

    private let stack = NSStackView()
    private let newTabButton = NSButton()
    private let overflowButton = NSPopUpButton(frame: .zero, pullsDown: false)
    private unowned let tabStore: TabStore
    private var cellsByTab: [DocumentTabID: TabCellView] = [:]
    private var stackLeading: NSLayoutConstraint!
    private var overflowWidth: NSLayoutConstraint!

    private var scrollOffset: CGFloat = 0
    private var reorderingTabID: DocumentTabID?
    private var isReordering = false

    init(tabStore: TabStore) {
        self.tabStore = tabStore
        super.init(frame: .zero)
        wantsLayer = true
        layer?.masksToBounds = true
        translatesAutoresizingMaskIntoConstraints = false
        heightAnchor.constraint(equalToConstant: 32).isActive = true

        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.distribution = .fillEqually
        stack.spacing = 2
        stack.edgeInsets = NSEdgeInsets(top: 4, left: 4, bottom: 4, right: 4)
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        stackLeading = stack.leadingAnchor.constraint(equalTo: leadingAnchor)
        configureNewTabButton()
        configureOverflowButton()
        NSLayoutConstraint.activate([
            stackLeading,
            stack.topAnchor.constraint(equalTo: topAnchor),
            stack.trailingAnchor.constraint(equalTo: newTabButton.leadingAnchor, constant: -4),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])

        overflowWidth = overflowButton.widthAnchor.constraint(equalToConstant: 0)
        NSLayoutConstraint.activate([
            newTabButton.widthAnchor.constraint(equalToConstant: 26),
            newTabButton.heightAnchor.constraint(equalToConstant: 26),
            newTabButton.trailingAnchor.constraint(equalTo: overflowButton.leadingAnchor, constant: -2),
            newTabButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            overflowWidth,
            overflowButton.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -4),
            overflowButton.centerYAnchor.constraint(equalTo: centerYAnchor),
            overflowButton.leadingAnchor.constraint(greaterThanOrEqualTo: stack.trailingAnchor, constant: 6),
        ])
        overflowButton.isHidden = true

        NotificationCenter.default.addObserver(
            self,
            selector: #selector(boundsChanged),
            name: NSView.boundsDidChangeNotification,
            object: self
        )
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    deinit {
        NotificationCenter.default.removeObserver(self)
    }

    private var reduceMotion: Bool {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    }

    private func configureNewTabButton() {
        newTabButton.image = NSImage(
            systemSymbolName: "plus",
            accessibilityDescription: L10n.t("新建标签")
        )
        newTabButton.imagePosition = .imageOnly
        newTabButton.isBordered = false
        newTabButton.controlSize = .small
        newTabButton.translatesAutoresizingMaskIntoConstraints = false
        newTabButton.toolTip = L10n.t("新建标签")
        newTabButton.setAccessibilityLabel(L10n.t("新建标签"))
        newTabButton.target = self
        newTabButton.action = #selector(newTabClicked)
        addSubview(newTabButton)
    }

    private func configureOverflowButton() {
        overflowButton.image = NSImage(systemSymbolName: "chevron.down", accessibilityDescription: L10n.t("所有标签"))
        overflowButton.imagePosition = .imageOnly
        overflowButton.isBordered = false
        overflowButton.controlSize = .small
        overflowButton.translatesAutoresizingMaskIntoConstraints = false
        overflowButton.refusesFirstResponder = false
        overflowButton.focusRingType = .default
        overflowButton.setAccessibilityLabel(L10n.t("所有标签"))
        addSubview(overflowButton)
    }

    @objc private func newTabClicked() {
        onNewTab?()
    }

    func reload() {
        let existing = Set(cellsByTab.keys)
        let current = Set(tabStore.tabs.map(\.tabID))
        var insertedCells: [TabCellView] = []
        for id in existing.subtracting(current) {
            if let cell = cellsByTab.removeValue(forKey: id) {
                stack.removeArrangedSubview(cell)
                cell.removeFromSuperview()
            }
        }
        for (index, tab) in tabStore.tabs.enumerated() {
            let cell: TabCellView
            if let existingCell = cellsByTab[tab.tabID] {
                cell = existingCell
            } else {
                cell = makeCell(for: tab)
                cell.alphaValue = 0
                insertedCells.append(cell)
            }
            cellsByTab[tab.tabID] = cell
            if stack.arrangedSubviews.count <= index || stack.arrangedSubviews[index] !== cell {
                stack.insertArrangedSubview(cell, at: index)
            }
            configure(cell, for: tab)
        }
        rebuildOverflowMenu()
        updateOverflowVisibility()
        guard !insertedCells.isEmpty else { return }
        layoutSubtreeIfNeeded()
        let duration = TabAnimationPolicy.duration(for: .insertRemoveReorder, reduceMotion: reduceMotion)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = duration
            context.allowsImplicitAnimation = duration > 0
            insertedCells.forEach { $0.animator().alphaValue = 1 }
        }
    }

    private func makeCell(for tab: DocumentTab) -> TabCellView {
        let cell = TabCellView()
        cell.tabID = tab.tabID
        cell.controller = self
        cell.onActivate = { [weak self] in self?.onActivate?(tab.tabID) }
        cell.onClose = { [weak self] in self?.onClose?(tab.tabID) }
        cell.onContextMenu = { [weak self, weak cell] event in
            guard let self, let cell, let id = cell.tabID else { return }
            self.showContextMenu(for: id, with: event, in: cell)
        }
        return cell
    }

    private func configure(_ cell: TabCellView, for tab: DocumentTab) {
        let isActive = tabStore.activeTabID == tab.tabID
        let duration = TabAnimationPolicy.duration(for: .activeState, reduceMotion: reduceMotion)
        cell.configure(
            title: tab.title,
            isActive: isActive,
            isDirty: tab.isDirty,
            isSuspended: tab.isSuspended,
            recoveryUnavailable: tab.recoveryUnavailable,
            toolTip: [tab.path ?? tab.title, tab.lastError].compactMap { $0 }.joined(separator: "\n"),
            animationDuration: duration,
            accessibilityTitle: tab.path ?? tab.title
        )
    }

    // MARK: - 拖拽重排

    func beginReorder(from cell: TabCellView) {
        guard isReordering == false else { return }
        isReordering = true
        reorderingTabID = cell.tabID
    }

    func dragReorder(to windowPoint: NSPoint) {
        guard isReordering, let id = reorderingTabID,
              let source = tabStore.tabs.firstIndex(where: { $0.tabID == id }) else { return }
        let local = convert(windowPoint, from: nil)
        let target = targetIndex(for: local)
        guard target != source else { return }
        let duration = TabAnimationPolicy.duration(for: .insertRemoveReorder, reduceMotion: reduceMotion)
        let cell = cellsByTab[id]
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = duration
            context.allowsImplicitAnimation = duration > 0
            if let cell {
                stack.removeArrangedSubview(cell)
                stack.insertArrangedSubview(cell, at: min(target, stack.arrangedSubviews.count))
            }
        })
    }

    func endReorder(at windowPoint: NSPoint) {
        guard isReordering, let id = reorderingTabID else {
            isReordering = false
            reorderingTabID = nil
            return
        }
        let source = tabStore.tabs.firstIndex(where: { $0.tabID == id })
        let target = targetIndex(for: convert(windowPoint, from: nil))
        isReordering = false
        reorderingTabID = nil
        if let source {
            onReorder?(source, target)
        }
    }

    private func targetIndex(for localPoint: NSPoint) -> Int {
        let p = stack.convert(localPoint, from: self)
        for (index, view) in stack.arrangedSubviews.enumerated() {
            if p.x < view.frame.midX {
                return index
            }
        }
        return stack.arrangedSubviews.count
    }

    // MARK: - 溢出菜单

    private func rebuildOverflowMenu() {
        overflowButton.menu?.removeAllItems()
        for tab in tabStore.tabs {
            let prefix = tab.isDirty ? "● " : ""
            let item = NSMenuItem(title: prefix + tab.title, action: #selector(overflowSelect(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = tab.tabID.rawValue
            item.state = tabStore.activeTabID == tab.tabID ? .on : .off
            overflowButton.menu?.addItem(item)
        }
    }

    @objc private func overflowSelect(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String else { return }
        onActivate?(DocumentTabID(raw))
    }

    @objc private func boundsChanged() {
        updateOverflowVisibility()
    }

    private func updateOverflowVisibility() {
        let show = stack.fittingSize.width > bounds.width - 34
        overflowButton.isHidden = !show
        overflowWidth.constant = show ? 26 : 0
        if !show { scrollOffset = 0 }
    }

    // MARK: - 横向滚动

    override func scrollWheel(with event: NSEvent) {
        let delta = event.hasPreciseScrollingDeltas ? event.scrollingDeltaX : event.scrollingDeltaY
        guard delta != 0 else {
            super.scrollWheel(with: event)
            return
        }
        let maxOffset = max(0, stack.fittingSize.width - bounds.width)
        scrollOffset = max(0, min(scrollOffset - delta, maxOffset))
        stackLeading.constant = -scrollOffset
        layoutSubtreeIfNeeded()
    }

    private func showContextMenu(for tabID: DocumentTabID, with event: NSEvent, in cell: NSView) {
        guard let index = tabStore.tabs.firstIndex(where: { $0.tabID == tabID }) else { return }
        let menu = NSMenu()
        menu.addItem(contextMenuItem(L10n.t("关闭标签"), action: .close, tabID: tabID))
        let closeOthers = contextMenuItem(L10n.t("关闭其他标签"), action: .closeOthers, tabID: tabID)
        closeOthers.isEnabled = tabStore.tabs.count > 1
        menu.addItem(closeOthers)
        let closeToRight = contextMenuItem(L10n.t("关闭右侧标签"), action: .closeToRight, tabID: tabID)
        closeToRight.isEnabled = index < tabStore.tabs.count - 1
        menu.addItem(closeToRight)
        NSMenu.popUpContextMenu(menu, with: event, for: cell)
    }

    private func contextMenuItem(_ title: String, action: TabContextAction, tabID: DocumentTabID) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(tabContextAction(_:)), keyEquivalent: "")
        item.target = self
        item.representedObject = (action, tabID.rawValue)
        return item
    }

    @objc private func tabContextAction(_ sender: NSMenuItem) {
        guard let value = sender.representedObject as? (TabContextAction, String) else { return }
        onContextAction?(value.0, DocumentTabID(value.1))
    }
}

/// 单个标签单元：标题 + 脏圆点 + 关闭按钮。
final class TabCellView: NSView {
    var onActivate: (() -> Void)?
    var onClose: (() -> Void)?
    var onContextMenu: ((NSEvent) -> Void)?
    weak var controller: TabBarController?
    var tabID: DocumentTabID?

    private let titleLabel = NSTextField(labelWithString: "")
    private let dirtyDot = NSView()
    private let closeButton = NSButton()
    private var isActive = false
    private var downPoint: NSPoint?
    private var didDrag = false

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.cornerRadius = 6

        titleLabel.lineBreakMode = .byTruncatingTail
        titleLabel.font = .systemFont(ofSize: 12)
        titleLabel.translatesAutoresizingMaskIntoConstraints = false
        titleLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        dirtyDot.wantsLayer = true
        dirtyDot.layer?.cornerRadius = 3
        dirtyDot.layer?.backgroundColor = NSColor.secondaryLabelColor.cgColor
        dirtyDot.translatesAutoresizingMaskIntoConstraints = false
        dirtyDot.isHidden = true

        closeButton.image = NSImage(systemSymbolName: "xmark", accessibilityDescription: nil)
        closeButton.bezelStyle = .inline
        closeButton.isBordered = false
        closeButton.imagePosition = .imageOnly
        closeButton.controlSize = .mini
        closeButton.target = self
        closeButton.action = #selector(closeClicked)
        closeButton.translatesAutoresizingMaskIntoConstraints = false
        closeButton.refusesFirstResponder = true

        addSubview(titleLabel)
        addSubview(dirtyDot)
        addSubview(closeButton)
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: 24),
            titleLabel.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 10),
            titleLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            titleLabel.trailingAnchor.constraint(lessThanOrEqualTo: dirtyDot.leadingAnchor, constant: -6),

            dirtyDot.widthAnchor.constraint(equalToConstant: 6),
            dirtyDot.heightAnchor.constraint(equalToConstant: 6),
            dirtyDot.centerYAnchor.constraint(equalTo: centerYAnchor),
            dirtyDot.trailingAnchor.constraint(equalTo: closeButton.leadingAnchor, constant: -6),

            closeButton.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -6),
            closeButton.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    @objc private func closeClicked() { onClose?() }

    override func mouseDown(with event: NSEvent) {
        downPoint = convert(event.locationInWindow, from: nil)
        didDrag = false
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = downPoint else { return }
        let point = convert(event.locationInWindow, from: nil)
        if !didDrag, hypot(point.x - start.x, point.y - start.y) > 6 {
            didDrag = true
            controller?.beginReorder(from: self)
        }
        if didDrag {
            controller?.dragReorder(to: event.locationInWindow)
        }
    }

    override func mouseUp(with event: NSEvent) {
        if didDrag {
            controller?.endReorder(at: event.locationInWindow)
        } else {
            onActivate?()
        }
        downPoint = nil
        didDrag = false
    }

    override func rightMouseDown(with event: NSEvent) {
        onContextMenu?(event)
    }

    override func otherMouseDown(with event: NSEvent) {
        if event.buttonNumber == 2 {
            onClose?()
        } else {
            super.otherMouseDown(with: event)
        }
    }

    func configure(
        title: String,
        isActive: Bool,
        isDirty: Bool,
        isSuspended: Bool,
        recoveryUnavailable: Bool,
        toolTip: String,
        animationDuration: TimeInterval,
        accessibilityTitle: String
    ) {
        self.isActive = isActive
        titleLabel.stringValue = title
        self.toolTip = toolTip
        dirtyDot.isHidden = !isDirty
        setAccessibilityLabel(
            accessibilityTitle
            + (isDirty ? "，" + L10n.t("已修改") : "")
            + (isSuspended ? "，" + L10n.t("已暂停") : "")
            + (recoveryUnavailable ? "，" + L10n.t("恢复保护不可用") : "")
        )
        closeButton.setAccessibilityLabel(L10n.f("关闭 %@", title))

        let applyColors = { [weak self] in
            guard let self else { return }
            self.layer?.backgroundColor = (isActive
                ? NSColor.controlBackgroundColor
                : NSColor.clear).cgColor
            self.titleLabel.textColor = isActive ? .labelColor : .secondaryLabelColor
        }
        guard animationDuration > 0 else { applyColors(); return }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = animationDuration
            context.allowsImplicitAnimation = true
            applyColors()
        }
    }
}
