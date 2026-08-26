import AppKit
import UniformTypeIdentifiers

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
        let state = editorMenuState
        guard !state.isReadOnly else {
            execute("clearBlockHighlight")
            return
        }
        switch EditorMenuPolicy.semanticContext(for: state) {
        case .footnoteDefinition:
            addFootnoteCommands(to: menu, state: state)
        case .table:
            addTableCommands(to: menu, state: state)
        case .mermaid:
            addMermaidCommands(to: menu, state: state)
        case .codeBlock:
            addCodeBlockCommands(to: menu, state: state)
        case .image, .math:
            break
        case .ordinaryBlock:
            if !state.isReadOnly && !state.isSourceMode {
                addOrdinaryBlockHandleCommands(to: menu)
            }
        }

        guard !menu.items.isEmpty else {
            execute("clearBlockHighlight")
            return
        }

        let windowPoint = webView.convert(pointInView, to: nil)
        let screenPoint = window.convertToScreen(NSRect(origin: windowPoint, size: .zero)).origin
        menu.popUp(positioning: nil, at: screenPoint, in: nil)
        execute("clearBlockHighlight")
        _ = position
    }

    private func addOrdinaryBlockHandleCommands(to menu: NSMenu) {
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
        addFormatCommand(menu, L10n.t("插入注释"), "insertFootnote")
        menu.addItem(tableSizePickerSubmenu { [weak self] size in
            self?.insertTable(rows: size.rows, columns: size.columns)
        })
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("段前插入行"), "insertLineBefore")
        addFormatCommand(menu, L10n.t("段后插入行"), "insertLineAfter")
    }

    private static func headingLevelName(_ level: Int) -> String {
        // 与菜单栏一致：只返回数字词（“一”…“六”），由外层 “%@级标题” 拼成完整标题。
        let lang = SettingsService.shared.settings.displayLanguage
        if lang == "en" || lang == "ja" { return "\(level)" }
        return ["一", "二", "三", "四", "五", "六"][level - 1]
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
        EditorContextMenuState.preserveExplicitAvailability(in: menu)
        let state = editorMenuState
        let semanticContext = EditorMenuPolicy.semanticContext(for: state)
        if state.isSourceMode {
            // 源码模式：剪贴板 + 全选
            if state.isReadOnly {
                addEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: hasSelection)
            } else {
                addClipboardCommands(menu)
                menu.addItem(.separator())
            }
            addFormatCommand(menu, L10n.t("全选"), "selectAll")
        } else if semanticContext == .footnoteDefinition {
            addFootnoteCommands(to: menu, state: state)
        } else if semanticContext == .table {
            addTableCommands(to: menu, state: state)
        } else if semanticContext == .mermaid {
            addMermaidCommands(to: menu, state: state)
        } else if semanticContext == .image {
            // 图片：更换 / 旋转 / 缩放 / 另存为 + 标题 + 剪贴板（对应 Windows ImageContextMenu）
            guard !state.isReadOnly else { return }
            addFormatCommand(menu, L10n.t("更换图片…"), "changeImage")
            addFormatCommand(menu, L10n.t("顺时针旋转图片"), "rotateImage")
            let resize = NSMenu(title: L10n.t("调整图片大小"))
            for (title, command) in [
                (L10n.t("100%"), "resizeImage100"),
                (L10n.t("75%"), "resizeImage75"),
                (L10n.t("90%"), "resizeImage90"),
                (L10n.t("50%"), "resizeImage50"),
            ] {
                addFormatCommand(resize, title, command)
            }
            let resizeItem = NSMenuItem(title: L10n.t("调整图片大小"), action: nil, keyEquivalent: "")
            resizeItem.submenu = resize
            menu.addItem(resizeItem)
            addFormatCommand(menu, L10n.t("将图片另存为…"), "saveImageAs")
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("编辑图片标题"), "editImageCaption")
            menu.addItem(.separator())
            addClipboardCommands(menu)
        } else if semanticContext == .math {
            // 公式：编辑 / 行内块级互转 / 删除
            guard !state.isReadOnly else { return }
            addFormatCommand(menu, L10n.t("编辑公式"), "editMath")
            addFormatCommand(menu, mathBlock ? L10n.t("转为行内公式") : L10n.t("转为块级公式"), "convertMath")
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("删除公式"), "deleteMath")
        } else if semanticContext == .codeBlock {
            // 代码块：语言 / 整段复制 / 退出代码 + 剪贴板
            addCodeBlockCommands(to: menu, state: state)
            if !menu.items.isEmpty { menu.addItem(.separator()) }
            if !state.isReadOnly {
                addFormatCommand(menu, L10n.t("退出代码"), "exitCode")
                menu.addItem(.separator())
            }
            addClipboardCommands(menu)
        } else if state.isReadOnly {
            addEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: hasSelection)
            addFormatCommand(menu, L10n.t("全选"), "selectAll")
        } else {
            // 常规：标题升降级（在标题内时）+ 行内格式 + 段落/标题/列表 + 剪贴板
            if let headingLevel {
                addEnabledCommand(
                    menu,
                    L10n.t("提升标题级别"),
                    "promoteHeading",
                    enabled: headingLevel > 1)
                addEnabledCommand(
                    menu,
                    L10n.t("降低标题级别"),
                    "demoteHeading",
                    enabled: headingLevel < 6)
                menu.addItem(.separator())
            }
            addInlineFormatCommand(menu, L10n.t("粗体"), "toggleBold", "b")
            addInlineFormatCommand(menu, L10n.t("斜体"), "toggleItalic", "i")
            addInlineFormatCommand(menu, L10n.t("下划线"), "toggleUnderline", "u")
            addInlineFormatCommand(menu, L10n.t("删除线"), "toggleStrike")
            addInlineFormatCommand(menu, L10n.t("行内代码"), "toggleCode")
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
            addFormatCommand(menu, L10n.t("正文"), "setParagraph")
            let headings = NSMenu(title: L10n.t("标题"))
            for level in 1...6 {
                headings.addItem(menuItem(L10n.f("%@级标题", Self.headingLevelName(level)), #selector(handleCommand(_:))))
                headings.items.last?.representedObject = "setHeading\(level)"
            }
            let headingItem = NSMenuItem(title: L10n.t("标题"), action: nil, keyEquivalent: "")
            headingItem.submenu = headings
            menu.addItem(headingItem)
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
            addFormatCommand(menu, L10n.t("引用块"), "toggleBlockquote")
            addFormatCommand(menu, L10n.t("代码块"), "toggleCodeBlock")
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("水平线"), "insertHorizontalRule")
            menu.addItem(tableSizePickerSubmenu { [weak self] size in
                self?.insertTable(rows: size.rows, columns: size.columns)
            })
            menu.addItem(.separator())
            addClipboardCommands(menu)
        }

        guard !menu.items.isEmpty else { return }

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
        guard let markdownPath = importedImageMarkdownPath(for: url) else { return }
        execute("insertImage", text: markdownPath + "\n图片")
        let settings = SettingsService.shared.settings
        if settings.fileImageHandling != "copyToAssets" || documentURL != nil {
            statusText = L10n.t("图片已插入文档")
        }
    }

    @discardableResult
    private func importedImageMarkdownPath(for url: URL) -> String? {
        let ext = url.pathExtension.lowercased()
        guard ["png", "jpg", "jpeg", "gif", "webp", "bmp"].contains(ext) else { return nil }
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
        return markdownReferencePath(for: physicalPath)
    }

    /// 更换选中图片（对应 Windows ChangeImageAsync）。
    func changeImage() {
        guard let window = webView?.window else { return }
        let panel = NSOpenPanel()
        panel.title = L10n.t("更换图片")
        panel.allowedContentTypes = [.png, .jpeg, .gif, .tiff, (UTType(filenameExtension: "webp") ?? .png)]
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url, let self,
                  let markdownPath = self.importedImageMarkdownPath(for: url) else { return }
            self.execute("changeImage", text: markdownPath)
            self.statusText = L10n.t("图片已更换")
        }
    }

    /// 图片另存为（对应 Windows SaveImageAsAsync）。
    func saveImageAs() {
        requestSelectionExport { [weak self] result in
            guard let self, case .success(let export) = result else { return }
            guard let src = Self.extractImageSrc(from: export.markdown),
                  let sourceURL = self.resolveImagePath(src),
                  FileManager.default.fileExists(atPath: sourceURL.path) else {
                self.statusText = L10n.t("未选中图片")
                return
            }
            guard let window = self.webView?.window else { return }
            let panel = NSSavePanel()
            panel.title = L10n.t("将图片另存为")
            panel.nameFieldStringValue = sourceURL.lastPathComponent
            panel.beginSheetModal(for: window) { response in
                guard response == .OK, let url = panel.url else { return }
                do {
                    try FileManager.default.copyItem(at: sourceURL, to: url)
                    self.statusText = L10n.t("图片已另存为")
                } catch {
                    self.presentError(L10n.f("图片另存为失败：%@", error.localizedDescription))
                }
            }
        }
    }

    /// 从选区导出的 Markdown 中提取第一张图片的 src。
    static func extractImageSrc(from markdown: String) -> String? {
        guard let range = markdown.range(of: #"!\[[^\]]*\]\(([^)]+)\)"#, options: .regularExpression) else { return nil }
        let match = markdown[range]
        guard let srcRange = match.range(of: #"\(([^)]+)\)"#, options: .regularExpression) else { return nil }
        let inner = match[srcRange].dropFirst().dropLast()
        return String(inner).trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// 解析图片 src 为本地绝对路径；网络图片/无法解析时返回 nil。
    func resolveImagePath(_ src: String) -> URL? {
        let decoded = src.removingPercentEncoding ?? src
        if decoded.hasPrefix("http://") || decoded.hasPrefix("https://") {
            return nil
        }
        if decoded.hasPrefix("/") {
            return URL(fileURLWithPath: decoded)
        }
        guard let docDir = documentURL?.deletingLastPathComponent() else { return nil }
        return docDir.appendingPathComponent(decoded)
    }

    var editorMenuState: EditorContextMenuState {
        EditorContextMenuState(
            isSourceMode: isSourceMode,
            isReadOnly: isReadOnly,
            isPlainText: isPlainText,
            footnoteDefinitionLabel: footnoteDefinitionLabel,
            inTable: inTable,
            mermaidSelected: mermaidSelected,
            mermaidCount: mermaidCount,
            imageSelected: imageSelected,
            mathInline: mathInline,
            mathBlock: mathBlock,
            codeBlock: codeBlock,
            codeBlockText: codeBlockText
        )
    }

    private func addFootnoteCommands(to menu: NSMenu, state: EditorContextMenuState) {
        let commands: [(String, String, EditorNativeCommand)] = [
            (L10n.t("转到引用"), "goToFootnoteReference", .goToFootnoteReference),
            (L10n.t("重设注释编号"), "resetFootnoteLabel", .resetFootnoteNumber),
            (L10n.t("清空引用"), "clearFootnoteReferences", .clearFootnoteReferences),
            (L10n.t("删除注释"), "deleteFootnote", .deleteFootnote),
        ]
        for (title, command, nativeCommand) in commands where EditorMenuPolicy.allows(nativeCommand, state: state) {
            addFormatCommand(menu, title, command)
        }
    }

    private func addTableCommands(to menu: NSMenu, state: EditorContextMenuState) {
        guard EditorMenuPolicy.allows(.tableRows, state: state) else { return }
        addFormatCommand(menu, L10n.t("在上方添加行"), "addRowBefore")
        addFormatCommand(menu, L10n.t("在下方添加行"), "addRowAfter")
        addFormatCommand(menu, L10n.t("删除当前行"), "deleteRow")
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("在左侧添加列"), "addColumnBefore")
        addFormatCommand(menu, L10n.t("在右侧添加列"), "addColumnAfter")
        addFormatCommand(menu, L10n.t("删除当前列"), "deleteColumn")
        menu.addItem(.separator())
        let align = NSMenu(title: L10n.t("对齐"))
        addFormatCommand(align, L10n.t("左对齐"), "alignTableLeft")
        addFormatCommand(align, L10n.t("居中对齐"), "alignTableCenter")
        addFormatCommand(align, L10n.t("右对齐"), "alignTableRight")
        let alignItem = NSMenuItem(title: L10n.t("对齐"), action: nil, keyEquivalent: "")
        alignItem.submenu = align
        menu.addItem(alignItem)
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("编辑表格标题"), "editTableCaption")
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("删除表格"), "deleteTable")
    }

    private func addMermaidCommands(to menu: NSMenu, state: EditorContextMenuState) {
        let commands: [(String, String, EditorNativeCommand)] = [
            (L10n.t("编辑 Mermaid 源码"), "editMermaid", .editMermaid),
            (L10n.t("重新渲染 Mermaid 图表"), "rerenderMermaid", .rerenderMermaid),
            (L10n.t("删除 Mermaid 图表"), "deleteMermaid", .deleteMermaid),
        ]
        for (title, command, nativeCommand) in commands where EditorMenuPolicy.allows(nativeCommand, state: state) {
            addFormatCommand(menu, title, command)
        }
    }

    private func addCodeBlockCommands(to menu: NSMenu, state: EditorContextMenuState) {
        if EditorMenuPolicy.allows(.declareCodeLanguage, state: state) {
            addFormatCommand(menu, L10n.t("声明代码语言…"), "declareCodeLanguage")
        }
        if EditorMenuPolicy.allows(.copyCodeBlock, state: state) {
            addFormatCommand(menu, L10n.t("复制整段代码"), "copyCodeBlock")
        }
    }

    private func addFormatCommand(_ menu: NSMenu, _ title: String, _ command: String, _ key: String = "") {
        let item = menuItem(title, #selector(handleCommand(_:)), key: key)
        item.representedObject = command
        menu.addItem(item)
    }

    private func addInlineFormatCommand(_ menu: NSMenu, _ title: String, _ command: String, _ key: String = "") {
        let item = menuItem(title, #selector(handleCommand(_:)), key: key)
        item.representedObject = command
        item.isEnabled = EditorMenuPolicy.isInlineFormatCommandEnabled(
            command: command,
            hasSelection: hasSelection,
            isSourceMode: isSourceMode,
            isReadOnly: isReadOnly
        )
        menu.addItem(item)
    }

    private func menuItem(_ title: String, _ action: Selector, key: String = "", mask: NSEvent.ModifierFlags = [.command]) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        item.keyEquivalentModifierMask = mask
        return item
    }

    /// 统一的剪贴板区块：剪切/拷贝/粘贴/粘贴为纯文本 + “复制为 ▸ 纯文本 / Markdown”。
    private func addClipboardCommands(_ menu: NSMenu) {
        let canCut = EditorMenuPolicy.isEnabled(
            command: "cut", hasSelection: hasSelection,
            clipboardHasContent: clipboardHasContent, isReadOnly: isReadOnly
        )
        let canCopy = EditorMenuPolicy.isEnabled(
            command: "copy", hasSelection: hasSelection,
            clipboardHasContent: clipboardHasContent, isReadOnly: isReadOnly
        )
        let canPaste = EditorMenuPolicy.isEnabled(
            command: "paste", hasSelection: hasSelection,
            clipboardHasContent: clipboardHasContent, isReadOnly: isReadOnly
        )
        let canCopyAs = EditorMenuPolicy.isEnabled(
            command: "copyAs", hasSelection: hasSelection,
            clipboardHasContent: clipboardHasContent, isReadOnly: isReadOnly
        )
        addEnabledCommand(menu, L10n.t("剪切"), "cut", enabled: canCut)
        addEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: canCopy)
        addEnabledCommand(menu, L10n.t("粘贴"), "paste", enabled: canPaste)
        addEnabledCommand(menu, L10n.t("粘贴为纯文本"), "pastePlainText", enabled: canPaste)
        let copyAs = NSMenuItem(title: L10n.t("复制为"), action: nil, keyEquivalent: "")
        copyAs.isEnabled = canCopyAs
        let copyAsMenu = NSMenu()
        let plain = menuItem(L10n.t("纯文本"), #selector(copyPlain(_:)))
        plain.isEnabled = canCopyAs
        copyAsMenu.addItem(plain)
        let markdown = menuItem(L10n.t("Markdown"), #selector(copyMarkdown(_:)))
        markdown.isEnabled = canCopyAs
        copyAsMenu.addItem(markdown)
        copyAs.submenu = copyAsMenu
        menu.addItem(copyAs)
    }

    private func addEnabledCommand(_ menu: NSMenu, _ title: String, _ command: String, enabled: Bool) {
        let item = menuItem(title, #selector(handleCommand(_:)))
        item.representedObject = command
        item.isEnabled = enabled
        menu.addItem(item)
    }

    @objc func copyFormatted(_ sender: Any?) { copySelectionAs(.formatted) }
    @objc func copyPlain(_ sender: Any?) { copySelectionAs(.plainText) }
    @objc func copyMarkdown(_ sender: Any?) { copySelectionAs(.markdown) }
    @objc func pasteFromClipboardAction(_ sender: Any?) { pasteFromClipboard() }
    @objc func pastePlainTextFromClipboardAction(_ sender: Any?) { pastePlainTextFromClipboard() }
}
