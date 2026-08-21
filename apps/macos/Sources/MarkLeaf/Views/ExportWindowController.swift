import AppKit
import PDFKit
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
    private let headerPresetPopup = NSPopUpButton()
    private let footerPresetPopup = NSPopUpButton()
    private let headerField = NSTextField(string: "")
    private let footerField = NSTextField(string: "")
    private let headerFieldLabel = NSTextField(labelWithString: "")
    private let footerFieldLabel = NSTextField(labelWithString: "")
    private let headerFieldRow = NSView()
    private let footerFieldRow = NSView()
    private let lastSettingsButton = NSButton(title: "", target: nil, action: nil)
    private let previewView = WKWebView(frame: .zero, configuration: WKWebViewConfiguration())
    private let pageCountLabel = NSTextField(labelWithString: "")

    private var paperRow: NSView?
    private var landscapeRow: NSView?
    private var marginRow: NSView?
    private var headerPresetRow: NSView?
    private var footerPresetRow: NSView?
    private var headerFieldRowHeight: NSLayoutConstraint?
    private var footerFieldRowHeight: NSLayoutConstraint?
    private var themeIDs: [String] = []
    private var styleIDs: [String] = []
    private var margins = ExportMargins()
    private var previewTimer: DispatchWorkItem?
    private var previewGeneration = 0
    private var previewFileCounter = 0
    private var customMarginItemIndex: Int?

    private static let headerFooterPresets: [(id: String, title: String)] = [
        ("none", L10n.t("无")),
        ("title-left", L10n.t("标题（左对齐）")),
        ("page-center", L10n.t("页码（居中）")),
        ("page-right", L10n.t("页码（右对齐）")),
        ("page-total-center", L10n.t("页码/总页数（居中）")),
        ("custom", L10n.t("自定义")),
    ]

    private static let marginPresets: [(String, ExportMargins)] = [
        (L10n.t("标准"), ExportMargins(top: 18, bottom: 18, left: 15, right: 15)),
        (L10n.t("窄"), ExportMargins(top: 10, bottom: 10, left: 10, right: 10)),
        (L10n.t("宽"), ExportMargins(top: 25, bottom: 25, left: 20, right: 20)),
        (L10n.t("无"), ExportMargins(top: 0, bottom: 0, left: 0, right: 0)),
    ]

    private static let fieldRowHeight: CGFloat = 28

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
        paperPopup.target = self
        paperPopup.action = #selector(optionChanged)

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
        headerPresetPopup.addItems(withTitles: Self.headerFooterPresets.map(\.title))
        footerPresetPopup.addItems(withTitles: Self.headerFooterPresets.map(\.title))
        headerPresetPopup.target = self
        footerPresetPopup.target = self
        headerPresetPopup.action = #selector(headerFooterPresetChanged)
        footerPresetPopup.action = #selector(headerFooterPresetChanged)
        headerField.target = self
        headerField.action = #selector(optionChanged)
        footerField.target = self
        footerField.action = #selector(optionChanged)
        headerField.bezelStyle = .roundedBezel
        footerField.bezelStyle = .roundedBezel
        headerField.widthAnchor.constraint(equalToConstant: 180).isActive = true
        footerField.widthAnchor.constraint(equalToConstant: 180).isActive = true
        stylePopup.widthAnchor.constraint(equalToConstant: 180).isActive = true
        colorThemePopup.widthAnchor.constraint(equalToConstant: 180).isActive = true
        headerPresetPopup.widthAnchor.constraint(equalToConstant: 112).isActive = true
        footerPresetPopup.widthAnchor.constraint(equalToConstant: 112).isActive = true
        headerField.placeholderString = L10n.t("支持 {title}、{page}、{pages}")
        footerField.placeholderString = L10n.t("支持 {title}、{page}、{pages}")
        headerFieldLabel.font = .systemFont(ofSize: 12)
        headerFieldLabel.alignment = .right
        headerFieldLabel.widthAnchor.constraint(equalToConstant: 72).isActive = true
        footerFieldLabel.font = .systemFont(ofSize: 12)
        footerFieldLabel.alignment = .right
        footerFieldLabel.widthAnchor.constraint(equalToConstant: 72).isActive = true

        lastSettingsButton.title = L10n.t("按上次设置导出")
        lastSettingsButton.bezelStyle = .rounded
        lastSettingsButton.target = self
        lastSettingsButton.action = #selector(exportWithLastSettingsClicked)

        pageCountLabel.font = .systemFont(ofSize: 12)
        pageCountLabel.textColor = .secondaryLabelColor
        pageCountLabel.alignment = .right

        let exportButton = NSButton(title: L10n.t("导出…"), target: self, action: #selector(exportClicked))
        exportButton.keyEquivalent = "\r"
        exportButton.bezelStyle = .rounded
        let cancelButton = NSButton(title: L10n.t("取消"), target: self, action: #selector(cancelClicked))
        cancelButton.bezelStyle = .rounded

        paperRow = labeled(L10n.t("纸张"), paperPopup)
        landscapeRow = labeled(L10n.t("方向"), landscapeCheck)
        marginRow = labeled(L10n.t("页边距"), NSStackView(views: [marginPopup, customMarginButton]))
        headerPresetRow = labeled(L10n.t("页眉"), headerPresetPopup)
        footerPresetRow = labeled(L10n.t("页脚"), footerPresetPopup)

        configureFieldRow(headerFieldRow, label: headerFieldLabel, field: headerField)
        configureFieldRow(footerFieldRow, label: footerFieldLabel, field: footerField)
        headerFieldRowHeight = headerFieldRow.heightAnchor.constraint(equalToConstant: 0)
        footerFieldRowHeight = footerFieldRow.heightAnchor.constraint(equalToConstant: 0)
        headerFieldRowHeight?.isActive = true
        footerFieldRowHeight?.isActive = true

        let optionsStack = NSStackView(views: [
            labeled(L10n.t("格式"), formatPopup),
            paperRow!,
            landscapeRow!,
            marginRow!,
            labeled(L10n.t("排版样式"), stylePopup),
            labeled(L10n.t("配色方案"), colorThemePopup),
            headerPresetRow!,
            headerFieldRow,
            footerPresetRow!,
            footerFieldRow,
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
        pageCountLabel.translatesAutoresizingMaskIntoConstraints = false
        previewContainer.addSubview(previewView)
        previewContainer.addSubview(pageCountLabel)
        NSLayoutConstraint.activate([
            previewView.leadingAnchor.constraint(equalTo: previewContainer.leadingAnchor),
            previewView.trailingAnchor.constraint(equalTo: previewContainer.trailingAnchor),
            previewView.topAnchor.constraint(equalTo: pageCountLabel.bottomAnchor, constant: 4),
            previewView.bottomAnchor.constraint(equalTo: previewContainer.bottomAnchor),
            pageCountLabel.leadingAnchor.constraint(equalTo: previewContainer.leadingAnchor),
            pageCountLabel.trailingAnchor.constraint(equalTo: previewContainer.trailingAnchor),
            pageCountLabel.topAnchor.constraint(equalTo: previewContainer.topAnchor),
        ])

        let buttonRow = NSStackView(views: [lastSettingsButton, NSView(), exportButton, cancelButton])
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

    private func configureFieldRow(_ row: NSView, label: NSTextField, field: NSTextField) {
        row.translatesAutoresizingMaskIntoConstraints = false
        row.wantsLayer = true
        row.layer?.masksToBounds = true
        row.alphaValue = 0
        row.isHidden = true
        let stack = NSStackView(views: [label, field])
        stack.orientation = .horizontal
        stack.spacing = 8
        stack.translatesAutoresizingMaskIntoConstraints = false
        row.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: row.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: row.trailingAnchor),
            stack.topAnchor.constraint(equalTo: row.topAnchor),
        ])
    }

    private func populateOptions() {
        guard let session else { return }
        styleIDs = session.styles.map(\.id)
        themeIDs = session.colorThemes.map(\.id)
        stylePopup.addItems(withTitles: session.styles.map { L10n.t($0.displayName) })
        let saved = SettingsService.shared.settings.exportSettings
        if let idx = styleIDs.firstIndex(of: saved.style) ?? styleIDs.firstIndex(of: session.currentStyleId) {
            stylePopup.selectItem(at: idx)
        }
        colorThemePopup.addItems(withTitles: session.colorThemes.map { L10n.t($0.displayName) })
        let preferredThemeID = ExportThemeSelectionPolicy.preferredThemeID(
            currentThemeID: session.currentThemeId,
            persistedThemeID: saved.colorTheme,
            availableThemeIDs: themeIDs
        )
        if let idx = preferredThemeID.flatMap(themeIDs.firstIndex(of:)) {
            colorThemePopup.selectItem(at: idx)
        }
        formatPopup.selectItem(at: saved.format == "html" ? 1 : 0)
        paperPopup.selectItem(withTitle: saved.paperSize)
        landscapeCheck.state = saved.landscape ? .on : .off
        margins = ExportMargins(
            top: saved.marginTop, bottom: saved.marginBottom,
            left: saved.marginLeft, right: saved.marginRight
        )
        headerField.stringValue = saved.format == "html" ? saved.htmlHeader : saved.headerCustom
        footerField.stringValue = saved.format == "html" ? saved.htmlFooter : saved.footerCustom
        selectHeaderFooterPreset(saved.headerPreset, in: headerPresetPopup)
        selectHeaderFooterPreset(saved.footerPreset, in: footerPresetPopup)
        marginPopup.addItem(withTitle: L10n.t("自定义"))
        customMarginItemIndex = marginPopup.numberOfItems - 1
        if let presetIndex = Self.marginPresets.firstIndex(where: { preset in
            preset.1.top == margins.top && preset.1.bottom == margins.bottom
                && preset.1.left == margins.left && preset.1.right == margins.right
        }) {
            marginPopup.selectItem(at: presetIndex)
        } else {
            marginPopup.selectItem(at: customMarginItemIndex ?? 0)
        }
        updatePDFVisibility()
        updateHeaderFooterFieldState()
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
        if selectedFormat == "html" {
            options.header = headerField.stringValue
            options.footer = footerField.stringValue
        } else {
            let headerPreset = selectedHeaderFooterPreset(in: headerPresetPopup)
            let footerPreset = selectedHeaderFooterPreset(in: footerPresetPopup)
            options.pdfHeader = PDFHeaderFooterPolicy.text(for: headerPreset, custom: headerField.stringValue)
            options.pdfHeaderAlignment = PDFHeaderFooterPolicy.alignment(for: headerPreset)
            options.pdfFooter = PDFHeaderFooterPolicy.text(for: footerPreset, custom: footerField.stringValue)
            options.pdfFooterAlignment = PDFHeaderFooterPolicy.alignment(for: footerPreset)
            options.headerFooterFontFamily = selectedHeaderFooterFontFamily
        }
        return options
    }

    private var selectedHeaderFooterFontFamily: String {
        guard let style = session?.styles.first(where: { $0.id == selectedStyleID }) else { return "serif" }
        let pattern = "font-family\\s*:\\s*([^;]+);"
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return "serif" }
        let range = NSRange(style.css.startIndex..., in: style.css)
        guard let match = regex.matches(in: style.css, range: range).last,
              let valueRange = Range(match.range(at: 1), in: style.css) else { return "serif" }
        return String(style.css[valueRange]).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private func selectedHeaderFooterPreset(in popup: NSPopUpButton) -> String {
        let index = popup.indexOfSelectedItem
        return Self.headerFooterPresets.indices.contains(index) ? Self.headerFooterPresets[index].id : "none"
    }

    private func selectHeaderFooterPreset(_ preset: String, in popup: NSPopUpButton) {
        let normalized = PDFHeaderFooterPolicy.normalizePreset(preset)
        popup.selectItem(at: Self.headerFooterPresets.firstIndex(where: { $0.id == normalized }) ?? 0)
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
        headerPresetRow?.isHidden = !isPDF
        footerPresetRow?.isHidden = !isPDF
        headerFieldLabel.stringValue = isPDF ? "" : L10n.t("页眉")
        footerFieldLabel.stringValue = isPDF ? "" : L10n.t("页脚")
        updateHeaderFooterFieldState()
    }

    @objc private func headerFooterPresetChanged() {
        updateHeaderFooterFieldState()
        schedulePreview()
    }

    private func updateHeaderFooterFieldState() {
        let isPDF = selectedFormat == "pdf"
        let headerVisible = !isPDF || selectedHeaderFooterPreset(in: headerPresetPopup) == "custom"
        let footerVisible = !isPDF || selectedHeaderFooterPreset(in: footerPresetPopup) == "custom"
        setFieldRowVisible(headerFieldRow, height: headerFieldRowHeight, visible: headerVisible)
        setFieldRowVisible(footerFieldRow, height: footerFieldRowHeight, visible: footerVisible)
    }

    private func setFieldRowVisible(_ row: NSView, height: NSLayoutConstraint?, visible: Bool, animated: Bool = true) {
        guard let height else { return }
        guard visible != (height.constant > 0) else { return }
        if visible, row.isHidden {
            row.isHidden = false
            row.alphaValue = 0
        }
        if animated {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.22
                height.animator().constant = visible ? Self.fieldRowHeight : 0
                row.animator().alphaValue = visible ? 1 : 0
            } completionHandler: {
                if !visible { row.isHidden = true }
            }
        } else {
            height.constant = visible ? Self.fieldRowHeight : 0
            row.alphaValue = visible ? 1 : 0
            if !visible { row.isHidden = true }
        }
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
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.6, execute: item)
    }

    private func refreshPreview() {
        guard let session else { return }
        previewGeneration += 1
        let generation = previewGeneration
        let options = currentOptions()
        // 保留上一份预览与页码，新预览生成完成后再替换，避免闪烁与页码消失。
        session.requestExportHTML(options: options) { [weak self] html in
            guard let self, generation == self.previewGeneration else { return }
            if options.format == "pdf" {
                self.renderPDFPreview(html: html, options: options, generation: generation)
            } else {
                self.previewView.loadHTMLString(html, baseURL: nil)
            }
        }
    }

    /// PDF 预览：用所选纸张/方向/边距生成真实 PDF 并显示，同时给出页数。
    private func renderPDFPreview(html: String, options: ExportOptions, generation: Int) {
        guard let window else { return }
        previewFileCounter += 1
        let url = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("markleaf-preview-\(previewFileCounter).pdf")
        PDFGenerator().printPDF(
            html: html,
            paperSize: options.paperSize,
            landscape: options.landscape,
            margins: options.margins,
            window: window,
            showsPanel: false,
            saveURL: url,
            headerText: options.pdfHeader,
            headerAlignment: options.pdfHeaderAlignment,
            footerText: options.pdfFooter,
            footerAlignment: options.pdfFooterAlignment,
            headerFooterFontFamily: options.headerFooterFontFamily,
            documentTitle: session?.exportTitle ?? L10n.t("未命名")
        ) { [weak self] result in
            DispatchQueue.main.async {
                guard let self, generation == self.previewGeneration else { return }
                if case .success(true) = result {
                    self.previewView.loadFileURL(url, allowingReadAccessTo: url.deletingLastPathComponent())
                    if let document = PDFDocument(url: url) {
                        self.pageCountLabel.stringValue = L10n.f("共 %d 页", document.pageCount)
                    }
                    self.cleanupOldPreviews(except: url)
                }
            }
        }
    }

    private func cleanupOldPreviews(except current: URL) {
        let dir = URL(fileURLWithPath: NSTemporaryDirectory())
        guard let files = try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) else { return }
        let prefix = "markleaf-preview-"
        // 用文件名比较（/var 与 /private/var 的路径表示可能不同，全路径比较会误删当前文件）。
        for file in files where file.lastPathComponent.hasPrefix(prefix) && file.lastPathComponent != current.lastPathComponent {
            try? FileManager.default.removeItem(at: file)
        }
    }

    private func cleanupAllPreviews() {
        let dir = URL(fileURLWithPath: NSTemporaryDirectory())
        guard let files = try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) else { return }
        for file in files where file.lastPathComponent.hasPrefix("markleaf-preview-") {
            try? FileManager.default.removeItem(at: file)
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
        guard let session else { return }
        let options = currentOptions()
        presentSavePanel(options: options, title: L10n.t("导出文档")) { [weak self] url in
            guard let self else { return }
            let targetURL = EditorSession.fixExportExtension(url, format: options.format)
            self.persist(options: options)
            session.runExport(options: options, saveURL: targetURL)
            self.close()
        }
    }

    @objc private func exportWithLastSettingsClicked() {
        guard let session else { return }
        let options = session.exportOptions(from: SettingsService.shared.settings.exportSettings)
        presentSavePanel(options: options, title: L10n.t("按上次设置导出")) { [weak self] url in
            guard let self else { return }
            let targetURL = EditorSession.fixExportExtension(url, format: options.format)
            session.runExport(options: options, saveURL: targetURL)
            self.close()
        }
    }

    private func presentSavePanel(options: ExportOptions, title: String, onSave: @escaping (URL) -> Void) {
        guard let session, let window else { return }
        let panel = NSSavePanel()
        panel.title = title
        let baseName = session.documentURL?.deletingPathExtension().lastPathComponent ?? L10n.t("未命名")
        panel.nameFieldStringValue = baseName + "." + (options.format == "pdf" ? "pdf" : "html")
        panel.beginSheetModal(for: window) { response in
            guard response == .OK, let url = panel.url else { return }
            onSave(url)
        }
    }

    private func persist(options: ExportOptions) {
        let headerPreset = selectedHeaderFooterPreset(in: headerPresetPopup)
        let footerPreset = selectedHeaderFooterPreset(in: footerPresetPopup)
        SettingsService.shared.update { settings in
            settings.exportSettings = PersistedExportSettings(
                format: options.format,
                paperSize: options.paperSize.rawValue,
                landscape: options.landscape,
                marginTop: options.margins.top,
                marginBottom: options.margins.bottom,
                marginLeft: options.margins.left,
                marginRight: options.margins.right,
                style: options.style,
                colorTheme: options.colorScheme ?? "",
                htmlHeader: options.format == "html" ? headerField.stringValue : settings.exportSettings.htmlHeader,
                htmlFooter: options.format == "html" ? footerField.stringValue : settings.exportSettings.htmlFooter,
                headerPreset: headerPreset,
                headerCustom: headerField.stringValue,
                headerAlignment: options.pdfHeaderAlignment,
                footerPreset: footerPreset,
                footerCustom: footerField.stringValue,
                footerAlignment: options.pdfFooterAlignment,
                headerFontFamily: options.headerFooterFontFamily,
                footerFontFamily: options.headerFooterFontFamily
            )
        }
    }

    @objc private func cancelClicked() {
        close()
    }

    // MARK: - 生命周期

    func windowWillClose(_ notification: Notification) {
        previewTimer?.cancel()
        cleanupAllPreviews()
        onClose?()
    }
}
