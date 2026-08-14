import AppKit

extension EditorSession {
    /// 段落左侧句柄菜单（对应 Windows ParagraphBlockHandleMenu）。
    /// 菜单弹出期间由前端高亮当前块，菜单关闭后清除高亮。
    func showBlockMenu(clientX: Double, clientY: Double, position: Int) {
        guard let webView, let window = webView.window else { return }
        let pointInView = Self.editorContextMenuPoint(
            clientX: clientX,
            clientY: clientY,
            viewHeight: webView.bounds.height,
            isFlipped: webView.isFlipped
        )
        let menu = NSMenu()
        addFormatCommand(menu, L10n.t("正文"), "setParagraph")
        let headings = NSMenu(title: L10n.t("标题"))
        for level in 1...6 {
            headings.addItem(menuItem(L10n.f("%@级标题", Self.headingLevelName(level)), #selector(handleCommand(_:))))
            headings.items.last?.representedObject = "setHeading\(level)"
        }
        let headingItem = NSMenuItem(title: L10n.t("标题"), action: nil, keyEquivalent: "")
        headingItem.submenu = headings
        menu.addItem(headingItem)
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("引用"), "toggleBlockquote")
        addFormatCommand(menu, L10n.t("代码块"), "toggleCodeBlock")
        let lists = NSMenu(title: L10n.t("列表"))
        for (title, command) in [(L10n.t("无序列表"), "toggleBulletList"),
                                 (L10n.t("有序列表"), "toggleOrderedList"),
                                 (L10n.t("任务列表"), "toggleTaskList")] {
            let item = menuItem(title, #selector(handleCommand(_:)))
            item.representedObject = command
            lists.addItem(item)
        }
        let listItem = NSMenuItem(title: L10n.t("列表"), action: nil, keyEquivalent: "")
        listItem.submenu = lists
        menu.addItem(listItem)
        addFormatCommand(menu, L10n.t("水平线"), "insertHorizontalRule")
        menu.addItem(tableSizePickerMenuItem { [weak self] size in
            self?.insertTable(rows: size.rows, columns: size.columns)
        })
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("段前插入行"), "insertLineBefore")
        addFormatCommand(menu, L10n.t("段后插入行"), "insertLineAfter")

        let windowPoint = webView.convert(pointInView, to: nil)
        let screenPoint = window.convertToScreen(NSRect(origin: windowPoint, size: .zero)).origin
        menu.popUp(positioning: nil, at: screenPoint, in: nil)
        execute("clearBlockHighlight")
        _ = position
    }

    private static func headingLevelName(_ level: Int) -> String {
        switch level {
        case 1: return L10n.t("一级")
        case 2: return L10n.t("二级")
        case 3: return L10n.t("三级")
        case 4: return L10n.t("四级")
        case 5: return L10n.t("五级")
        default: return L10n.t("六级")
        }
    }

    /// 编辑器右键菜单（对应 C# OnEditorContextMenuRequested）。
    func showEditorContextMenu(
        clientX: Double,
        clientY: Double,
        canStartFormatPainter: Bool? = nil,
        formatPainterArmed: Bool? = nil
    ) {
        guard let webView, let window = webView.window else { return }
        // WKWebView 是 flipped 视图（原点在左上），因此 JS clientY 可直接映射到
        // webView 局部坐标；这里统一换算到屏幕坐标后以 in: nil 弹出，避免依赖
        // WKWebView 内部子视图的翻转语义。
        let pointInView = Self.editorContextMenuPoint(
            clientX: clientX,
            clientY: clientY,
            viewHeight: webView.bounds.height,
            isFlipped: webView.isFlipped
        )

        let menu = NSMenu()
        addFormatCommand(menu, L10n.t("粗体"), "toggleBold", "b")
        addFormatCommand(menu, L10n.t("斜体"), "toggleItalic", "i")
        addFormatCommand(menu, L10n.t("删除线"), "toggleStrike")
        addFormatCommand(menu, L10n.t("行内代码"), "toggleCode")
        let painterItem = menuItem(L10n.t("格式刷"), #selector(handleCommand(_:)))
        painterItem.representedObject = "formatPainterArm"
        let currentCanStart = canStartFormatPainter ?? self.canStartFormatPainter
        let currentArmed = formatPainterArmed ?? isFormatPainterArmed
        painterItem.isEnabled = EditorContextMenuState.formatPainterEnabled(
            isSourceMode: isSourceMode,
            canStartFormatPainter: currentCanStart,
            isFormatPainterArmed: currentArmed
        )
        painterItem.state = currentArmed ? .on : .off
        menu.addItem(painterItem)
        menu.addItem(.separator())
        let levelNames = [L10n.t("一级"), L10n.t("二级"), L10n.t("三级")]
        for level in 1...3 {
            addFormatCommand(menu, L10n.f("%@级标题", levelNames[level - 1]), "setHeading\(level)", "\(level)")
        }
        addFormatCommand(menu, L10n.t("正文"), "setParagraph")
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("无序列表"), "toggleBulletList")
        addFormatCommand(menu, L10n.t("有序列表"), "toggleOrderedList")
        addFormatCommand(menu, L10n.t("任务列表"), "toggleTaskList")
        addFormatCommand(menu, L10n.t("引用块"), "toggleBlockquote")
        addFormatCommand(menu, L10n.t("代码块"), "toggleCodeBlock")
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("水平线"), "insertHorizontalRule")
        menu.addItem(tableSizePickerMenuItem { [weak self] size in
            self?.insertTable(rows: size.rows, columns: size.columns)
        })
        menu.addItem(.separator())

        let copyAs = NSMenuItem(title: L10n.t("复制为"), action: nil, keyEquivalent: "")
        let copyAsMenu = NSMenu()
        copyAsMenu.addItem(menuItem(L10n.t("格式化"), #selector(copyFormatted(_:)), key: "c", mask: [.command, .option]))
        copyAsMenu.addItem(menuItem(L10n.t("纯文本"), #selector(copyPlain(_:)), key: "c", mask: [.command, .control]))
        copyAsMenu.addItem(menuItem("Markdown", #selector(copyMarkdown(_:))))
        copyAs.submenu = copyAsMenu
        menu.addItem(copyAs)
        menu.addItem(menuItem(L10n.t("粘贴"), #selector(pasteFromClipboardAction(_:)), key: "v"))

        // 将 WebView 局部坐标换算为屏幕坐标后，以 in: nil（屏幕坐标系）弹出。
        let windowPoint = webView.convert(pointInView, to: nil)
        let screenPoint = window.convertToScreen(NSRect(origin: windowPoint, size: .zero)).origin
        menu.popUp(positioning: nil, at: screenPoint, in: nil)
    }

    static func editorContextMenuPoint(clientX: Double, clientY: Double, viewHeight: CGFloat, isFlipped: Bool) -> NSPoint {
        NSPoint(x: clientX, y: isFlipped ? clientY : Double(viewHeight) - clientY)
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
                statusText = L10n.t("文档未保存，无法复制到 .assets 目录，图片将引用原位置")
            }
            physicalPath = url.path
        }
        let markdownPath = markdownReferencePath(for: physicalPath)
        execute("insertImage", text: markdownPath + "\n图片")
        if settings.fileImageHandling != "copyToAssets" || documentURL != nil {
            statusText = L10n.t("图片已插入文档")
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
