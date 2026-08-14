import AppKit

final class TableSizePickerView: NSView, NSMenuDelegate {
    let cellSize: CGFloat = 18
    let cellSpacing: CGFloat = 3
    let contentPadding: CGFloat = 10
    let titleHeight: CGFloat = 22
    let titleTopPadding: CGFloat = 4
    let titleToGridSpacing: CGFloat = 4
    private let gridToButtonSpacing: CGFloat = 12
    private let customButtonHeight: CGFloat = 24

    let visibleLimit: Int
    private(set) var selectedSize: TableSize
    var onSelect: ((TableSize) -> Void)?
    var onCancel: (() -> Void)?

    private let titleField = NSTextField(labelWithString: "")
    private let customButton = NSButton(title: L10n.t("自定义表格…"), target: nil, action: nil)
    /// 保持自定义表格 sheet 的 NSAlert 存活，避免弹窗闪一下即被关闭。
    private var customTableAlert: NSAlert?
    /// 等待菜单真正关闭后再弹出的自定义表格对话框状态。
    private weak var pendingCustomTableMenu: NSMenu?
    private var pendingCustomTableParent: NSWindow?
    private var pendingCustomTableRequested = false

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    convenience init() {
        self.init(initialSize: TableSize(rows: 0, columns: 0), visibleLimit: TableSizePickerModel.visibleLimit)
    }

