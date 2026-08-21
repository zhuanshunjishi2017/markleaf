import AppKit

/// 文档统计窗口：以网格展示字符/选区/段落等统计信息（对齐 Windows ShowDocumentStatisticsDialog）。
final class DocumentStatisticsWindowController: NSWindowController {
    init(session: EditorSession) {
        let stats = session.documentStatistics
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 360, height: 420),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false)
        window.title = L10n.t("文档统计")
        window.center()
        super.init(window: window)

        let rows: [(String, String)] = [
            (L10n.t("字符数"), "\(stats.characterCount)"),
            (L10n.t("已选字符数"), "\(stats.selectedCharacterCount)"),
            (L10n.t("总字符数"), "\(stats.totalCharacterCount)"),
            (L10n.t("非空白字符数"), "\(stats.nonWhitespaceCharacterCount)"),
            (L10n.t("CJK 字符数"), "\(stats.cjkCharacterCount)"),
            (L10n.t("西文单词数"), "\(stats.westernWordCount)"),
            (L10n.t("公式数"), "\(stats.formulaCount)"),
            (L10n.t("代码行数"), "\(stats.codeLineCount)"),
            (L10n.t("段落数"), "\(stats.paragraphCount)"),
            (L10n.t("行"), "\(stats.line)"),
            (L10n.t("列"), "\(stats.column)"),
            (L10n.t("块类型"), EditorSession.blockTypeDisplayName(stats.blockType)),
            (L10n.t("编码"), session.documentEncoding),
            (L10n.t("换行符"), session.documentNewLine),
        ]

        var gridViews: [[NSView]] = []
        for (label, value) in rows {
            let labelField = NSTextField(labelWithString: label)
            labelField.textColor = .secondaryLabelColor
            let valueField = NSTextField(labelWithString: value)
            valueField.alignment = .right
            valueField.font = .monospacedDigitSystemFont(ofSize: 13, weight: .regular)
            gridViews.append([labelField, valueField])
        }
        let grid = NSGridView(views: gridViews)
        grid.rowSpacing = 9
        grid.columnSpacing = 16
        grid.column(at: 0).xPlacement = .leading
        grid.column(at: 1).xPlacement = .trailing

        let close = NSButton(title: L10n.t("关闭"), target: self, action: #selector(closeAction))
        close.keyEquivalent = "\r"
        let buttons = NSStackView(views: [NSView(), close])
        buttons.orientation = .horizontal

        let stack = NSStackView(views: [grid, buttons])
        stack.orientation = .vertical
        stack.alignment = .width
        stack.spacing = 18
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 24, bottom: 16, right: 24)
        stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView = stack
        let fitting = stack.fittingSize
        window.setContentSize(NSSize(width: max(340, fitting.width), height: fitting.height))
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor),
            stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor),
            stack.bottomAnchor.constraint(equalTo: window.contentView!.bottomAnchor),
        ])
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func runModal() {
        guard let window else { return }
        NSApp.runModal(for: window)
        window.orderOut(nil)
    }

    @objc private func closeAction() {
        NSApp.stopModal()
    }
}
