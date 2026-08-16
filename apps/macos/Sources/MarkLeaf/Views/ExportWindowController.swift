import AppKit
import WebKit

/// “导出…”对话框：格式（PDF / HTML）切换 + 左侧选项，右侧 WKWebView 实时预览，
/// 底部“导出… / 取消”。PDF 显示纸张/方向/页边距选项，HTML 隐藏之；两种格式均带预览。
final class ExportWindowController: NSWindowController, NSWindowDelegate {
    private weak var session: EditorSession?
    var onClose: (() -> Void)?

    private let formatPopup = NSPopUpButton()
    private let paperPopup = NSPopUpButton()
    private let landscapeCheck = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let marginPopup = NSPopUpButton()
    private let customMarginButton = NSButton(title: "", target: nil, action: nil)
    private let stylePopup = NSPopUpButton()
    private let colorThemePopup = NSPopUpButton()
    private let headerField = NSTextField(string: "")
    private let footerField = NSTextField(string: "")
    private let previewView = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
    private let statusLabel = NSTextField(labelWithString: "")

    private var paperRow: NSView?
    private var landscapeRow: NSView?
    private var marginRow: NSView?
    private var themeIDs: [String] = []
    private var styleIDs: [String] = []
    private var margins = ExportMargins()
    private var previewTimer: DispatchWorkItem?
    private var previewGeneration = 0
    private var customMarginItemIndex: Int?

    private static let marginPresets: [(String, ExportMargins)] = [
        (L10n.t("标准"), ExportMargins(top: 18, bottom: 18, left: 15, right: 15)),
        (L10n.t("窄"), ExportMargins(top: 10, bottom: 10, left: 10, right: 10)),
        (L10n.t("宽"), ExportMargins(top: 25, bottom: 25, left: 20, right: 20)),
        (L10n.t("无"), ExportMargins(top: 0, bottom: 0, left: 0, right: 0)),
    ]

    init(session: EditorSession) {
        self.session = session
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 920, height: 680),
            styleMask: [.titled, .closable, .resizable],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("导出文档")
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self
        buildContent()
        populateOptions()
        window.center()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func buildContent() {
        guard let window else { return }

        previewView.underPageBackgroundColor = .white
        previewView.setValue(false, forKey: "drawsBackground")

        formatPopup.addItems(withTitles: [L10n.t("PDF"), L10n.t("HTML")])
        formatPopup.selectItem(at: 0)
        formatPopup.target = self
        formatPopup.action = #selector(formatChanged)
        formatPopup.widthAnchor.constraint(equalToConstant: 120).isActive = true

        paperPopup.addItems(withTitles: PaperSize.allCases.map(\.rawValue))
        paperPopup.selectItem(withTitle: "A4")

        landscapeCheck.title = L10n.t("横向")
        landscapeCheck.target = self
        landscapeCheck.action = #selector(optionChanged)

        marginPopup.addItems(withTitles: Self.marginPresets.map(\.0))
        marginPopup.selectItem(withTitle: L10n.t("标准"))
        marginPopup.target = self
        marginPopup.action = #selector(marginPresetChanged)

        customMarginButton.title = L10n.t("自定义边距…")
        customMarginButton.bezelStyle = .rounded
        customMarginButton.target = self
        customMarginButton.action = #selector(presentCustomMarginSheet)

        stylePopup.target = self
        stylePopup.action = #selector(optionChanged)
        colorThemePopup.target = self
        colorThemePopup.action = #selector(optionChanged)
        headerField.target = self
        headerField.action = #selector(optionChanged)
        footerField.target = self
        footerField.action = #selector(optionChanged)
        headerField.widthAnchor.constraint(equalToConstant: 180).isActive = true
        footerField.widthAnchor.constraint(equalToConstant: 180).isActive = true
        stylePopup.widthAnchor.constraint(equalToConstant: 180).isActive = true
        colorThemePopup.widthAnchor.constraint(equalToConstant: 180).isActive = true

        statusLabel.font = .systemFont(ofSize: 12)
        statusLabel.textColor = .secondaryLabelColor

        let exportButton = NSButton(title: L10n.t("导出…"), target: self, action: #selector(exportClicked))
        exportButton.keyEquivalent = "\r"
        exportButton.bezelStyle = .rounded
        let cancelButton = NSButton(title: L10n.t("取消"), target: self, action: #selector(cancelClicked))
        cancelButton.bezelStyle = .rounded

        paperRow = labeled(L10n.t("纸张"), paperPopup)
        landscapeRow = labeled(L10n.t("方向"), landscapeCheck)
        marginRow = labeled(L10n.t("页边距"), NSStackView(views: [marginPopup, customMarginButton]))

        let optionsStack = NSStackView(views: [
            labeled(L10n.t("格式"), formatPopup),
            paperRow!,
            landscapeRow!,
            marginRow!,
            labeled(L10n.t("排版样式"), stylePopup),
            labeled(L10n.t("配色方案"), colorThemePopup),
            labeled(L10n.t("页眉"), headerField),
            labeled(L10n.t("页脚"), footerField),
        ])
        optionsStack.orientation = .vertical
        optionsStack.alignment = .leading
        optionsStack.spacing = 12
        optionsStack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        optionsStack.translatesAutoresizingMaskIntoConstraints = false
        optionsStack.widthAnchor.constraint(equalToConstant: 300).isActive = true

        let previewContainer = NSView()
        previewContainer.translatesAutoresizingMaskIntoConstraints = false
        previewView.translatesAutoresizingMaskIntoConstraints = false
        previewContainer.addSubview(previewView)
        NSLayoutConstraint.activate([
            previewView.leadingAnchor.constraint(equalTo: previewContainer.leadingAnchor),
            previewView.trailingAnchor.constraint(equalTo: previewContainer.trailingAnchor),
            previewView.topAnchor.constraint(equalTo: previewContainer.topAnchor),
            previewView.bottomAnchor.constraint(equalTo: previewContainer.bottomAnchor),
        ])

        let buttonRow = NSStackView(views: [NSView(), statusLabel, exportButton, cancelButton])
        buttonRow.orientation = .horizontal
        buttonRow.spacing = 10
        buttonRow.translatesAutoresizingMaskIntoConstraints = false

        let root = NSView()
        root.addSubview(optionsStack)
        root.addSubview(previewContainer)
        root.addSubview(buttonRow)

        window.contentView = root
        NSLayoutConstraint.activate([
            optionsStack.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            optionsStack.topAnchor.constraint(equalTo: root.topAnchor),
            optionsStack.bottomAnchor.constraint(equalTo: buttonRow.topAnchor, constant: -8),

            previewContainer.leadingAnchor.constraint(equalTo: optionsStack.trailingAnchor, constant: 4),
            previewContainer.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -12),
            previewContainer.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            previewContainer.bottomAnchor.constraint(equalTo: buttonRow.topAnchor, constant: -8),

            buttonRow.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            buttonRow.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            buttonRow.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -14),
        ])
        if let contentView = window.contentView {
            contentView.widthAnchor.constraint(equalToConstant: 920).isActive = true
            contentView.heightAnchor.constraint(equalToConstant: 680).isActive = true
        }
        updatePDFVisibility()
    }

