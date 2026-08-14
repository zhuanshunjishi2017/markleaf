import AppKit
import UniformTypeIdentifiers

extension EditorSession {
    /// 打开导出面板（对应 C# ExportDocumentAsync + ExportDialog）。
    func exportDocument() {
        guard let window = webView?.window else { return }
        let panel = NSSavePanel()
        panel.title = L10n.t("导出文档")
        let baseName = documentURL?.deletingPathExtension().lastPathComponent ?? L10n.t("未命名")
        panel.nameFieldStringValue = baseName + ".pdf"

        let accessory = ExportAccessory(styles: styles, themes: colorThemes)
        if let idx = styles.firstIndex(where: { $0.id == currentStyleId }) {
            accessory.stylePopup.selectItem(at: idx)
        }
        if let idx = colorThemes.firstIndex(where: { $0.id == currentThemeId }) {
            accessory.colorThemePopup.selectItem(at: idx)
        }
        panel.accessoryView = accessory

        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            var options = accessory.options
            if options.style.isEmpty || !(self?.styles.contains(where: { $0.id == options.style }) ?? false) {
                options.style = self?.currentStyleId ?? "serif"
            }
            // 根据格式修正扩展名
            let targetURL = Self.fixExportExtension(url, format: options.format)
            self?.runExport(options: options, saveURL: targetURL)
        }
    }

    private static func fixExportExtension(_ url: URL, format: String) -> URL {
        let ext = format == "pdf" ? "pdf" : "html"
        if url.pathExtension.lowercased() == ext {
            return url
        }
        return url.deletingPathExtension().appendingPathExtension(ext)
    }

    /// 核心导出流程：请求前端生成导出 HTML，再按格式落盘。
    func runExport(options: ExportOptions, saveURL: URL) {
        let settings = SettingsService.shared.settings
        // 导出配色：按所选颜色主题注入主题 CSS（对应 Windows ExportDialog 的配色方案）
        let colorSchemeCss = options.colorScheme.flatMap { id in
            colorThemes.first(where: { $0.id == id })?.css
        } ?? ""
        let payload: [String: Any] = [
            "format": options.format,
            "style": options.style,
            "header": options.header,
            "footer": options.footer,
            "fontSize": settings.visualFontSize,
            "lineHeight": settings.visualLineHeight,
            "maxWidth": settings.visualMaxContentWidth,
            "colorSchemeCss": colorSchemeCss,
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: payload),
              let text = String(data: data, encoding: .utf8) else { return }
        pendingExportContext = ExportContext(options: options, saveURL: saveURL)
        pendingExport = true
        statusText = L10n.t("正在生成导出内容…")
        execute("exportDocument", text: text)
    }

    func handleExportedContent(_ html: String) {
        guard let context = pendingExportContext else {
            AppLog.warning(L10n.t("收到无上下文的导出内容"))
            return
        }
        pendingExportContext = nil

        if context.options.format == "pdf" {
            statusText = L10n.t("正在打开打印面板…")
            AppLog.info("开始 PDF 导出（系统打印面板，纸张 \(context.options.paperSize.rawValue)）")
            guard let window = webView?.window else {
                presentError(L10n.t("无法打开打印面板"))
                onExportComplete?(false)
                return
            }
            PDFGenerator().printPDF(
                html: html,
                paperSize: context.options.paperSize,
                landscape: context.options.landscape,
                margins: context.options.margins,
                window: window
            ) { [weak self] result in
                DispatchQueue.main.async {
                    switch result {
                    case .success(let printed):
                        if printed {
                            self?.statusText = L10n.t("已导出 PDF")
                            AppLog.info("PDF 已通过系统打印面板导出")
                            self?.onExportComplete?(true)
                        } else {
                            self?.statusText = ""
                            self?.onExportComplete?(false)
                        }
                    case .failure(let error):
                        self?.presentError("PDF 导出失败：\(error.localizedDescription)")
                        self?.onExportComplete?(false)
                    }
                }
            }
        } else {
            do {
                try html.write(to: context.saveURL, atomically: true, encoding: .utf8)
                statusText = L10n.t("已导出 HTML")
                AppLog.info("HTML 已导出: \(context.saveURL.path)")
                onExportComplete?(true)
            } catch {
                presentError("导出失败：\(error.localizedDescription)")
                onExportComplete?(false)
            }
        }
    }
}
