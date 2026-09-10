import AppKit
import UniformTypeIdentifiers

extension EditorSession {
    enum ClipboardCopyMode {
        case formatted
        case markdown
        case plainText
        case html
    }

    /// 剪贴板中是否有可粘贴内容（文本/富文本/图片/Finder 文件），用于菜单置灰。
    var clipboardHasContent: Bool {
        guard let types = NSPasteboard.general.types else { return false }
        let supported: Set<NSPasteboard.PasteboardType> = [
            .string, .html, .rtf, .png, .tiff, .pdf, .fileURL,
        ]
        return types.contains { supported.contains($0) }
    }

    // MARK: - 复制（对应 C# ExecuteClipboardCopyAsync）

    func copySelectionAs(_ mode: ClipboardCopyMode, pasteboard: NSPasteboard = .general) {
        requestSelectionExport { [weak self] result in
            DispatchQueue.main.async {
                guard let self else { return }
                switch result {
                case .success(let selection):
                    let text: String
                    switch mode {
                    case .markdown: text = selection.markdown
                    case .html: text = selection.html
                    case .formatted, .plainText: text = selection.text
                    }
                    guard !text.isEmpty || (mode == .formatted && !selection.html.isEmpty) else {
                        self.statusText = L10n.t("当前没有可复制的文本")
                        return
                    }
                    pasteboard.clearContents()
                    var copied = pasteboard.setString(text, forType: .string)
                    if mode == .formatted && !selection.html.isEmpty {
                        // macOS 剪贴板 HTML 类型（对应 Windows CF_HTML）
                        copied = pasteboard.setString(selection.html, forType: .html) && copied
                    }
                    guard copied else {
                        self.statusText = L10n.t("剪贴板操作失败")
                        return
                    }
                    if mode == .html {
                        // 与 Windows CopySelectionHtmlAsync 一致：HTML 源码作为文本复制。
                        self.statusText = L10n.t("已复制 HTML")
                    } else {
                        self.statusText = mode == .formatted ? L10n.t("已复制格式化内容") : L10n.t("已复制")
                    }
                case .failure:
                    self.statusText = L10n.t("剪贴板操作失败")
                }
            }
        }
    }

    // MARK: - 粘贴（对应 C# PasteClipboardContentAsync）

    func pasteFromClipboard() {
        guard isReady, !isReadOnly else { return }
        let pasteboard = NSPasteboard.general
        let plainText = pasteboard.string(forType: .string)
        // 源码模式先取文本，再考虑同一剪贴板附带的文件或位图。
        if isSourceMode, let command = EditorPastePolicy.command(isSourceMode: true, plainText: plainText, html: nil) {
            executePaste(command)
            return
        }
        let urls = pasteboard.readObjects(
            forClasses: [NSURL.self],
            options: [.urlReadingFileURLsOnly: true]
        ) as? [URL] ?? []
        let image = NSImage(pasteboard: pasteboard)
        let html = pasteboard.string(forType: .html)

        switch EditorPastePolicy.contentKind(
            hasFinderFiles: !urls.isEmpty,
            hasBitmapImage: image != nil,
            plainText: plainText,
            html: html
        ) {
        case .finderFiles:
            // Finder 文件优先（对应 Windows Clipboard.ContainsFileDropList）→ 按「文件图片」设置导入。
            let imageExtensions = Set(["png", "jpg", "jpeg", "gif", "webp", "bmp"])
            var imported = 0
            for url in urls where imageExtensions.contains(url.pathExtension.lowercased()) {
                insertImageFile(at: url)
                imported += 1
            }
            statusText = imported > 0 ? "已插入 \(imported) 张图片" : L10n.t("未找到可插入的图片")
            return
        case .bitmapImage:
            guard let image else { return }
            importClipboardImage(image)
            return
        case .textOrHTML:
            guard let command = EditorPastePolicy.command(
                isSourceMode: isSourceMode,
                plainText: plainText,
                html: html
            ) else {
                statusText = L10n.t("剪贴板中没有可粘贴的内容")
                return
            }
            executePaste(command)
            return
        case .none:
            statusText = L10n.t("剪贴板中没有可粘贴的内容")
        }
    }

    /// 只读取纯文本格式；与 Windows 一致，可视模式仍按 Markdown 解析。
    func pastePlainTextFromClipboard() {
        guard isReady, !isReadOnly else { return }
        guard let command = EditorPastePolicy.command(
            isSourceMode: isSourceMode, plainText: NSPasteboard.general.string(forType: .string), html: nil
        ) else {
            statusText = L10n.t("剪贴板中没有可粘贴的内容")
            return
        }
        executePaste(command)
    }

    struct PendingPasteCommand {
        let sourceMode: Bool
        let formattedRequested: Bool
        let timeout: DispatchWorkItem
    }

    func executePaste(_ command: EditorPasteCommand) {
        guard isReady, !isReadOnly else { return }
        let requestId = UUID().uuidString.lowercased()
        let timeout = DispatchWorkItem { [weak self] in
            self?.failPasteCommand(requestId, reason: "Paste command timed out")
        }
        pendingPasteCommands[requestId] = PendingPasteCommand(
            sourceMode: isSourceMode,
            formattedRequested: command.html != nil,
            timeout: timeout
        )
        // 与 Windows ExecuteCommandResultAsync 使用相同的 10 秒等待期限。
        DispatchQueue.main.asyncAfter(deadline: .now() + 10, execute: timeout)
        execute(command.command, text: command.text, html: command.html, requestId: requestId) { [weak self] in
            self?.failPasteCommand(requestId, reason: "Paste command could not be sent")
        }
    }

