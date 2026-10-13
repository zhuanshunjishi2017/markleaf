import AppKit
import UniformTypeIdentifiers
import WebKit

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
        EditorContextMenuState.preserveExplicitAvailability(in: menu)
        let state = editorMenuState
        guard !state.isReadOnly else {
            execute("clearBlockHighlight")
            return
        }
        switch EditorMenuPolicy.semanticContext(for: state) {
        case .footnoteDefinition:
            addFootnoteCommands(to: menu, state: state)
        case .table:
            addTableCommands(to: menu, state: state, includeClipboard: false)
        default:
            // 对齐 Windows：句柄菜单除脚注/表格外一律使用段落命令全集。
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
        // 顺序对齐 Windows NativeMenuService.BuildBlockHandleMenu()。
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
        addFormatCommand(menu, L10n.t("段间公式"), "insertMathBlock")
        addFormatCommand(menu, L10n.t("代码块"), "toggleCodeBlock")
        addFormatCommand(menu, L10n.t("水平线"), "insertHorizontalRule")
        let alerts = NSMenu(title: L10n.t("提示框"))
        for (title, command) in [(L10n.t("备注"), "insertAlertNote"),
                                 (L10n.t("提示"), "insertAlertTip"),
                                 (L10n.t("重要"), "insertAlertImportant"),
                                 (L10n.t("警告"), "insertAlertWarning"),
                                 (L10n.t("注意"), "insertAlertCaution")] {
            addFormatCommand(alerts, title, command)
        }
        menu.addItem(submenuItem(L10n.t("提示框"), alerts))
        menu.addItem(.separator())
        menu.addItem(tableSizePickerSubmenu { [weak self] size in
            self?.insertTable(rows: size.rows, columns: size.columns)
        })
        let lists = NSMenu(title: L10n.t("列表"))
        for (title, command) in [(L10n.t("无序列表"), "toggleBulletList"),
                                 (L10n.t("有序列表"), "toggleOrderedList"),
                                 (L10n.t("任务列表"), "toggleTaskList")] {
            let item = menuItem(title, #selector(handleCommand(_:)))
            item.representedObject = command
            lists.addItem(item)
        }
        lists.addItem(.separator())
        addFormatCommand(lists, L10n.t("增加列表缩进"), "indentListItem")
        addFormatCommand(lists, L10n.t("减少列表缩进"), "outdentListItem")
        menu.addItem(submenuItem(L10n.t("列表"), lists))
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("插入注释"), "insertFootnote")
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("段前插入行"), "insertLineBefore")
        addFormatCommand(menu, L10n.t("段后插入行"), "insertLineAfter")
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("重复该段"), "duplicateParagraph")
        addFormatCommand(menu, L10n.t("删除该段"), "deleteParagraph")
    }

    private static func headingLevelName(_ level: Int) -> String {
        // 与菜单栏一致：只返回数字词（“一”…“六”），由外层 “%@级标题” 拼成完整标题。
        let lang = SettingsService.shared.settings.displayLanguage
        if lang == "en" || lang == "ja" { return "\(level)" }
        return ["一", "二", "三", "四", "五", "六"][level - 1]
    }

    /// 编辑器右键菜单（对应 C# OnEditorContextMenuRequested）。
    /// 快捷格式行与普通菜单项共用同一个 NSMenu 和菜单跟踪生命周期。
    func showEditorContextMenu(
        clientX: Double,
        clientY: Double,
        canStartFormatPainter: Bool? = nil,
        formatPainterArmed: Bool? = nil
    ) {
        guard let webView, let window = webView.window else { return }
        activeNativeFormatMenuSession?.finish()
        if editorMenuState.expandedSource {
            showExpandedSourceContextMenu(clientX: clientX, clientY: clientY)
            return
        }
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
        let session = NativeFormatMenuSession(owner: self, menu: menu)
        if !state.isSourceMode && !state.isReadOnly && state.actions["toggleBold"]?.enabled == true {
            let toolbar = NativeFormatToolbarView(
                actions: state.actions,
                canStartFormatPainter: canStartFormatPainter,
                formatPainterArmed: formatPainterArmed
            )
            toolbar.onPreview = { [weak session] command in session?.preview(command) }
            toolbar.onCommand = { [weak session] command in session?.commit(command) }
            let item = NSMenuItem(title: "", action: nil, keyEquivalent: "")
            item.view = toolbar
            menu.addItem(item)
            menu.addItem(.separator())
        }
        if state.isSourceMode {
            // 源码模式：历史 + 剪贴板 + 全选（对齐 Windows 源码分支 + 历史区）。
            if state.isReadOnly {
                addClipboardCommands(menu)
            } else {
                addHistoryCommands(menu)
                menu.addItem(.separator())
                addClipboardCommands(menu, includeCopyAs: false)
            }
        } else if semanticContext == .footnoteDefinition {
            addFootnoteCommands(to: menu, state: state)
            menu.addItem(.separator())
            addClipboardCommands(menu)
        } else if semanticContext == .frontMatter {
            addCodeContextMenu(
                menu,
                state: state,
                includeLanguage: false,
                includeFormat: false)
        } else if semanticContext == .table {
            addTableCommands(to: menu, state: state, includeClipboard: true)
        } else if semanticContext == .mermaid {
            addMermaidCommands(to: menu, state: state)
        } else if semanticContext == .image {
            // 顺序对齐 Windows ImageContextMenu：更换 / 标题 / 缩放 / 旋转 / 另存为 + 剪贴板。
            addFormatCommand(menu, L10n.t("更换图片…"), "changeImage")
            addFormatCommand(menu, L10n.t("编辑图片标题"), "editImageCaption")
            let resize = NSMenu(title: L10n.t("调整图片大小"))
            for (title, command) in [
                (L10n.t("100%"), "resizeImage100"),
                (L10n.t("75%"), "resizeImage75"),
                (L10n.t("90%"), "resizeImage90"),
                (L10n.t("50%"), "resizeImage50"),
            ] {
                addFormatCommand(resize, title, command)
            }
            menu.addItem(submenuItem(L10n.t("调整图片大小"), resize))
            addFormatCommand(menu, L10n.t("顺时针旋转图片"), "rotateImage")
            addFormatCommand(menu, L10n.t("将图片另存为…"), "saveImageAs")
            menu.addItem(.separator())
            addClipboardCommands(menu, includeCopyAs: false)
        } else if semanticContext == .math {
            // 公式：编辑 / 行内块级互转 / 删除
            addFormatCommand(menu, L10n.t("编辑公式源码"), "editMath")
            addFormatCommand(menu, L10n.t("公式编号…"), "setMathNumber")
            addFormatCommand(menu, mathBlock ? L10n.t("转为行内公式") : L10n.t("转为块级公式"), "convertMath")
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("删除公式"), "deleteMath")
        } else if semanticContext == .codeBlock {
            addCodeContextMenu(
                menu,
                state: state,
                includeLanguage: true,
                includeFormat: editorCommandEnabled("formatCodeBlock"))
        } else if state.isReadOnly {
            addClipboardCommands(menu)
        } else {
            if editorCommandEnabled("normalizeInlineCode") {
                addFormatCommand(
                    menu,
                    L10n.t("整理行内代码"),
                    "normalizeInlineCode",
                    "f",
                    [.shift, .option]
                )
                menu.addItem(.separator())
            }
            addHistoryCommands(menu)
            menu.addItem(.separator())
            addClipboardCommands(menu)
            menu.addItem(.separator())

            addFormatCommand(menu, L10n.t("行内代码"), "toggleCode")
            addFormatCommand(menu, L10n.t("行内公式"), "insertMathInline")
            addFormatCommand(menu, L10n.t("插入超链接…"), "insertLink")
            let images = NSMenu(title: L10n.t("插入图片"))
            addFormatCommand(images, L10n.t("插入本地图片…"), "insertImage")
            addFormatCommand(images, L10n.t("插入来自互联网的图片…"), "insertImageFromUrl")
            menu.addItem(submenuItem(L10n.t("插入图片"), images))
            menu.addItem(.separator())

            let paragraph = NSMenu(title: L10n.t("段落"))
            addOrdinaryBlockHandleCommands(to: paragraph)
            menu.addItem(submenuItem(L10n.t("段落"), paragraph))
        }

        guard !menu.items.isEmpty else { return }

        // 将 WebView 局部坐标换算为屏幕坐标后，以 in: nil（屏幕坐标系）弹出。
        let windowPoint = webView.convert(pointInView, to: nil)
        let screenPoint = window.convertToScreen(NSRect(origin: windowPoint, size: .zero)).origin
        activeNativeFormatMenuSession = session
        menu.popUp(positioning: nil, at: screenPoint, in: nil)
        // menuDidClose 通常会先于 popUp 返回；这里作为异常路径兜底，finish 是幂等的。
        session.finish()
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
            codeBlockText: codeBlockText,
            frontMatterActive: frontMatterActive,
            expandedSource: expandedSourceActive,
            actions: editorActions,
            semanticContext: editorSemanticContext
        )
    }

    /// 公式/Mermaid 浮层中的右键菜单只作用于浮层源码。
    private func showExpandedSourceContextMenu(clientX: Double, clientY: Double) {
        guard let webView, let window = webView.window else { return }
        activeNativeFormatMenuSession?.finish()
        let menu = NSMenu()
        EditorContextMenuState.preserveExplicitAvailability(in: menu)
        // 对齐 Windows BuildExpandedSourceContextMenu：拷贝 / 粘贴 / 剪切 ─ 全选。
        for (title, command) in [
            (L10n.t("拷贝"), "copy"),
            (L10n.t("粘贴"), "paste"),
            (L10n.t("剪切"), "cut"),
        ] {
            addFormatCommand(menu, title, command)
        }
        menu.addItem(.separator())
        addFormatCommand(menu, L10n.t("全选"), "selectAll")

        let pointInView = Self.editorContextMenuPoint(
            clientX: clientX,
            clientY: clientY,
            viewHeight: webView.bounds.height,
            isFlipped: webView.isFlipped
        )
        let windowPoint = webView.convert(pointInView, to: nil)
        let screenPoint = window.convertToScreen(NSRect(origin: windowPoint, size: .zero)).origin
        let session = NativeFormatMenuSession(owner: self, menu: menu)
        activeNativeFormatMenuSession = session
        menu.popUp(positioning: nil, at: screenPoint, in: nil)
        session.finish()
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

    /// 表格命令组（对齐 Windows AppendTableCommands）。
    /// 右键菜单携带剪贴板（includeClipboard: true）；段落句柄菜单不带。
    private func addTableCommands(to menu: NSMenu, state: EditorContextMenuState, includeClipboard: Bool) {
        guard EditorMenuPolicy.allows(.tableRows, state: state) else { return }
        addFormatCommand(menu, L10n.t("编辑表格标题"), "editTableCaption")
        menu.addItem(.separator())
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
        if includeClipboard {
            menu.addItem(.separator())
            addEnabledCommand(menu, L10n.t("剪切"), "cut", enabled: editorCommandEnabled("cut"))
            addEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: editorCommandEnabled("copy"))
            addEnabledCommand(menu, L10n.t("粘贴"), "paste", enabled: editorCommandEnabled("paste"))
        }
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

    /// 代码块/前置元数据命令组（对齐 Windows AppendCodeContextMenu）：
    /// [声明代码语言] [格式化代码块] 复制整段代码 ─ 退出代码 ─ 剪切/拷贝/粘贴。
    private func addCodeContextMenu(
        _ menu: NSMenu,
        state: EditorContextMenuState,
        includeLanguage: Bool,
        includeFormat: Bool
    ) {
        if includeLanguage, EditorMenuPolicy.allows(.declareCodeLanguage, state: state) {
            addFormatCommand(menu, L10n.t("声明代码语言…"), "declareCodeLanguage")
        }
        if includeFormat {
            let formatItem = menuItem(
                L10n.t("格式化代码块"),
                #selector(handleCommand(_:)),
                key: "f",
                mask: [.shift, .option]
            )
            formatItem.representedObject = "formatCodeBlock"
            formatItem.isEnabled = editorCommandEnabled("formatCodeBlock")
            applyShortcut(to: formatItem, command: "formatCodeBlock")
            menu.addItem(formatItem)
        }
        if EditorMenuPolicy.allows(.copyCodeBlock, state: state) {
            addFormatCommand(menu, L10n.t("复制整段代码"), "copyCodeBlock")
        }
        menu.addItem(.separator())
        if !state.isReadOnly {
            addFormatCommand(menu, L10n.t("退出代码"), "exitCode")
            menu.addItem(.separator())
        }
        addClipboardCommands(menu, includeCopyAs: false, includeSelectAll: false)
    }

    private func addFormatCommand(
        _ menu: NSMenu,
        _ title: String,
        _ command: String,
        _ key: String = "",
        _ mask: NSEvent.ModifierFlags = [.command]
    ) {
        let item = menuItem(title, #selector(handleCommand(_:)), key: key, mask: mask)
        item.representedObject = command
        item.isEnabled = editorCommandEnabled(command)
        applyShortcut(to: item, command: command)
        menu.addItem(item)
    }

    private func menuItem(_ title: String, _ action: Selector, key: String = "", mask: NSEvent.ModifierFlags = [.command]) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        item.keyEquivalentModifierMask = mask
        return item
    }

    private func submenuItem(_ title: String, _ submenu: NSMenu) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.submenu = submenu
        return item
    }

    /// 统一的剪贴板区块（对齐 Windows AppendReadOnlyContextMenu 形态）：
    /// 剪切/拷贝/粘贴 [includeCopyAs] 复制为 ▸ Markdown / 纯文本 / HTML ─ 粘贴为纯文本 [includeSelectAll] ─ 全选。
    /// 表格/图片/代码块等上下文传 false 以只保留剪切/拷贝/粘贴。
    private func addClipboardCommands(_ menu: NSMenu, includeCopyAs: Bool = true, includeSelectAll: Bool = true) {
        let canCut = editorCommandEnabled("cut")
        let canCopy = editorCommandEnabled("copy")
        let canPaste = editorCommandEnabled("paste")
        addEnabledCommand(menu, L10n.t("剪切"), "cut", enabled: canCut)
        addEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: canCopy)
        if includeCopyAs {
            let canCopyAs = editorCommandEnabled("copyAs")
            let copyAs = NSMenuItem(title: L10n.t("复制为"), action: nil, keyEquivalent: "")
            copyAs.isEnabled = canCopyAs
            let copyAsMenu = NSMenu()
            let markdown = menuItem(L10n.t("Markdown"), #selector(copyMarkdown(_:)))
            markdown.isEnabled = canCopyAs
            copyAsMenu.addItem(markdown)
            let plain = menuItem(L10n.t("纯文本"), #selector(copyPlain(_:)))
            plain.isEnabled = canCopyAs
            copyAsMenu.addItem(plain)
            let html = menuItem(L10n.t("HTML"), #selector(handleCommand(_:)))
            html.representedObject = "copyHtml"
            html.isEnabled = editorCommandEnabled("copyHtml")
            copyAsMenu.addItem(html)
            copyAsMenu.addItem(.separator())
            let pastePlain = menuItem(L10n.t("粘贴为纯文本"), #selector(pastePlainTextFromClipboardAction(_:)))
            pastePlain.representedObject = "pastePlainText"
            pastePlain.isEnabled = canPaste
            applyShortcut(to: pastePlain, command: "pastePlainText")
            copyAsMenu.addItem(pastePlain)
            copyAs.submenu = copyAsMenu
            menu.addItem(copyAs)
        }
        addEnabledCommand(menu, L10n.t("粘贴"), "paste", enabled: canPaste)
        if includeSelectAll {
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("全选"), "selectAll")
        }
    }

    /// 撤销/重做区块：与“编辑”菜单共用同一组命令和快捷键。
    private func addHistoryCommands(_ menu: NSMenu) {
        addEnabledCommand(menu, L10n.t("撤销"), "undo", enabled: editorCommandEnabled("undo"))
        addEnabledCommand(menu, L10n.t("重做"), "redo", enabled: editorCommandEnabled("redo"))
    }

    private func addEnabledCommand(_ menu: NSMenu, _ title: String, _ command: String, enabled: Bool) {
        let item = menuItem(title, #selector(handleCommand(_:)))
        item.representedObject = command
        applyShortcut(to: item, command: command)
        item.isEnabled = enabled
        menu.addItem(item)
    }

    private func applyShortcut(to item: NSMenuItem, command: String) {
        guard let entry = ShortcutCatalog.entry(for: command),
              let effective = ShortcutSettings.shared.effectiveKey(for: entry) else { return }
        item.keyEquivalent = effective.0
        item.keyEquivalentModifierMask = effective.1
    }

    @objc func copyFormatted(_ sender: Any?) { copySelectionAs(.formatted) }
    @objc func copyPlain(_ sender: Any?) { copySelectionAs(.plainText) }
    @objc func copyMarkdown(_ sender: Any?) { copySelectionAs(.markdown) }
    @objc func copyHtml(_ sender: Any?) { copySelectionAs(.html) }
    @objc func pasteFromClipboardAction(_ sender: Any?) { pasteFromClipboard() }
    @objc func pastePlainTextFromClipboardAction(_ sender: Any?) { pastePlainTextFromClipboard() }
}

/// 菜单内按钮与普通菜单项共用 AppKit 的跟踪循环，不再监听网页浮层坐标。
final class NativeFormatMenuSession: NSObject, NSMenuDelegate {
    private weak var owner: EditorSession?
    private weak var menu: NSMenu?
    private var previewCommand: String?
    private var didFinish = false

    init(owner: EditorSession, menu: NSMenu) {
        self.owner = owner
        self.menu = menu
        super.init()
        menu.delegate = self
    }

    func preview(_ command: String?) {
        guard !didFinish, previewCommand != command else { return }
        previewCommand = command
        owner?.execute("previewFormat", text: command)
    }

    func commit(_ command: String) {
        guard !didFinish else { return }
        preview(nil)
        // 先提交命令，再结束菜单会话；前端命令路径负责恢复原选区。
        owner?.execute(command)
        menu?.cancelTracking()
    }

    func menu(_ menu: NSMenu, willHighlight item: NSMenuItem?) {
        // 离开快捷行进入普通项/子菜单后，立即撤销临时预览。
        // 自定义菜单行不对应普通高亮项，AppKit 会发出 nil；不能因此
        // 撤销按钮刚刚发出的悬停预览。离开按钮由它自己的 mouseExited 负责。
        if let item, !(item.view is NativeFormatToolbarView) { preview(nil) }
    }

    func menuDidClose(_ menu: NSMenu) {
        finish()
    }

    func finish() {
        guard !didFinish else { return }
        didFinish = true
        owner?.execute("endContextMenu")
        if owner?.activeNativeFormatMenuSession === self {
            owner?.activeNativeFormatMenuSession = nil
        }
    }

}
