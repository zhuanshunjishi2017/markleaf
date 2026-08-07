import AppKit
import UniformTypeIdentifiers

extension EditorSession {
    enum ClipboardCopyMode {
        case formatted
        case markdown
        case plainText
    }

    // MARK: - 复制（对应 C# ExecuteClipboardCopyAsync）

    func copySelectionAs(_ mode: ClipboardCopyMode) {
        requestSelectionExport { [weak self] result in
            DispatchQueue.main.async {
                guard let self else { return }
                switch result {
                case .success(let selection):
                    let text = mode == .markdown ? selection.markdown : selection.text
                    guard !text.isEmpty || !selection.html.isEmpty else {
                        self.statusText = "当前没有可复制的文本"
                        return
                    }
                    let pasteboard = NSPasteboard.general
                    pasteboard.clearContents()
                    pasteboard.setString(text, forType: .string)
                    if mode == .formatted && !selection.html.isEmpty {
                        // macOS 剪贴板 HTML 类型（对应 Windows CF_HTML）
                        pasteboard.setString(selection.html, forType: .html)
                    }
                    self.statusText = mode == .formatted ? "已复制格式化内容" : "已复制"
                case .failure:
                    self.statusText = "剪贴板操作失败"
                }
            }
        }
    }

    // MARK: - 粘贴（对应 C# PasteClipboardContentAsync）

    func pasteFromClipboard() {
        let pasteboard = NSPasteboard.general

        // 0) Finder 复制的文件（对应 Windows Clipboard.ContainsFileDropList）→ 按「文件图片」设置导入
        if let urls = pasteboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL],
           !urls.isEmpty {
            let imageExtensions = Set(["png", "jpg", "jpeg", "gif", "webp", "bmp"])
            var imported = 0
            for url in urls where imageExtensions.contains(url.pathExtension.lowercased()) {
                insertImageFile(at: url)
                imported += 1
            }
            statusText = imported > 0 ? "已插入 \(imported) 张图片" : "未找到可插入的图片"
            return
        }

        // 1) 图片 → 保存到本地并插入
        if let image = NSImage(pasteboard: pasteboard) {
            importClipboardImage(image)
            return
        }

        // 2) HTML 格式化粘贴（可视化模式）
        if !isSourceMode, let html = pasteboard.string(forType: .html), !html.isEmpty {
            execute("pasteHtml", text: html)
            statusText = "已粘贴格式化内容"
            return
        }

        // 3) 纯文本
        if let text = pasteboard.string(forType: .string), !text.isEmpty {
            execute("pasteText", text: text)
            statusText = "已粘贴纯文本"
            return
        }
        statusText = "剪贴板中没有可粘贴的内容"
    }

    /// 剪贴板图片 → 保存到图片目录 → insertImage（对应 C# ImportClipboardBitmapAsync）。
    private func importClipboardImage(_ image: NSImage) {
        guard let tiff = image.tiffRepresentation,
              let rep = NSBitmapImageRep(data: tiff),
              let png = rep.representation(using: .png, properties: [:]) else {
            statusText = "无法读取剪贴板图片"
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
            statusText = "图片已插入文档"
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
            statusText = "文档未保存，无法复制到 .assets 目录，图片已保存到默认目录"
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
        if settings.useRelativePaths, let docDir = documentURL?.deletingLastPathComponent().path {
            let absolute = URL(fileURLWithPath: path).standardizedFileURL.path
            let docDirAbs = URL(fileURLWithPath: docDir).standardizedFileURL.path
            if absolute.hasPrefix(docDirAbs + "/") {
                var relative = String(absolute.dropFirst(docDirAbs.count + 1))
                if settings.prefixRelativeWithDotSlash {
                    relative = "./" + relative
                }
                return EditorSession.encodeMarkdownPath(relative)
            }
        }
        return EditorSession.toMarkdownPath(path)
    }

    static func encodeMarkdownPath(_ path: String) -> String {
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-._~"))
        return path.split(separator: "/").map {
            $0.addingPercentEncoding(withAllowedCharacters: allowed) ?? String($0)
        }.joined(separator: "/")
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
        let full = URL(fileURLWithPath: path).standardizedFileURL.path
        let allowed = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-._~"))
        let segments = full.split(separator: "/").map { String($0) }
        let escaped = segments.map { segment -> String in
            if segment.hasSuffix(":") { return segment }
            return segment.addingPercentEncoding(withAllowedCharacters: allowed) ?? segment
        }
        return "/" + escaped.joined(separator: "/")
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
