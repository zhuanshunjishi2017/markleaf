import AppKit

final class TableSizePickerView: NSView {
    private let cellSize: CGFloat = 24
    private let cellSpacing: CGFloat = 4
    private let contentPadding: CGFloat = 12
    private let titleHeight: CGFloat = 30
    private let customButtonHeight: CGFloat = 28

    let visibleLimit: Int
    private(set) var selectedSize: TableSize
    var onSelect: ((TableSize) -> Void)?
    var onCancel: (() -> Void)?

    private let titleField = NSTextField(labelWithString: "")
    private let customButton = NSButton(title: L10n.t("自定义表格…"), target: nil, action: nil)

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { true }

    convenience init() {
        self.init(initialSize: TableSizePickerModel.defaultSize, visibleLimit: TableSizePickerModel.visibleLimit)
    }

    init(initialSize: TableSize = TableSizePickerModel.defaultSize, visibleLimit: Int = TableSizePickerModel.visibleLimit) {
        self.visibleLimit = max(1, visibleLimit)
        self.selectedSize = TableSizePickerModel.clamped(rows: initialSize.rows, columns: initialSize.columns)
        let gridWidth = CGFloat(self.visibleLimit) * 24 + CGFloat(max(0, self.visibleLimit - 1)) * 4
        let width = 2 * 12 + gridWidth
        let height = 12 + 30 + CGFloat(self.visibleLimit) * 24 + CGFloat(max(0, self.visibleLimit - 1)) * 4 + 12 + 28 + 12
        super.init(frame: NSRect(x: 0, y: 0, width: width, height: height))

        titleField.font = .systemFont(ofSize: 15, weight: .semibold)
        titleField.textColor = .labelColor
        titleField.alignment = .left
        titleField.translatesAutoresizingMaskIntoConstraints = false
        addSubview(titleField)

        customButton.bezelStyle = .rounded
        customButton.font = .systemFont(ofSize: 13)
        customButton.target = self
        customButton.action = #selector(showCustomTableDialog)
        customButton.translatesAutoresizingMaskIntoConstraints = false
        addSubview(customButton)

        NSLayoutConstraint.activate([
            titleField.leadingAnchor.constraint(equalTo: leadingAnchor, constant: contentPadding),
            titleField.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -contentPadding),
            titleField.topAnchor.constraint(equalTo: topAnchor, constant: 4),
            titleField.heightAnchor.constraint(equalToConstant: titleHeight),
            customButton.leadingAnchor.constraint(equalTo: leadingAnchor, constant: contentPadding),
            customButton.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -contentPadding),
            customButton.bottomAnchor.constraint(equalTo: bottomAnchor, constant: -contentPadding),
            customButton.heightAnchor.constraint(equalToConstant: customButtonHeight),
        ])
        updateTitle()
        addTrackingArea(NSTrackingArea(
            rect: .zero,
            options: [.mouseMoved, .activeInKeyWindow, .inVisibleRect],
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

        let gridOrigin = NSPoint(x: contentPadding, y: 12 + titleHeight)
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

    func commitSelection() {
        onSelect?(selectedSize)
    }

    func cancelSelection() {
        onCancel?()
    }

    override func mouseMoved(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        guard let cell = cell(at: point) else { return }
        updateSelection(row: cell.row, column: cell.column)
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
        let origin = NSPoint(x: contentPadding, y: 12 + titleHeight)
        for row in 1...visibleLimit {
            for column in 1...visibleLimit {
                if cellRect(row: row, column: column, origin: origin).contains(point) {
                    return (row, column)
                }
            }
        }
        return nil
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
        titleField.stringValue = L10n.f("%@×%@ 表格", "\(selectedSize.rows)", "\(selectedSize.columns)")
    }

    @objc private func showCustomTableDialog() {
        let rowsField = NSTextField(string: "\(selectedSize.rows)")
        let columnsField = NSTextField(string: "\(selectedSize.columns)")
        let rowsLabel = NSTextField(labelWithString: L10n.t("行数"))
        let columnsLabel = NSTextField(labelWithString: L10n.t("列数"))
        let rows = NSStackView(views: [rowsLabel, rowsField])
        let columns = NSStackView(views: [columnsLabel, columnsField])
        rows.orientation = .horizontal
        columns.orientation = .horizontal
        rows.spacing = 8
        columns.spacing = 8
        let fields = NSStackView(views: [rows, columns])
        fields.orientation = .vertical
        fields.spacing = 8
        fields.edgeInsets = NSEdgeInsets(top: 4, left: 0, bottom: 4, right: 0)
        rowsField.widthAnchor.constraint(equalToConstant: 80).isActive = true
        columnsField.widthAnchor.constraint(equalToConstant: 80).isActive = true

        let alert = NSAlert()
        alert.messageText = L10n.t("自定义表格")
        alert.accessoryView = fields
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        guard let size = TableSizePickerModel.parse("\(rowsField.stringValue),\(columnsField.stringValue)") else {
            NSSound.beep()
            return
        }
        selectedSize = size
        updateTitle()
        needsDisplay = true
        commitSelection()
    }
}