    private func labeled(_ title: String, _ control: NSView) -> NSView {
        let label = NSTextField(labelWithString: title)
        label.font = .systemFont(ofSize: 12)
        label.alignment = .right
        label.widthAnchor.constraint(equalToConstant: 72).isActive = true
        label.setContentHuggingPriority(.required, for: .horizontal)
        let row = NSStackView(views: [label, control])
        row.orientation = .horizontal
        row.spacing = 8
        return row
    }

    private func populateOptions() {
        guard let session else { return }
        styleIDs = session.styles.map(\.id)
        themeIDs = session.colorThemes.map(\.id)
        stylePopup.addItems(withTitles: session.styles.map { L10n.t($0.displayName) })
        if let idx = styleIDs.firstIndex(of: session.currentStyleId) {
            stylePopup.selectItem(at: idx)
        }
        colorThemePopup.addItems(withTitles: session.colorThemes.map { L10n.t($0.displayName) })
        if let idx = themeIDs.firstIndex(of: session.currentThemeId ?? "colors-white-only") {
            colorThemePopup.selectItem(at: idx)
        }
        margins = ExportMargins()
        marginPopup.addItem(withTitle: L10n.t("自定义"))
        customMarginItemIndex = marginPopup.numberOfItems - 1
        refreshPreview()
    }

    private var selectedStyleID: String {
        let idx = stylePopup.indexOfSelectedItem
        return idx >= 0 && idx < styleIDs.count ? styleIDs[idx] : session?.currentStyleId ?? "serif"
    }

    private var selectedThemeID: String? {
        let idx = colorThemePopup.indexOfSelectedItem
        guard idx >= 0, idx < themeIDs.count else { return nil }
        return themeIDs[idx]
    }

    private var selectedFormat: String {
        formatPopup.indexOfSelectedItem == 1 ? "html" : "pdf"
    }

    private func currentOptions() -> ExportOptions {
        var options = ExportOptions()
        options.format = selectedFormat
        options.style = selectedStyleID
        options.colorScheme = selectedThemeID
        options.paperSize = PaperSize(rawValue: paperPopup.titleOfSelectedItem ?? "A4") ?? .a4
        options.landscape = landscapeCheck.state == .on
        options.margins = margins
        options.header = headerField.stringValue
        options.footer = footerField.stringValue
        return options
    }

    // MARK: - 格式切换

    @objc private func formatChanged() {
        updatePDFVisibility()
        refreshPreview()
    }

    private func updatePDFVisibility() {
        let isPDF = selectedFormat == "pdf"
        paperRow?.isHidden = !isPDF
        landscapeRow?.isHidden = !isPDF
        marginRow?.isHidden = !isPDF
    }

