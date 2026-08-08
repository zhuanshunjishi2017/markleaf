import AppKit

/// 导出选项（对应 C# ExportDialog.Options）。
struct ExportOptions {
    var format = "html"
    var style = "serif"
    var colorScheme: String? = nil
    var paperSize = PaperSize.a4
    var landscape = false
    var margins = ExportMargins()
    var header = ""
    var footer = ""
}

/// 保存面板附属视图：格式/纸张/方向/边距/样式/页眉页脚（对应 Windows ExportDialog）。
final class ExportAccessory: NSView {
    let formatPopup = NSPopUpButton()
    let paperPopup = NSPopUpButton()
    let landscapeCheck = NSButton(checkboxWithTitle: "横向", target: nil, action: nil)
    let marginPopup = NSPopUpButton()
    let stylePopup = NSPopUpButton()
    let colorThemePopup = NSPopUpButton()
    let headerField = NSTextField(string: "")
    let footerField = NSTextField(string: "")
    private var themeIDs: [String] = []

    private static let marginPresets: [(String, ExportMargins)] = [
        ("标准", ExportMargins(top: 18, bottom: 18, left: 15, right: 15)),
        ("窄", ExportMargins(top: 10, bottom: 10, left: 10, right: 10)),
        ("宽", ExportMargins(top: 25, bottom: 25, left: 20, right: 20)),
        ("无", ExportMargins(top: 0, bottom: 0, left: 0, right: 0)),
    ]

    init(styles: [StyleDefinition], themes: [ColorThemeInfo]) {
        super.init(frame: NSRect(x: 0, y: 0, width: 380, height: 170))
        themeIDs = themes.map(\.id)

        formatPopup.addItems(withTitles: ["PDF", "HTML"])
        formatPopup.selectItem(at: 0)
        formatPopup.target = self
        formatPopup.action = #selector(formatChanged)

        paperPopup.addItems(withTitles: PaperSize.allCases.map(\.rawValue))
        paperPopup.selectItem(withTitle: "A4")

        marginPopup.addItems(withTitles: Self.marginPresets.map(\.0))
        marginPopup.selectItem(withTitle: "标准")

        stylePopup.addItems(withTitles: styles.map(\.displayName))
        if let idx = styles.firstIndex(where: { $0.id == "serif" }) {
            stylePopup.selectItem(at: idx)
        }

        colorThemePopup.addItems(withTitles: themes.map(\.displayName))
        if let idx = themes.firstIndex(where: { $0.id == "colors-white-only" }) {
            colorThemePopup.selectItem(at: idx)
        } else if let idx = themes.firstIndex(where: { $0.id == "colors-apple-blue" }) {
            colorThemePopup.selectItem(at: idx)
        }

        let form = NSGridView(views: [
            [label("格式"), formatPopup],
            [label("纸张"), paperPopup],
            [label("方向"), landscapeCheck],
            [label("页边距"), marginPopup],
            [label("排版样式"), stylePopup],
            [label("配色方案"), colorThemePopup],
            [label("页眉"), headerField],
            [label("页脚"), footerField],
        ])
        form.columnSpacing = 10
        form.rowSpacing = 6
        form.translatesAutoresizingMaskIntoConstraints = false
        addSubview(form)
        NSLayoutConstraint.activate([
            form.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 8),
            form.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            form.topAnchor.constraint(equalTo: topAnchor, constant: 8),
            form.bottomAnchor.constraint(lessThanOrEqualTo: bottomAnchor, constant: -8),
        ])
        updatePDFControls()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    @objc private func formatChanged() {
        updatePDFControls()
    }

    private func updatePDFControls() {
        let isPDF = formatPopup.indexOfSelectedItem == 0
        paperPopup.isEnabled = isPDF
        landscapeCheck.isEnabled = isPDF
        marginPopup.isEnabled = isPDF
    }

    private func label(_ text: String) -> NSTextField {
        let field = NSTextField(labelWithString: text)
        field.alignment = .right
        field.font = .systemFont(ofSize: 12)
        return field
    }

    var options: ExportOptions {
        var options = ExportOptions()
        options.format = formatPopup.indexOfSelectedItem == 0 ? "pdf" : "html"
        options.paperSize = PaperSize(rawValue: paperPopup.titleOfSelectedItem ?? "A4") ?? .a4
        options.landscape = landscapeCheck.state == .on
        options.margins = Self.marginPresets[max(0, marginPopup.indexOfSelectedItem)].1
        options.header = headerField.stringValue
        options.footer = footerField.stringValue
        if colorThemePopup.indexOfSelectedItem >= 0, colorThemePopup.indexOfSelectedItem < themeIDs.count {
            options.colorScheme = themeIDs[colorThemePopup.indexOfSelectedItem]
        }
        return options
    }
}
