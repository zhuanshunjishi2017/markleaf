import AppKit
import QuartzCore

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
    private var motionGeneration = 0
    private var draggingCell: TabCellView?
    private var dragPlaceholder: NSView?
    private var dragSourceIndex: Int?
    private var dragStartOffset = NSPoint.zero

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
        guard !isReordering else { return }
        motionGeneration += 1
        let generation = motionGeneration
        let existing = Set(cellsByTab.keys)
        let current = Set(tabStore.tabs.map(\.tabID))
        let removedCells = existing.subtracting(current).compactMap { id -> TabCellView? in
            guard let cell = cellsByTab.removeValue(forKey: id) else { return nil }
            cell.layer?.removeAllAnimations()
            cell.setLifted(false, animated: false)
            return cell
        }
        let currentOrder = stack.arrangedSubviews.compactMap { ($0 as? TabCellView)?.tabID }
        let desiredOrder = tabStore.tabs.map(\.tabID)
        let orderChanged = currentOrder != desiredOrder
        var insertedCells: [TabCellView] = []
        for tab in tabStore.tabs {
            let cell: TabCellView
            if let existingCell = cellsByTab[tab.tabID] {
                cell = existingCell
            } else {
                cell = makeCell(for: tab)
                cell.prepareForInsertion()
                insertedCells.append(cell)
            }
            cellsByTab[tab.tabID] = cell
            configure(cell, for: tab)
        }
        rebuildOverflowMenu()
        updateOverflowVisibility()

        let needsMotion = !insertedCells.isEmpty || !removedCells.isEmpty || orderChanged
        guard needsMotion else { return }
        // Establish the pre-change frames so Auto Layout can interpolate the
        // stack's expansion, collapse, or reorder inside the animation group.
        layoutSubtreeIfNeeded()
        animateTabChanges(
            inserted: insertedCells,
            removed: removedCells,
            generation: generation
        )
    }

    private func animateTabChanges(
        inserted: [TabCellView],
        removed: [TabCellView],
        generation: Int
    ) {
        let duration = TabAnimationPolicy.duration(for: .insertRemoveReorder, reduceMotion: reduceMotion)
        removed.forEach { stack.removeArrangedSubview($0) }
        for (index, tab) in tabStore.tabs.enumerated() {
            guard let cell = cellsByTab[tab.tabID] else { continue }
            if stack.arrangedSubviews.count <= index || stack.arrangedSubviews[index] !== cell {
                stack.insertArrangedSubview(cell, at: min(index, stack.arrangedSubviews.count))
            }
        }
        guard duration > 0 else {
            inserted.forEach { $0.finishInsertion() }
            removed.forEach { $0.removeFromSuperview() }
            layoutSubtreeIfNeeded()
            return
        }
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = duration
            context.allowsImplicitAnimation = true
            self.layoutSubtreeIfNeeded()
            inserted.forEach { $0.animator().alphaValue = 1 }
            removed.forEach { $0.animator().alphaValue = 0 }
        }, completionHandler: { [weak self] in
            guard let self else { return }
            removed.forEach { $0.removeFromSuperview() }
            inserted.forEach { $0.finishInsertion() }
            guard self.motionGeneration == generation else { return }
            self.layoutSubtreeIfNeeded()
        })
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
        let duration = tab.recoveryUnavailable
            ? TabAnimationPolicy.duration(for: .statusMark, reduceMotion: reduceMotion)
            : TabAnimationPolicy.duration(for: .activeState, reduceMotion: reduceMotion)
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

    func beginReorder(from cell: TabCellView, at windowPoint: NSPoint) {
        guard isReordering == false else { return }
        guard let id = cell.tabID,
              let source = tabStore.tabs.firstIndex(where: { $0.tabID == id }) else { return }
        let initialFrame = cell.convert(cell.bounds, to: self)
        let localPoint = convert(windowPoint, from: nil)
        let placeholder = NSView(frame: .zero)
        placeholder.translatesAutoresizingMaskIntoConstraints = false
        placeholder.wantsLayer = true

        isReordering = true
        reorderingTabID = id
        draggingCell = cell
        dragPlaceholder = placeholder
        dragSourceIndex = source
        dragStartOffset = NSPoint(
            x: localPoint.x - initialFrame.minX,
            y: localPoint.y - initialFrame.minY
        )

        stack.removeArrangedSubview(cell)
        cell.removeFromSuperview()
        stack.insertArrangedSubview(placeholder, at: source)
        addSubview(cell)
        cell.translatesAutoresizingMaskIntoConstraints = true
        cell.frame = initialFrame
        cell.setFrameOrigin(initialFrame.origin)
        cell.needsLayout = true
        cell.setLifted(true, animated: !reduceMotion)
    }

    func dragReorder(to windowPoint: NSPoint) {
        guard isReordering, let cell = draggingCell, let placeholder = dragPlaceholder else { return }
        let local = convert(windowPoint, from: nil)
        cell.setFrameOrigin(NSPoint(
            x: local.x - dragStartOffset.x,
            y: local.y - dragStartOffset.y
        ))
        let target = targetIndex(for: local)
        let current = stack.arrangedSubviews.firstIndex(of: placeholder) ?? 0
        guard target != current else { return }
        let duration = TabAnimationPolicy.duration(for: .insertRemoveReorder, reduceMotion: reduceMotion)
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = duration
            context.allowsImplicitAnimation = duration > 0
            stack.removeArrangedSubview(placeholder)
            stack.insertArrangedSubview(placeholder, at: min(target, stack.arrangedSubviews.count))
            stack.layoutSubtreeIfNeeded()
        })
    }

    func endReorder(at windowPoint: NSPoint) {
        guard isReordering, let id = reorderingTabID,
              let cell = draggingCell, let placeholder = dragPlaceholder else {
            isReordering = false
            reorderingTabID = nil
            return
        }
        let source = dragSourceIndex ?? tabStore.tabs.firstIndex(where: { $0.tabID == id })
        let target = targetIndex(for: convert(windowPoint, from: nil))
        let placeholderIndex = stack.arrangedSubviews.firstIndex(of: placeholder) ?? target
        stack.removeArrangedSubview(placeholder)
        placeholder.removeFromSuperview()
        cell.removeFromSuperview()
        cell.translatesAutoresizingMaskIntoConstraints = false
        stack.insertArrangedSubview(cell, at: min(placeholderIndex, stack.arrangedSubviews.count))
        cell.setLifted(false, animated: !reduceMotion)
        isReordering = false
        reorderingTabID = nil
        draggingCell = nil
        dragPlaceholder = nil
        dragSourceIndex = nil
        if let source, source != target {
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
        layer?.shadowColor = NSColor.black.cgColor

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

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool {
        true
    }

    func prepareForInsertion() {
        alphaValue = 0
        layer?.transform = CATransform3DMakeScale(0.96, 0.96, 1)
    }

    func finishInsertion() {
        alphaValue = 1
        layer?.transform = CATransform3DIdentity
    }

    func setLifted(_ lifted: Bool, animated: Bool) {
        guard let layer else { return }
        let duration = TabAnimationPolicy.duration(for: .insertRemoveReorder, reduceMotion: !animated)
        let fromTransform = layer.presentation()?.transform ?? layer.transform
        let targetTransform = lifted
            ? CATransform3DMakeScale(1.04, 1.08, 1)
            : CATransform3DIdentity
        let targetShadowOpacity: Float = lifted ? 0.24 : 0
        let targetShadowRadius: CGFloat = lifted ? 10 : 0
        let targetShadowOffset = lifted ? CGSize(width: 0, height: -2) : .zero

        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer.transform = targetTransform
        layer.shadowOpacity = targetShadowOpacity
        layer.shadowRadius = targetShadowRadius
        layer.shadowOffset = targetShadowOffset
        layer.zPosition = lifted ? 10 : 0
        CATransaction.commit()

        guard duration > 0 else { return }
        let transformAnimation = CABasicAnimation(keyPath: "transform")
        transformAnimation.fromValue = NSValue(caTransform3D: fromTransform)
        transformAnimation.toValue = NSValue(caTransform3D: targetTransform)
        transformAnimation.duration = duration
        transformAnimation.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        layer.add(transformAnimation, forKey: "tabLift")

        let shadowOpacityAnimation = CABasicAnimation(keyPath: "shadowOpacity")
        shadowOpacityAnimation.fromValue = layer.presentation()?.shadowOpacity ?? layer.shadowOpacity
        shadowOpacityAnimation.toValue = targetShadowOpacity
        shadowOpacityAnimation.duration = duration
        layer.add(shadowOpacityAnimation, forKey: "tabLiftShadow")
    }

    override func mouseDown(with event: NSEvent) {
        downPoint = convert(event.locationInWindow, from: nil)
        didDrag = false
        controller?.beginReorder(from: self, at: event.locationInWindow)
    }

    override func mouseDragged(with event: NSEvent) {
        guard let start = downPoint else { return }
        let point = convert(event.locationInWindow, from: nil)
        if hypot(point.x - start.x, point.y - start.y) > 6 {
            didDrag = true
        }
        controller?.dragReorder(to: event.locationInWindow)
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
        titleLabel.stringValue = recoveryUnavailable ? "\(title) ⚠︎" : title
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
            self.titleLabel.textColor = recoveryUnavailable
                ? .systemOrange
                : (isActive ? .labelColor : .secondaryLabelColor)
        }
        guard animationDuration > 0 else { applyColors(); return }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = animationDuration
            context.allowsImplicitAnimation = true
            applyColors()
        }
    }
}
