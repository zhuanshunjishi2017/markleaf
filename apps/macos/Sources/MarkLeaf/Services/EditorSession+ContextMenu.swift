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
        case .frontMatter:
            addCodeContextMenu(
                menu,
                state: state,
                includeLanguage: false,
                includeFormat: false)
        case .codeBlock:
            addCodeContextMenu(
                menu,
                state: state,
                includeLanguage: true,
                includeFormat: true)
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
        addFormatCommand(menu, L10n.t("块级公式"), "insertMathBlock")
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
        addFormatCommand(menu, L10n.t("插入脚注"), "insertFootnote")
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

        let (menu, session) = buildEditorContextMenu(canStartFormatPainter: canStartFormatPainter, formatPainterArmed: formatPainterArmed)
        guard !menu.items.isEmpty else { session.finish(); return }
        let windowPoint = webView.convert(pointInView, to: nil)
        let screenPoint = window.convertToScreen(NSRect(origin: windowPoint, size: .zero)).origin
        activeNativeFormatMenuSession = session
        menu.popUp(positioning: nil, at: screenPoint, in: nil)
        session.finish()
    }

    func buildEditorContextMenu(canStartFormatPainter: Bool? = nil, formatPainterArmed: Bool? = nil) -> (NSMenu, NativeFormatMenuSession) {
        let menu = NSMenu()
        EditorContextMenuState.preserveExplicitAvailability(in: menu)
        let state = editorMenuState
        let semanticContext = EditorMenuPolicy.semanticContext(for: state)
        let session = NativeFormatMenuSession(owner: self, menu: menu)
        let inlineCommonActions = SettingsService.shared.settings.inlineCommonActions
        if inlineCommonActions && !state.isSourceMode && !state.isReadOnly && state.actions["toggleBold"]?.enabled == true {
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
                (L10n.t("50%"), "resizeImage50"),
                (L10n.t("75%"), "resizeImage75"),
                (L10n.t("90%"), "resizeImage90"),
                (L10n.t("100%"), "resizeImage100"),
            ] {
                addFormatCommand(resize, title, command)
            }
            menu.addItem(submenuItem(L10n.t("调整图片大小"), resize))
            addFormatCommand(menu, L10n.t("顺时针旋转图片"), "rotateImage")
            addFormatCommand(menu, L10n.t("将图片另存为…"), "saveImageAs")
            menu.addItem(.separator())
            addClipboardCommands(menu, includeCopyAs: false)
        } else if semanticContext == .math {
            if state.isReadOnly {
                addFormatCommand(menu, L10n.t("查看公式源码"), "viewMathSource")
                addEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: editorCommandEnabled("copy"))
            } else {
            // 公式：编辑 / 行内块级互转 / 删除
            addFormatCommand(menu, L10n.t("编辑公式源码"), "editMath")
            if mathBlock { addFormatCommand(menu, L10n.t("公式编号…"), "setMathNumber") }
            addFormatCommand(menu, mathBlock ? L10n.t("转为行内公式") : L10n.t("转为块级公式"), "convertMath")
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("删除公式"), "deleteMath")
            }
        } else if semanticContext == .codeBlock {
            addCodeContextMenu(
                menu,
                state: state,
                includeLanguage: true,
                includeFormat: true)
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

        if linkHref != nil {
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("打开链接"), "openLink")
            addFormatCommand(menu, L10n.t("复制链接地址"), "copyLinkAddress")
            if !state.isReadOnly {
                addFormatCommand(menu, L10n.t("编辑链接…"), "editLink")
                addFormatCommand(menu, L10n.t("移除链接"), "removeLink")
            }
        }
        if footnoteReferenceLabel != nil {
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("转到脚注正文"), "goToFootnoteDefinition")
            if !state.isReadOnly { addFormatCommand(menu, L10n.t("删除此引用"), "deleteFootnoteReference") }
        }
        if editorCommandEnabled("cancelAlert") {
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("取消提示块"), "cancelAlert")
        }

        // Keep formatting available for editable table/inline contexts too, not
        // only ordinary paragraphs, when replacing the quick row with text items.
        if !inlineCommonActions && !state.isSourceMode && !state.isReadOnly
            && (semanticContext == .ordinaryBlock || state.actions["toggleBold"]?.enabled == true) {
            NativeFormatTextMenu.append(
                to: menu, actions: state.actions,
                canStartFormatPainter: canStartFormatPainter,
                formatPainterArmed: formatPainterArmed ?? isFormatPainterArmed
            ) { title, command in
                let item = self.menuItem(title, #selector(self.handleCommand(_:)))
                self.applyShortcut(to: item, command: command)
                return item
            }
        }

        return (menu, session)
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
        guard ImageImportPolicy.supports(url) else {
            statusText = L10n.t("不支持此图片格式")
            return nil
        }
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
        panel.allowedContentTypes = ImageImportPolicy.contentTypes
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
        guard imageSelected, let source = imageSource, let window = webView?.window else { return }
        let identity = currentDocumentIdentifier
        let sourceDocument = documentURL
        Task { [weak self] in
            do {
                let image = try await ImageOriginalExportService.read(source: source, documentURL: sourceDocument)
                guard let self, self.currentDocumentIdentifier == identity, self.imageSource == source else { return }
                let panel = NSSavePanel()
                panel.title = L10n.t("将图片另存为")
                panel.allowedContentTypes = [UTType(filenameExtension: image.fileExtension) ?? .image]
                let stem = URL(string: source)?.deletingPathExtension().lastPathComponent ?? "image"
                panel.nameFieldStringValue = "\(stem.isEmpty ? "image" : stem).\(image.fileExtension)"
                panel.beginSheetModal(for: window) { response in
                    guard response == .OK, let url = panel.url,
                          self.currentDocumentIdentifier == identity else { return }
                    do {
                        try ImageOriginalExportService.write(image, to: url)
                        self.statusText = L10n.t("图片已另存为")
                    } catch {
                        self.presentError(L10n.f("图片另存为失败：%@", error.localizedDescription))
                    }
                }
            } catch is CancellationError { }
            catch { self?.presentError(L10n.f("图片另存为失败：%@", error.localizedDescription)) }
        }
    }

    func saveMermaidAs() {
        guard mermaidSelected, mermaidRendered, let window = webView?.window else { return }
        let identity = currentDocumentIdentifier
        let sourceRevision = currentRevision
        let panel = NSSavePanel()
        panel.title = L10n.t("将图表另存为图片")
        panel.nameFieldStringValue = "diagram.png"
        panel.allowedContentTypes = [.png, .svg]
        let format = NSPopUpButton(frame: .zero, pullsDown: false)
        format.addItems(withTitles: ["PNG (2×)", "SVG"])
        format.target = self
        format.action = #selector(mermaidExportFormatChanged(_:))
        panel.accessoryView = NSStackView(views: [NSTextField(labelWithString: L10n.t("格式")), format])
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url, let self,
                  self.currentDocumentIdentifier == identity, self.currentRevision == sourceRevision else { return }
            let extensionName = format.indexOfSelectedItem == 1 ? "svg" : "png"
            // Do not silently write a different format under a confirmed filename.
            guard url.pathExtension.lowercased() == extensionName else {
                self.presentError(L10n.t("文件扩展名与所选图片格式不一致，请重新导出")); return
            }
            self.requestMermaidImageExport(format: extensionName) { [weak self] result in
                guard let self, self.currentDocumentIdentifier == identity, self.currentRevision == sourceRevision else { return }
                do {
                    let image = try result.get()
                    try image.data.write(to: url, options: .atomic)
                    self.statusText = L10n.t("图片已另存为")
                } catch let error as CocoaError where error.code == .userCancelled { }
                catch { self.presentError(error.localizedDescription) }
            }
        }
    }

    @objc private func mermaidExportFormatChanged(_ sender: NSPopUpButton) {
        guard let panel = sender.window as? NSSavePanel else { return }
        let extensionName = sender.indexOfSelectedItem == 1 ? "svg" : "png"
        panel.allowedContentTypes = extensionName == "svg" ? [.svg] : [.png]
        let currentName = panel.nameFieldStringValue
        let stem = URL(fileURLWithPath: currentName).deletingPathExtension().lastPathComponent
        panel.nameFieldStringValue = "\(stem.isEmpty ? "diagram" : stem).\(extensionName)"
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
            (L10n.t("更改脚注标识…"), "resetFootnoteLabel", .resetFootnoteNumber),
            (L10n.t("删除全部引用"), "clearFootnoteReferences", .clearFootnoteReferences),
            (L10n.t("删除脚注及全部引用"), "deleteFootnote", .deleteFootnote),
        ]
        for (title, command, nativeCommand) in commands where EditorMenuPolicy.allows(nativeCommand, state: state) {
            addFormatCommand(menu, title, command)
        }
    }

    /// 表格命令组（对齐 Windows AppendTableCommands）。
    /// 右键菜单携带剪贴板（includeClipboard: true）；段落句柄菜单不带。
    private func addTableCommands(to menu: NSMenu, state: EditorContextMenuState, includeClipboard: Bool) {
        guard state.inTable else { return }
        if state.isReadOnly {
            addClipboardCommands(menu, includeCopyAs: true, includeSelectAll: true)
            return
        }
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
        addFormatCommand(menu, L10n.t(state.isReadOnly ? "查看图表源码" : "编辑 Mermaid 源码"), state.isReadOnly ? "viewMermaidSource" : "editMermaid")
        if state.isReadOnly {
            addEnabledCommand(menu, L10n.t("拷贝"), "copy", enabled: editorCommandEnabled("copy"))
        }
        addFormatCommand(menu, L10n.t("重新渲染 Mermaid 图表"), "rerenderMermaid")
        addFormatCommand(menu, L10n.t("将图表另存为图片"), "saveMermaidAs")
        if !state.isReadOnly {
            menu.addItem(.separator())
            addFormatCommand(menu, L10n.t("删除 Mermaid 图表"), "deleteMermaid")
        }
    }

    /// 代码块/前置元数据命令组（对齐 Windows AppendCodeContextMenu）：
    /// [撤销/重做] [声明代码语言] [格式化代码块] 复制整段代码 ─ 转为正文 ─ 剪贴板/全选。
    private func addCodeContextMenu(
        _ menu: NSMenu,
        state: EditorContextMenuState,
        includeLanguage: Bool,
        includeFormat: Bool
    ) {
        if state.frontMatterActive {
            if EditorMenuPolicy.allows(.copyCodeBlock, state: state) {
                addFormatCommand(menu, L10n.t("复制元数据"), "copyCodeBlock")
            }
            if !state.isReadOnly {
                menu.addItem(.separator())
                addFormatCommand(menu, L10n.t("移到元数据之后"), "exitCode")
            }
            menu.addItem(.separator())
            addClipboardCommands(menu, includeCopyAs: false, includeSelectAll: true)
            return
        }
        if !state.isReadOnly {
            addHistoryCommands(menu)
            menu.addItem(.separator())
        }
        if includeLanguage, EditorMenuPolicy.allows(.declareCodeLanguage, state: state) {
            addFormatCommand(menu, L10n.t("声明代码语言…"), "declareCodeLanguage")
        }
        if includeFormat {
            let formatItem = menuItem(
                L10n.t(editorCommandEnabled("copy") ? "格式化选中行" : "格式化代码块"),
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
            addFormatCommand(menu, L10n.t("转为正文"), "exitCode")
            menu.addItem(.separator())
        }
        addClipboardCommands(menu, includeCopyAs: false, includeSelectAll: true)
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
            copyAs.isEnabled = canCopyAs || canPaste
            let copyAsMenu = NSMenu()
            let plain = menuItem(L10n.t("纯文本"), #selector(copyPlain(_:)))
            plain.isEnabled = canCopyAs
            copyAsMenu.addItem(plain)
            let markdown = menuItem(L10n.t("Markdown"), #selector(copyMarkdown(_:)))
            markdown.isEnabled = canCopyAs
            copyAsMenu.addItem(markdown)
            let html = menuItem(L10n.t("HTML"), #selector(handleCommand(_:)))
            html.representedObject = "copyHtml"
            html.isEnabled = editorCommandEnabled("copyHtml")
            copyAsMenu.addItem(html)
            copyAs.submenu = copyAsMenu
            menu.addItem(copyAs)
        }
        addEnabledCommand(menu, L10n.t("粘贴"), "paste", enabled: canPaste)
        let pastePlain = menuItem(L10n.t("粘贴为纯文本"), #selector(pastePlainTextFromClipboardAction(_:)))
        pastePlain.representedObject = "pastePlainText"
        pastePlain.isEnabled = canPaste
        applyShortcut(to: pastePlain, command: "pastePlainText")
        menu.addItem(pastePlain)
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
