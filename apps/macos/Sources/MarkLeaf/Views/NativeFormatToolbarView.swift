import AppKit

/// AppKit points, independent of editor zoom and display backing scale.
private enum NativeFormatToolbarMetrics {
    static let buttonSize: CGFloat = 24
    static let iconSize: CGFloat = 16
    static let cornerRadius: CGFloat = 5
    static let buttonSpacing: CGFloat = 2
    static let groupSpacing: CGFloat = 8
    // Match the inset of NSMenu's native separator and ordinary menu items.
    static let horizontalInset: CGFloat = 16
    static let verticalInset: CGFloat = 4
    static let rowHeight: CGFloat = buttonSize + 8
    static let separatorWidth: CGFloat = 1
    static let separatorHeight: CGFloat = buttonSize

    /// Keep SF Symbols vector-backed, but place them on a shared template canvas.
    /// NSButtonCell otherwise uses each symbol's text baseline/alignment metadata
    /// (and can resize it to the button font), rather than centering its artwork.
    static func icon(_ symbol: NSImage) -> NSImage {
        let size = NSSize(width: iconSize, height: iconSize)
        let scale = min(1, iconSize / max(symbol.size.width, symbol.size.height, 1))
        let glyphSize = NSSize(width: symbol.size.width * scale, height: symbol.size.height * scale)
        let icon = NSImage(size: size, flipped: false) { rect in
            let glyphRect = NSRect(x: rect.midX - glyphSize.width / 2,
                                   y: rect.midY - glyphSize.height / 2,
                                   width: glyphSize.width, height: glyphSize.height)
            symbol.draw(in: glyphRect, from: .zero, operation: .sourceOver,
                        fraction: 1, respectFlipped: true, hints: nil)
            return true
        }
        icon.isTemplate = true
        icon.accessibilityDescription = symbol.accessibilityDescription
        return icon
    }
}

/// NSMenuItem 的原生快捷行。背景由 NSMenu 提供，避免再次叠加玻璃材质。
final class NativeFormatToolbarView: NSView {
    var onPreview: ((String?) -> Void)?
    var onCommand: ((String) -> Void)?
    private var hoveredCommand: String?

    init(actions: [String: EditorActionState], canStartFormatPainter: Bool?, formatPainterArmed: Bool?) {
        super.init(frame: .zero)
        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.alignment = .centerY
        stack.spacing = NativeFormatToolbarMetrics.buttonSpacing
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)

        // SF Symbols 里文字字形符号（B/I/U/S）只占字号约七成高，物件符号
        // （高亮笔/刷/橡皮）几乎撑满符号框；同一点号会让文字类明显偏小。
        // 文字类用更大点号、物件类用更小点号，使两类的实际视觉高度一致。
        let textSymbolConfiguration = NSImage.SymbolConfiguration(pointSize: 14, weight: .medium)
        let objectSymbolConfiguration = NSImage.SymbolConfiguration(pointSize: 12, weight: .medium)