    @discardableResult
    func handlePasteResult(_ message: [String: Any]) -> Bool {
        guard message["documentId"] as? String == currentDocumentIdentifier,
              let requestId = message["requestId"] as? String,
              let pending = pendingPasteCommands.removeValue(forKey: requestId) else { return false }
        pending.timeout.cancel()
        let payload = message["payload"] as? [String: Any]
        guard payload?["success"] as? Bool == true else {
            statusText = L10n.t("无法粘贴剪贴板内容")
            return true
        }
        switch payload?["outcome"] as? String {
        case "markdown": statusText = L10n.t("已粘贴 Markdown")
        case "normalized": statusText = L10n.t("已粘贴 Markdown，并转换了不兼容的格式")
        case "plainText" where !pending.sourceMode:
            if let error = payload?["error"] as? String, !error.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                statusText = L10n.f("已作为纯文本粘贴：%@", error)
            } else {
                statusText = L10n.t("Markdown 格式不兼容，已作为纯文本粘贴")
            }
        case "formatted": statusText = L10n.t("已粘贴格式化内容")
        default: statusText = pending.formattedRequested ? L10n.t("已粘贴格式化内容") : L10n.t("已粘贴纯文本")
        }
        return true
    }

    func failPasteCommand(_ requestId: String, reason: String) {
        guard let pending = pendingPasteCommands.removeValue(forKey: requestId) else { return }
        pending.timeout.cancel()
        AppLog.warning(reason)
        statusText = L10n.t("剪贴板操作失败")
    }

    func cancelPendingPasteCommands() {
        pendingPasteCommands.values.forEach { $0.timeout.cancel() }
        pendingPasteCommands.removeAll()
    }

    /// 剪贴板图片 → 保存到图片目录 → insertImage（对应 C# ImportClipboardBitmapAsync）。
    private func importClipboardImage(_ image: NSImage) {
        guard let tiff = image.tiffRepresentation,
              let rep = NSBitmapImageRep(data: tiff),
              let png = rep.representation(using: .png, properties: [:]) else {
            statusText = L10n.t("无法读取剪贴板图片")
            return
        }

        let directory = imageTargetDirectory()
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let formatter = DateFormatter()
            formatter.dateFormat = "yyyyMMdd-HHmmss"
            let fileName = "clipboard-\(formatter.string(from: Date())).png"
            let fileURL = directory.appendingPathComponent(fileName)
            try png.write(to: fileURL)
            let markdownPath = markdownReferencePath(for: fileURL.path)
            execute("insertImage", text: markdownPath + "\n图片")
            statusText = L10n.t("图片已插入文档")
        } catch {
            presentError("保存剪贴板图片失败：\(error.localizedDescription)")
        }
    }

    /// 图片存放目录：按 clipboardImageHandling 与设置决定。
    private func imageTargetDirectory() -> URL {
        let settings = SettingsService.shared.settings
        if settings.clipboardImageHandling == "copyToAssets", let docURL = documentURL {
            return docURL.deletingLastPathComponent().appendingPathComponent("assets", isDirectory: true)
        }
        if settings.clipboardImageHandling == "copyToAssets" {
            statusText = L10n.t("文档未保存，无法复制到 .assets 目录，图片已保存到默认目录")
        }
        if !settings.imageDefaultDirectory.isEmpty {
            return URL(fileURLWithPath: settings.imageDefaultDirectory, isDirectory: true)
        }
        let pictures = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        return pictures.appendingPathComponent("MarkLeaf", isDirectory: true)
    }

    /// 图片引用路径：按 useRelativePaths 相对文档目录，并可选加 "./" 前缀。
    func markdownReferencePath(for path: String) -> String {
        let settings = SettingsService.shared.settings
        if settings.useRelativePaths, let documentPath = documentURL?.path,
           let relative = MarkdownImagePathPolicy.relative(
            documentPath: documentPath,
            filePath: path,
            prefixDotSlash: settings.prefixRelativeWithDotSlash
           ) {
            return relative
        }
        return MarkdownImagePathPolicy.absolute(path)
    }

    static func encodeMarkdownPath(_ path: String) -> String {
        MarkdownImagePathPolicy.encode(path)
    }

    /// 复制图片到目标目录（对应 C# ImageAssetService.CopyFileIntoAsync）。
    func copyImageToAssets(source: URL, targetDir: URL) -> String {
        do {
            try FileManager.default.createDirectory(at: targetDir, withIntermediateDirectories: true)
            var target = targetDir.appendingPathComponent(source.lastPathComponent)
            if FileManager.default.fileExists(atPath: target.path) {
                let base = source.deletingPathExtension().lastPathComponent
                target = targetDir.appendingPathComponent("\(base)-\(Int(Date().timeIntervalSince1970)).\(source.pathExtension)")
            }
            try FileManager.default.copyItem(at: source, to: target)
            return target.path
        } catch {
            AppLog.warning("图片复制失败，引用原位置: \(error.localizedDescription)")
            return source.path
        }
    }

    /// 对应 C# ImageAssetService.ToMarkdownPath：绝对路径分段百分号编码。
    static func toMarkdownPath(_ path: String) -> String {
        MarkdownImagePathPolicy.absolute(path)
    }

    // MARK: - 选区导出请求

    func requestSelectionExport(completion: @escaping (Result<EditorSelectionExport, Error>) -> Void) {
        pendingSelectionExport = completion
        execute("exportSelection")
    }

    func handleSelectionExport(_ payload: [String: Any]?) {
        let export = EditorSelectionExport(
            text: payload?["text"] as? String ?? "",
            markdown: payload?["markdown"] as? String ?? "",
            html: payload?["html"] as? String ?? "")
        pendingSelectionExport?(.success(export))
        pendingSelectionExport = nil
    }
}