    // MARK: - 预览

    @objc private func optionChanged() {
        schedulePreview()
    }

    @objc private func marginPresetChanged() {
        let idx = marginPopup.indexOfSelectedItem
        if idx < Self.marginPresets.count {
            margins = Self.marginPresets[idx].1
        }
        schedulePreview()
    }

    private func schedulePreview() {
        previewTimer?.cancel()
        let item = DispatchWorkItem { [weak self] in self?.refreshPreview() }
        previewTimer = item
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3, execute: item)
    }

    private func refreshPreview() {
        guard let session else { return }
        previewGeneration += 1
        let generation = previewGeneration
        statusLabel.stringValue = L10n.t("正在生成预览…")
        session.requestExportHTML(options: currentOptions()) { [weak self] html in
            guard let self, generation == self.previewGeneration else { return }
            self.previewView.loadHTMLString(html, baseURL: nil)
            self.statusLabel.stringValue = L10n.t("预览")
        }
    }

    // MARK: - 自定义边距

    @objc private func presentCustomMarginSheet() {
        guard let window else { return }
        let topField = marginField(String(format: "%.1f", margins.top))
        let bottomField = marginField(String(format: "%.1f", margins.bottom))
        let leftField = marginField(String(format: "%.1f", margins.left))
        let rightField = marginField(String(format: "%.1f", margins.right))

        let accessory = NSView(frame: NSRect(x: 0, y: 0, width: 300, height: 96))
        let rows: [(String, NSTextField)] = [
            (L10n.t("上边距"), topField),
            (L10n.t("下边距"), bottomField),
            (L10n.t("左边距"), leftField),
            (L10n.t("右边距"), rightField),
        ]
        let grid = NSGridView(views: rows.map { [marginLabel($0.0), $0.1] })
        grid.rowSpacing = 8
        grid.columnSpacing = 8
        grid.translatesAutoresizingMaskIntoConstraints = false
        accessory.addSubview(grid)
        NSLayoutConstraint.activate([
            grid.centerXAnchor.constraint(equalTo: accessory.centerXAnchor),
            grid.centerYAnchor.constraint(equalTo: accessory.centerYAnchor),
        ])

        let alert = NSAlert()
        alert.messageText = L10n.t("自定义边距")
        alert.accessoryView = accessory
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))
        let okButton = alert.buttons.first
        func refreshOK() {
            okButton?.isEnabled = [topField, bottomField, leftField, rightField].allSatisfy { marginValue($0) != nil }
        }
        refreshOK()
        var tokens: [NSObjectProtocol] = []
        for field in [topField, bottomField, leftField, rightField] {
            tokens.append(NotificationCenter.default.addObserver(
                forName: NSControl.textDidChangeNotification,
                object: field,
                queue: .main
            ) { _ in refreshOK() })
        }
        alert.beginSheetModal(for: window) { [weak self] response in
            tokens.forEach { NotificationCenter.default.removeObserver($0) }
            guard response == .alertFirstButtonReturn,
                  let top = self?.marginValue(topField),
                  let bottom = self?.marginValue(bottomField),
                  let left = self?.marginValue(leftField),
                  let right = self?.marginValue(rightField) else { return }
            self?.margins = ExportMargins(top: top, bottom: bottom, left: left, right: right)
            if let index = self?.customMarginItemIndex {
                self?.marginPopup.selectItem(at: index)
            }
            self?.schedulePreview()
        }
    }

    private func marginLabel(_ text: String) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 12)
        label.alignment = .right
        return label
    }

    private func marginField(_ value: String) -> NSTextField {
        let field = NSTextField(string: value)
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        formatter.minimum = 0
        formatter.maximum = 100
        formatter.maximumFractionDigits = 1
        field.formatter = formatter
        field.alignment = .center
        field.widthAnchor.constraint(equalToConstant: 70).isActive = true
        return field
    }

    private func marginValue(_ field: NSTextField) -> Double? {
        Double(field.stringValue.replacingOccurrences(of: ",", with: "."))
    }

    // MARK: - 导出/取消

    @objc private func exportClicked() {
        guard let session, let window else { return }
        let options = currentOptions()
        let panel = NSSavePanel()
        panel.title = L10n.t("导出文档")
        let baseName = session.documentURL?.deletingPathExtension().lastPathComponent ?? L10n.t("未命名")
        panel.nameFieldStringValue = baseName + "." + (options.format == "pdf" ? "pdf" : "html")
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url, let self else { return }
            let targetURL = EditorSession.fixExportExtension(url, format: options.format)
            session.runExport(options: options, saveURL: targetURL)
            self.close()
        }
    }

    @objc private func cancelClicked() {
        close()
    }

    // MARK: - 生命周期

    func windowWillClose(_ notification: Notification) {
        previewTimer?.cancel()
        onClose?()
    }
}
