import AppKit
import UniformTypeIdentifiers

extension EditorSession {
    /// 打开导出面板（对应 C# ExportDocumentAsync + ExportDialog）。
    func exportDocument() {
        guard let window = webView?.window else { return }
        let panel = NSSavePanel()
        panel.title = "导出文档"
        let baseName = documentURL?.deletingPathExtension().lastPathComponent ?? "未命名"
        panel.nameFieldStringValue = baseName + ".pdf"

        let accessory = ExportAccessory(styles: styles)
        if let idx = styles.firstIndex(where: { $0.id == currentStyleId }) {
            accessory.stylePopup.selectItem(at: idx)
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
        let payload: [String: Any] = [
            "format": options.format,
            "style": options.style,
            "header": options.header,
            "footer": options.footer,
            "fontSize": settings.visualFontSize,
            "lineHeight": settings.visualLineHeight,
            "maxWidth": settings.visualMaxContentWidth,
        ]
        guard let data = try? JSONSerialization.data(withJSONObject: payload),
              let text = String(data: data, encoding: .utf8) else { return }
        pendingExportContext = ExportContext(options: options, saveURL: saveURL)
        pendingExport = true
        statusText = "正在生成导出内容…"
        execute("exportDocument", text: text)
    }

    func handleExportedContent(_ html: String) {
        guard let context = pendingExportContext else {
            AppLog.warning("收到无上下文的导出内容")
            return
        }
        pendingExportContext = nil

        if context.options.format == "pdf" {
            statusText = "正在生成 PDF…"
            AppLog.info("开始 PDF 导出流水线 (纸张 \(context.options.paperSize.rawValue))")
            PDFGenerator().generatePDF(
                html: html,
                paperSize: context.options.paperSize,
                landscape: context.options.landscape,
                margins: context.options.margins
            ) { [weak self] result in
                DispatchQueue.main.async {
                    switch result {
                    case .success(let data):
                        do {
                            try data.write(to: context.saveURL)
                            self?.statusText = "已导出 PDF"
                            AppLog.info("PDF 已导出: \(context.saveURL.path) (\(data.count) bytes)")
                            self?.onExportComplete?(true)
                        } catch {
                            self?.presentError("导出失败：\(error.localizedDescription)")
                            self?.onExportComplete?(false)
                        }
                    case .failure(let error):
                        self?.presentError("PDF 生成失败：\(error.localizedDescription)")
                        self?.onExportComplete?(false)
                    }
                }
            }
        } else {
            do {
                try html.write(to: context.saveURL, atomically: true, encoding: .utf8)
                statusText = "已导出 HTML"
                AppLog.info("HTML 已导出: \(context.saveURL.path)")
                onExportComplete?(true)
            } catch {
                presentError("导出失败：\(error.localizedDescription)")
                onExportComplete?(false)
            }
        }
    }
}
