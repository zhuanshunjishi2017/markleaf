import AppKit

/// 字体选择控件：圆角文本框显示当前字体名 + 「选择…」按钮，调起系统字体面板（NSFontPanel）。
/// 用于源码模式的西文/中文字体选择（对齐 Windows fccc7ad 的 SourceFontFamily / SourceCjkFontFamily）。
final class FontField: NSView {
    private let textField = NSTextField(string: "")
    private let chooseButton = NSButton(title: "", target: nil, action: nil)
    private let onChange: (String) -> Void
    private(set) var fontName: String

    init(fontName: String, onChange: @escaping (String) -> Void) {
        self.fontName = fontName
        self.onChange = onChange
        super.init(frame: .zero)

        textField.stringValue = Self.displayName(for: fontName)
        textField.isEditable = false
        textField.isSelectable = true
        textField.bezelStyle = .roundedBezel
        textField.font = .systemFont(ofSize: 13)
        textField.widthAnchor.constraint(equalToConstant: 210).isActive = true

        chooseButton.title = L10n.t("选择…")
        chooseButton.bezelStyle = .rounded
        chooseButton.target = self
        chooseButton.action = #selector(chooseFont)

        let stack = NSStackView(views: [textField, chooseButton])
        stack.orientation = .horizontal
        stack.spacing = 8
        stack.alignment = .centerY
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: trailingAnchor),
            stack.topAnchor.constraint(equalTo: topAnchor),
            stack.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    @objc private func chooseFont() {
        let fontManager = NSFontManager.shared
        fontManager.target = self
        fontManager.action = #selector(changeFont(_:))
        let current = NSFont(name: fontName, size: 13)
            ?? NSFont.monospacedSystemFont(ofSize: 13, weight: .regular)
        fontManager.fontPanel(true)?.setPanelFont(current, isMultiple: false)
        fontManager.fontPanel(true)?.makeKeyAndOrderFront(nil)
    }

    @objc func changeFont(_ sender: NSFontManager) {
        let font = sender.convert(.systemFont(ofSize: 13))
        fontName = font.fontName
        textField.stringValue = font.displayName ?? font.fontName
        onChange(font.fontName)
    }

    private static func displayName(for name: String) -> String {
        NSFont(name: name, size: 13)?.displayName ?? name
    }
}
