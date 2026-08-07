import AppKit

extension EditorSession {
    /// 编辑器右键菜单（对应 C# OnEditorContextMenuRequested）。
    func showEditorContextMenu(clientX: Double, clientY: Double) {
        guard let webView, let window = webView.window else { return }
        // JS clientY 原点在左上；AppKit 原点在左下，需翻转
        let pointInView = NSPoint(x: clientX, y: webView.bounds.height - clientY)
        let pointInWindow = webView.convert(pointInView, to: nil)
        let screenPoint = window.convertToScreen(NSRect(origin: pointInWindow, size: .zero)).origin

        let menu = NSMenu()
        addFormatCommand(menu, "粗体", "toggleBold", "b")
        addFormatCommand(menu, "斜体", "toggleItalic", "i")
        addFormatCommand(menu, "删除线", "toggleStrike")
        addFormatCommand(menu, "行内代码", "toggleCode")
        menu.addItem(.separator())
        let levelNames = ["一级", "二级", "三级"]
        for level in 1...3 {
            addFormatCommand(menu, "\(levelNames[level - 1])标题", "setHeading\(level)", "\(level)")
        }
        addFormatCommand(menu, "正文", "setParagraph")
        menu.addItem(.separator())
        addFormatCommand(menu, "无序列表", "toggleBulletList")
        addFormatCommand(menu, "有序列表", "toggleOrderedList")
        addFormatCommand(menu, "任务列表", "toggleTaskList")
        addFormatCommand(menu, "引用块", "toggleBlockquote")
        addFormatCommand(menu, "代码块", "toggleCodeBlock")
        menu.addItem(.separator())
        addFormatCommand(menu, "水平线", "insertHorizontalRule")
        addFormatCommand(menu, "插入表格", "insertTable")
        menu.addItem(.separator())

        let copyAs = NSMenuItem(title: "复制为", action: nil, keyEquivalent: "")
        let copyAsMenu = NSMenu()
        copyAsMenu.addItem(menuItem("格式化", #selector(copyFormatted(_:)), key: "c", mask: [.command, .option]))
        copyAsMenu.addItem(menuItem("纯文本", #selector(copyPlain(_:)), key: "c", mask: [.command, .control]))
        copyAsMenu.addItem(menuItem("Markdown", #selector(copyMarkdown(_:))))
        copyAs.submenu = copyAsMenu
        menu.addItem(copyAs)
        menu.addItem(menuItem("粘贴", #selector(pasteFromClipboardAction(_:)), key: "v"))

        menu.popUp(positioning: nil, at: screenPoint, in: nil)
    }

    // MARK: - 拖放图片导入（对应 C# ImportFileAsync + InsertImportedImageAsync）

    func insertImageFile(at url: URL) {
        let ext = url.pathExtension.lowercased()
        guard ["png", "jpg", "jpeg", "gif", "webp", "bmp"].contains(ext) else { return }
        let settings = SettingsService.shared.settings
        let physicalPath: String
        if settings.fileImageHandling == "copyToAssets", let docDir = documentURL?.deletingLastPathComponent() {
            physicalPath = copyImageToAssets(source: url, targetDir: docDir.appendingPathComponent("assets", isDirectory: true))
        } else {
            if settings.fileImageHandling == "copyToAssets" {
                // 文档未保存：无法复制到 .assets 目录，回退为引用原位置（对应 Windows 提示）
                statusText = "文档未保存，无法复制到 .assets 目录，图片将引用原位置"
            }
            physicalPath = url.path
        }
        let markdownPath = markdownReferencePath(for: physicalPath)
        execute("insertImage", text: markdownPath + "\n图片")
        if settings.fileImageHandling != "copyToAssets" || documentURL != nil {
            statusText = "图片已插入文档"
        }
    }

    private func addFormatCommand(_ menu: NSMenu, _ title: String, _ command: String, _ key: String = "") {
        let item = menuItem(title, #selector(handleCommand(_:)), key: key)
        item.representedObject = command
        menu.addItem(item)
    }

    private func menuItem(_ title: String, _ action: Selector, key: String = "", mask: NSEvent.ModifierFlags = [.command]) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        item.keyEquivalentModifierMask = mask
        return item
    }

    @objc func copyFormatted(_ sender: Any?) { copySelectionAs(.formatted) }
    @objc func copyPlain(_ sender: Any?) { copySelectionAs(.plainText) }
    @objc func copyMarkdown(_ sender: Any?) { copySelectionAs(.markdown) }
    @objc func pasteFromClipboardAction(_ sender: Any?) { pasteFromClipboard() }
}