    init(initialSize: TableSize = TableSize(rows: 0, columns: 0), visibleLimit: Int = TableSizePickerModel.visibleLimit) {
        self.visibleLimit = max(1, visibleLimit)
        self.selectedSize = TableSize(
            rows: max(0, initialSize.rows),
            columns: max(0, initialSize.columns)
        )
        let gridExtent = CGFloat(self.visibleLimit) * cellSize + CGFloat(max(0, self.visibleLimit - 1)) * cellSpacing
        let width = 2 * contentPadding + gridExtent
        let gridTop = titleTopPadding + titleHeight + titleToGridSpacing
        let height = gridTop + gridExtent + gridToButtonSpacing + customButtonHeight + contentPadding
        super.init(frame: NSRect(x: 0, y: 0, width: width, height: height))

        titleField.font = .systemFont(ofSize: 12, weight: .semibold)
        titleField.textColor = .labelColor
        titleField.alignment = .left
        titleField.translatesAutoresizingMaskIntoConstraints = false
        addSubview(titleField)

        customButton.bezelStyle = .rounded
        customButton.font = .systemFont(ofSize: 12)
        customButton.target = self
        customButton.action = #selector(showCustomTableDialog)
        customButton.translatesAutoresizingMaskIntoConstraints = false
        addSubview(customButton)

        NSLayoutConstraint.activate([
            titleField.leadingAnchor.constraint(equalTo: leadingAnchor, constant: contentPadding),
            titleField.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -contentPadding),
            titleField.topAnchor.constraint(equalTo: topAnchor, constant: titleTopPadding),
            titleField.heightAnchor.constraint(equalToConstant: titleHeight),
            customButton.leadingAnchor.constraint(equalTo: leadingAnchor, constant: contentPadding),
            customButton.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -contentPadding),
            customButton.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -contentPadding),
            customButton.heightAnchor.constraint(equalToConstant: customButtonHeight),
        ])
        updateTitle()
        addTrackingArea(NSTrackingArea(
            rect: .zero,
            options: [.mouseMoved, .mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
            owner: self,
            userInfo: nil
        ))
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        NSColor.controlBackgroundColor.setFill()
        dirtyRect.fill()

        let gridOrigin = NSPoint(x: contentPadding, y: titleTopPadding + titleHeight + titleToGridSpacing)
        for row in 1...visibleLimit {
            for column in 1...visibleLimit {
                let rect = cellRect(row: row, column: column, origin: gridOrigin)
                let isSelected = row <= selectedSize.rows && column <= selectedSize.columns
                (isSelected ? NSColor.controlAccentColor.withAlphaComponent(0.18) : NSColor.clear).setFill()
                rect.fill()
                (isSelected ? NSColor.controlAccentColor : NSColor.separatorColor).setStroke()
                let path = NSBezierPath(rect: rect.insetBy(dx: 0.5, dy: 0.5))
                path.lineWidth = isSelected ? 1.5 : 1
                path.stroke()
            }
        }
    }

    func updateSelection(row: Int, column: Int) {
        selectedSize = TableSizePickerModel.clamped(rows: row, columns: column)
        updateTitle()
        needsDisplay = true
    }

    /// 鼠标离开网格选择区域时立即回到空白（0×0），不保留悬停选择。
    func resetSelection() {
        selectedSize = TableSize(rows: 0, columns: 0)
        updateTitle()
        needsDisplay = true
    }

    func commitSelection() {
        onSelect?(selectedSize)
    }

    func cancelSelection() {
        onCancel?()
    }

    /// 悬停处理：仅当指针离开整个网格区域时重置为 0×0；
    /// 停在格子之间的间隔里保持当前选择，避免闪烁回空白。
    func handleHover(at point: NSPoint) {
        guard gridRect().contains(point) else {
            resetSelection()
            return
        }
        if let cell = cell(at: point) {
            updateSelection(row: cell.row, column: cell.column)
        }
    }

    override func mouseMoved(with event: NSEvent) {
        handleHover(at: convert(event.locationInWindow, from: nil))
    }

    override func mouseExited(with event: NSEvent) {
        resetSelection()
    }

    override func mouseDown(with event: NSEvent) {
        window?.makeFirstResponder(self)
        let point = convert(event.locationInWindow, from: nil)
        if let cell = cell(at: point) {
            updateSelection(row: cell.row, column: cell.column)
            commitSelection()
        }
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 {
            cancelSelection()
        } else {
            super.keyDown(with: event)
        }
    }

    private func cell(at point: NSPoint) -> (row: Int, column: Int)? {
        for row in 1...visibleLimit {
            for column in 1...visibleLimit {
                if cellRect(row: row, column: column, origin: gridRect().origin).contains(point) {
                    return (row, column)
                }
            }
        }
        return nil
    }

    private func gridRect() -> NSRect {
        let extent = CGFloat(visibleLimit) * cellSize + CGFloat(max(0, visibleLimit - 1)) * cellSpacing
        return NSRect(
            x: contentPadding,
            y: titleTopPadding + titleHeight + titleToGridSpacing,
            width: extent,
            height: extent
        )
    }

    private func cellRect(row: Int, column: Int, origin: NSPoint) -> NSRect {
        NSRect(
            x: origin.x + CGFloat(column - 1) * (cellSize + cellSpacing),
            y: origin.y + CGFloat(row - 1) * (cellSize + cellSpacing),
            width: cellSize,
            height: cellSize
        )
    }

    private func updateTitle() {
        if selectedSize.rows == 0 || selectedSize.columns == 0 {
            titleField.stringValue = L10n.t("插入表格")
        } else {
            titleField.stringValue = L10n.f("%@×%@ 表格", "\(selectedSize.rows)", "\(selectedSize.columns)")
        }
    }

    @objc private func showCustomTableDialog() {
        // 菜单跟踪期间不能弹窗（runModal 会被菜单事件循环阻塞），
        // 先记住文档窗口并关闭菜单；等菜单真正关闭（menuDidClose）后再以 sheet 形式弹出，
        // 避免弹窗打断菜单关闭动画，导致菜单“闪一下并缓慢关闭”。
        let parentWindow = NSApp.mainWindow ?? NSApp.keyWindow
        if let menu = enclosingMenuItem?.menu {
            pendingCustomTableMenu = menu
            pendingCustomTableParent = parentWindow
            pendingCustomTableRequested = true
            menu.delegate = self
            cancelSelection()
        } else {
            presentCustomTableDialog(parentWindow: parentWindow)
        }
    }

    func menuDidClose(_ menu: NSMenu) {
        guard pendingCustomTableRequested, menu === pendingCustomTableMenu else { return }
        pendingCustomTableRequested = false
        pendingCustomTableMenu = nil
        let parentWindow = pendingCustomTableParent
        pendingCustomTableParent = nil
        // 菜单已关闭，仅留一帧余量再弹 sheet，避免与关闭中的菜单窗口
        // 竞争焦点/层级，同时保持点击到弹窗的响应速度。
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.12) { [weak self] in
            guard let self else { return }
            if !NSApp.isActive {
                NSApp.activate(ignoringOtherApps: true)
            }
            self.presentCustomTableDialog(parentWindow: parentWindow)
        }
    }

    private func presentCustomTableDialog(parentWindow: NSWindow?) {
        let rowsField = NSTextField(string: "\(selectedSize.rows > 0 ? selectedSize.rows : TableSizePickerModel.defaultSize.rows)")
        let columnsField = NSTextField(string: "\(selectedSize.columns > 0 ? selectedSize.columns : TableSizePickerModel.defaultSize.columns)")
        let rowsLabel = NSTextField(labelWithString: L10n.t("行数"))
        let columnsLabel = NSTextField(labelWithString: L10n.t("列数"))
        // 只允许输入 1...maxCustomSize 的正整数；允许清空，非法输入直接拒绝。
        rowsField.formatter = BoundedIntegerFormatter(min: 1, max: TableSizePickerModel.maxCustomSize)
        columnsField.formatter = BoundedIntegerFormatter(min: 1, max: TableSizePickerModel.maxCustomSize)

        // NSAlert 的 accessoryView 保留显式尺寸；内部用文字基线约束，避免标签与输入内容视觉错位。
        rowsLabel.alignment = .right
        columnsLabel.alignment = .right
        let accessory = NSView(frame: NSRect(x: 0, y: 0, width: 260, height: 32))
        for subview in [rowsLabel, rowsField, columnsLabel, columnsField] {
            subview.translatesAutoresizingMaskIntoConstraints = false
            accessory.addSubview(subview)
        }
        NSLayoutConstraint.activate([
            rowsLabel.leadingAnchor.constraint(equalTo: accessory.leadingAnchor),
            rowsLabel.widthAnchor.constraint(equalToConstant: 40),
            rowsLabel.firstBaselineAnchor.constraint(equalTo: rowsField.firstBaselineAnchor),
            rowsField.leadingAnchor.constraint(equalTo: rowsLabel.trailingAnchor, constant: 4),
            rowsField.centerYAnchor.constraint(equalTo: accessory.centerYAnchor),
            rowsField.widthAnchor.constraint(equalToConstant: 80),
            rowsField.heightAnchor.constraint(equalToConstant: 24),
            columnsLabel.leadingAnchor.constraint(equalTo: rowsField.trailingAnchor, constant: 12),
            columnsLabel.widthAnchor.constraint(equalToConstant: 40),
            columnsLabel.firstBaselineAnchor.constraint(equalTo: columnsField.firstBaselineAnchor),
            columnsField.leadingAnchor.constraint(equalTo: columnsLabel.trailingAnchor, constant: 4),
            columnsField.centerYAnchor.constraint(equalTo: accessory.centerYAnchor),
            columnsField.widthAnchor.constraint(equalToConstant: 80),
            columnsField.heightAnchor.constraint(equalToConstant: 24),
            columnsField.trailingAnchor.constraint(equalTo: accessory.trailingAnchor),
        ])
        accessory.layoutSubtreeIfNeeded()

        let alert = NSAlert()
        alert.messageText = L10n.t("自定义表格")
        alert.accessoryView = accessory
        alert.window.initialFirstResponder = rowsField
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))
        let okButton = alert.buttons.first
        func refreshOKButton() {
            okButton?.isEnabled = TableSizePickerModel.parse("\(rowsField.stringValue),\(columnsField.stringValue)") != nil
        }
        refreshOKButton()
        let rowsToken = NotificationCenter.default.addObserver(
            forName: NSControl.textDidChangeNotification,
            object: rowsField,
            queue: .main
        ) { _ in refreshOKButton() }
        let columnsToken = NotificationCenter.default.addObserver(
            forName: NSControl.textDidChangeNotification,
            object: columnsField,
            queue: .main
        ) { _ in refreshOKButton() }
        let handleResponse: (NSApplication.ModalResponse) -> Void = { [weak self] response in
            NotificationCenter.default.removeObserver(rowsToken)
            NotificationCenter.default.removeObserver(columnsToken)
            guard response == .alertFirstButtonReturn, let self else { return }
            guard let size = TableSizePickerModel.parse("\(rowsField.stringValue),\(columnsField.stringValue)") else {
                NSSound.beep()
                return
            }
            self.selectedSize = size
            self.updateTitle()
            self.needsDisplay = true
            self.commitSelection()
        }
        customTableAlert = alert
        if let parentWindow, parentWindow.isVisible {
            alert.beginSheetModal(for: parentWindow) { [weak self] response in
                self?.customTableAlert = nil
                handleResponse(response)
            }
        } else {
            customTableAlert = nil
            handleResponse(alert.runModal())
        }
    }
}

func tableSizePickerMenuItem(onSelect: @escaping (TableSize) -> Void) -> NSMenuItem {
    let menuItem = NSMenuItem()
    let picker = TableSizePickerView()
    picker.onSelect = { [weak menuItem] size in
        onSelect(size)
        menuItem?.menu?.cancelTracking()
    }
    picker.onCancel = { [weak menuItem] in
        menuItem?.menu?.cancelTracking()
    }
    menuItem.view = picker
    menuItem.toolTip = L10n.t("选择表格大小")
    return menuItem
}

/// “插入表格”二级菜单：展开后才显示尺寸网格，避免右键菜单里直接铺开可视化选择器。
func tableSizePickerSubmenu(onSelect: @escaping (TableSize) -> Void) -> NSMenuItem {
    let menuItem = NSMenuItem(title: L10n.t("插入表格"), action: nil, keyEquivalent: "")
    let submenu = NSMenu(title: L10n.t("插入表格"))
    submenu.addItem(tableSizePickerMenuItem(onSelect: onSelect))
    menuItem.submenu = submenu
    return menuItem
}