        func add(_ command: String, title: String, symbol: String?, textSymbol: Bool = false, preview: Bool = false, enabled: Bool? = nil, checked: Bool? = nil) {
            let button = NativeFormatToolbarButton(title: title, target: self, action: #selector(activate(_:)))
            button.identifier = NSUserInterfaceItemIdentifier(command)
            button.toolTip = title
            button.setAccessibilityLabel(title)
            if let symbol, let image = NSImage(systemSymbolName: symbol, accessibilityDescription: title) {
                let configuration = textSymbol ? textSymbolConfiguration : objectSymbolConfiguration
                button.image = NativeFormatToolbarMetrics.icon(image.withSymbolConfiguration(configuration) ?? image)
                button.imagePosition = .imageOnly
                button.imageScaling = .scaleNone
            }
            button.bezelStyle = .texturedRounded
            button.controlSize = .small
            // 无常驻边框或底色；所有状态共用完整按钮区域，
            // 不在悬停时切换原生 bezel 或 AppKit alignment inset。
            button.isBordered = false
            button.focusRingType = .none
            button.refusesFirstResponder = true
            button.setButtonType(.pushOnPushOff)
            button.isEnabled = enabled ?? actions[command]?.enabled ?? false
            button.state = (checked ?? actions[command]?.checked ?? false) ? .on : .off
            button.onHover = { [weak self, weak button] entered in
                guard let self else { return }
                if entered && button?.isEnabled == true {
                    self.hoveredCommand = command
                    self.onPreview?(preview ? command : nil)
                } else if self.hoveredCommand == command {
                    // AppKit 不保证不同 tracking area 的 enter/exit 顺序。
                    // 旧按钮的 exit 不应清除新按钮刚建立的预览。
                    self.hoveredCommand = nil
                    self.onPreview?(nil)
                }
            }
            button.translatesAutoresizingMaskIntoConstraints = false
            NSLayoutConstraint.activate([
                button.widthAnchor.constraint(equalToConstant: NativeFormatToolbarMetrics.buttonSize),
                button.heightAnchor.constraint(equalToConstant: NativeFormatToolbarMetrics.buttonSize),
            ])
            stack.addArrangedSubview(button)
        }
        /// 功能分组使用 AppKit 原生分隔线，前后留出统一的组间距。
        func groupBreak() {
            if let previous = stack.arrangedSubviews.last {
                stack.setCustomSpacing(NativeFormatToolbarMetrics.groupSpacing, after: previous)
            }
            let line = NSBox()
            line.boxType = .separator
            line.translatesAutoresizingMaskIntoConstraints = false
            NSLayoutConstraint.activate([
                line.widthAnchor.constraint(equalToConstant: NativeFormatToolbarMetrics.separatorWidth),
                line.heightAnchor.constraint(equalToConstant: NativeFormatToolbarMetrics.separatorHeight),
            ])
            stack.addArrangedSubview(line)
            stack.setCustomSpacing(NativeFormatToolbarMetrics.groupSpacing, after: line)
        }

        add("toggleBold", title: L10n.t("粗体"), symbol: "bold", textSymbol: true, preview: true)
        add("toggleItalic", title: L10n.t("斜体"), symbol: "italic", textSymbol: true, preview: true)
        add("toggleUnderline", title: L10n.t("下划线"), symbol: "underline", textSymbol: true, preview: true)
        add("toggleStrike", title: L10n.t("删除线"), symbol: "strikethrough", textSymbol: true, preview: true)
        add("toggleHighlight", title: L10n.t("高亮"), symbol: "highlighter", preview: true)
        if actions["promoteHeading"]?.enabled == true || actions["demoteHeading"]?.enabled == true {
            groupBreak()
            // textformat.size.larger/smaller 是文字类符号（A 字形），
            // 必须与 B/I/U/S 共用文字类配置，否则 A 显得又小又不齐。
            add("promoteHeading", title: L10n.t("提升标题级别"), symbol: "textformat.size.larger", textSymbol: true)
            add("demoteHeading", title: L10n.t("降低标题级别"), symbol: "textformat.size.smaller", textSymbol: true)
        }
        groupBreak()
        add("formatPainter", title: L10n.t("格式刷"), symbol: "paintbrush",
            enabled: (formatPainterArmed ?? false) || (canStartFormatPainter ?? actions["formatPainter"]?.enabled ?? false),
            checked: formatPainterArmed)
        add("clearFormat", title: L10n.t("清除格式"), symbol: "eraser")

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor, constant: NativeFormatToolbarMetrics.horizontalInset),
            stack.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -NativeFormatToolbarMetrics.horizontalInset),
            stack.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
        // NSMenu 通过自定义 view 的 frame 获取最低菜单宽度与行高。
        frame.size = NSSize(width: stack.fittingSize.width + 2 * NativeFormatToolbarMetrics.horizontalInset,
                            height: NativeFormatToolbarMetrics.rowHeight)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    @objc private func activate(_ sender: NSButton) {
        guard sender.isEnabled, let command = sender.identifier?.rawValue else { return }
        onCommand?(command)
    }
}

/// 悬停由菜单中的原生按钮接收，不使用全局鼠标监视器或 WebView 命中测试。
private final class NativeFormatToolbarButton: NSButton {
    var onHover: ((Bool) -> Void)?
    private var hoverArea: NSTrackingArea?
    private var menuHovered = false

    // This toolbar paints and hit-tests the entire button bounds. Its layout
    // rectangle must be those same bounds, not SF Symbols' baseline rectangle.
    override var alignmentRectInsets: NSEdgeInsets { NSEdgeInsetsZero }

    private func setMenuHovered(_ entered: Bool) {
        let hovered = entered && isEnabled
        guard menuHovered != hovered else { return }
        menuHovered = hovered
        updateContentTint()
        needsDisplay = true
        onHover?(hovered)
    }

    override func draw(_ dirtyRect: NSRect) {
        let background = NSBezierPath(roundedRect: bounds,
                                      xRadius: NativeFormatToolbarMetrics.cornerRadius,
                                      yRadius: NativeFormatToolbarMetrics.cornerRadius)
        let checked = state == .on && isEnabled
        if menuHovered && isEnabled {
            // 悬停：accent 实底 + 原生菜单选中文字颜色。
            NSColor.controlAccentColor.setFill()
            background.fill()
        } else if checked {
            NSColor.controlAccentColor.withAlphaComponent(0.15).setFill()
            background.fill()
        }
        updateContentTint()
        super.draw(dirtyRect)
    }

    private func updateContentTint() {
        let tint: NSColor = !isEnabled ? .tertiaryLabelColor
            : menuHovered ? .selectedMenuItemTextColor : .labelColor
        if contentTintColor != tint { contentTintColor = tint }
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if window == nil { setMenuHovered(false) }
    }

    override func updateTrackingAreas() {
        if let hoverArea { removeTrackingArea(hoverArea) }
        let area = NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self)
        addTrackingArea(area)
        hoverArea = area
        super.updateTrackingAreas()
    }

    override func mouseEntered(with event: NSEvent) {
        super.mouseEntered(with: event)
        setMenuHovered(true)
    }

    override func mouseExited(with event: NSEvent) {
        super.mouseExited(with: event)
        setMenuHovered(false)
    }
}
