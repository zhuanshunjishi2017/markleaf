import AppKit

/// 右侧编辑区顶部的标签栏：文件名、脏状态圆点、关闭按钮。
/// 单击激活；关闭按钮关闭；完整路径走悬停提示与上下文菜单（Task 13 补溢出与拖拽）。
final class TabBarController: NSView {
    var onActivate: ((DocumentTabID) -> Void)?
    var onClose: ((DocumentTabID) -> Void)?

    private let stack = NSStackView()
    private unowned let tabStore: TabStore
    private var cellsByTab: [DocumentTabID: TabCellView] = [:]

    init(tabStore: TabStore) {
        self.tabStore = tabStore
        super.init(frame: .zero)
        wantsLayer = true
        translatesAutoresizingMaskIntoConstraints = false
        heightAnchor.constraint(equalToConstant: 32).isActive = true

        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.distribution = .fill
        stack.spacing = 2
        stack.edgeInsets = NSEdgeInsets(top: 4, left: 8, bottom: 4, right: 8)
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.topAnchor.constraint(equalTo: topAnchor),
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    private var reduceMotion: Bool {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    }

    func reload() {
        let existing = Set(cellsByTab.keys)
        let current = Set(tabStore.tabs.map(\.tabID))
        for id in existing.subtracting(current) {
            if let cell = cellsByTab.removeValue(forKey: id) {
                stack.removeArrangedSubview(cell)
                cell.removeFromSuperview()
            }
        }
        for (index, tab) in tabStore.tabs.enumerated() {
            let cell = cellsByTab[tab.tabID] ?? makeCell(for: tab)
            cellsByTab[tab.tabID] = cell
            if stack.arrangedSubviews.count <= index || stack.arrangedSubviews[index] !== cell {
                stack.insertArrangedSubview(cell, at: index)
            }
            configure(cell, for: tab)
        }
    }

    private func makeCell(for tab: DocumentTab) -> TabCellView {
        let cell = TabCellView()
        cell.onActivate = { [weak self] in self?.onActivate?(tab.tabID) }
        cell.onClose = { [weak self] in self?.onClose?(tab.tabID) }
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
            toolTip: tab.path ?? tab.title,
            animationDuration: duration,
            accessibilityTitle: tab.path ?? tab.title
        )
    }
}

/// 单个标签单元：标题 + 脏圆点 + 关闭按钮。
final class TabCellView: NSView {
    var onActivate: (() -> Void)?
    var onClose: (() -> Void)?

    private let titleLabel = NSTextField(labelWithString: "")
    private let dirtyDot = NSView()
    private let closeButton = NSButton()
    private var isActive = false

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
            widthAnchor.constraint(greaterThanOrEqualToConstant: 90),
            widthAnchor.constraint(lessThanOrEqualToConstant: 200),

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
        onActivate?()
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
