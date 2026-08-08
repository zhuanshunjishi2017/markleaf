import AppKit
import UniformTypeIdentifiers
import WebKit

/// 编辑器宿主会话：对应 C# EditorHostController + 文档管理。
/// - 作为 WKScriptMessageHandler 接收编辑器发来的消息（ready/snapshot/dirtyChanged...）
/// - 通过 evaluateJavaScript 向编辑器发送宿主消息（applyStyles/loadDocument/command...）
final class EditorSession: NSObject, WKScriptMessageHandler, WKNavigationDelegate {
    // MARK: 可观察状态（AppKit 通过 onStateChanged 刷新 UI）

    var statusText = "就绪" {
        didSet { notify() }
    }
    private(set) var isDirty = false {
        didSet { notify() }
    }
    private(set) var documentURL: URL? {
        didSet { notify() }
    }
    private(set) var zoomPercent = 100 {
        didSet { notify() }
    }

    private(set) var styles: [StyleDefinition] = []
    private(set) var colorThemes: [ColorThemeInfo] = []
    private(set) var currentStyleId = "serif"
    private(set) var currentThemeId: String?
    private(set) var isSourceMode = false
    private(set) var imageSelected = false
    private(set) var inTable = false
    private(set) var canUndo = false
    private(set) var canRedo = false
    private(set) var headingLevel: Int?

    // 工作区 / 大纲
    private(set) var workspaceRoot: String?
    private(set) var workspaceTree: [WorkspaceEntry] = []
    private(set) var outlineHeadings: [OutlineHeading] = []
    private(set) var activeOutlinePosition: Int?

    var onWorkspaceChanged: (() -> Void)?
    var onOutlineChanged: (() -> Void)?
    var onStylesReady: (() -> Void)?
    var onExportComplete: ((Bool) -> Void)?
    var onViewStateChanged: (() -> Void)?
    var workspaceScanner: WorkspaceScanner?
    private var workspaceWatcher: WorkspaceWatcher?

    // 视图状态（对应 Windows 视图菜单）
    var sidebarVisible = true
    var sidebarTabIndex = 0
    private(set) var workspaceListMode = false
    var statusBarVisible = true
    private(set) var workspaceDocuments: [WorkspaceEntry] = []

    /// UI 状态变化回调（主线程）
    var onStateChanged: (() -> Void)?
    var snapshotModePath: String?

    weak var webView: WKWebView?

    private var documentId = UUID().uuidString.lowercased()
    private var revision: Int64 = 0
    private var fileMonitorSource: DispatchSourceFileSystemObject?
    private var monitoredFileDescriptor: Int32 = -1
    private var lastExternalChange: TimeInterval = 0

    // 均匀 10% 步进（Windows 为 [50,75,90,100,110,125,150,175,200]，100→125→150 跨度 25% 偏大）
    static let zoomOptions = Array(stride(from: 50, through: 200, by: 10))
    /// 触控板捏合：连续缩放值（平滑），手势结束（去抖）后写回设置。
    private var continuousZoom: Double = 100
    private var pinchPersistTimer: Timer?
    /// ⌘+滚轮：小 delta 事件累积到阈值（12）跳一档（一次滚轮格 ≈ 一档）。
    private var wheelZoomAccumulator: Double = 0
    private var recoveryTimer: Timer?

    private var pendingSnapshot: ((Result<String, Error>) -> Void)?
    var pendingExport = false
    var pendingSelectionExport: ((Result<EditorSelectionExport, Error>) -> Void)?
    var pendingExportContext: ExportContext?
    private var didLoadInitialDocument = false
    private var didRunInitialLoad = false
    private var pendingInitialOpenPath: String?
    private var useStartupAction = false
    private(set) var isReady = false

    var windowTitle: String {
        let base = documentURL?.lastPathComponent ?? "未命名"
        return isDirty ? base + " — 已编辑" : base
    }

    private func notify() {
        DispatchQueue.main.async { [weak self] in
            self?.onStateChanged?()
        }
    }

    // MARK: - 初始化

    func attachStyleManager(_ manager: StyleManager?) {
        guard let manager else { return }
        styles = manager.styles
        colorThemes = manager.colorThemes
        currentStyleId = manager.defaultStyleId
        currentThemeId = manager.defaultThemeId
    }

    // MARK: - WKScriptMessageHandler（编辑器 -> 宿主）

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        guard message.name == "markleaf" else { return }
        guard let body = message.body as? [String: Any] else { return }
        DispatchQueue.main.async { [weak self] in
            self?.handleEditorMessage(body)
        }
    }

    private func handleEditorMessage(_ message: [String: Any]) {
        guard let type = message["type"] as? String else { return }
        let payload = message["payload"] as? [String: Any]

        switch type {
        case "ready":
            AppLog.info("编辑器就绪 (protocol v1)")
            isReady = true
            applyStyles()
            if !didLoadInitialDocument {
                didLoadInitialDocument = true
                loadInitialDocument()
            }

        case "documentLoaded":
            AppLog.info("文档加载完成")
            statusText = "已加载"
            applyPostLoadSettings()

        case "snapshot":
            let markdown = payload?["markdown"] as? String ?? ""
            pendingSnapshot?(.success(markdown))
            pendingSnapshot = nil

        case "dirtyChanged":
            let dirty = payload?["dirty"] as? Bool ?? false
            isDirty = dirty
            statusText = dirty ? "已修改" : "已保存"

        case "editorStatusChanged":
            updateStatus(from: payload)

        case "commandStateChanged":
            isSourceMode = payload?["sourceMode"] as? Bool ?? false
            imageSelected = payload?["imageSelected"] as? Bool ?? false
            inTable = payload?["inTable"] as? Bool ?? false
            canUndo = payload?["canUndo"] as? Bool ?? false
            canRedo = payload?["canRedo"] as? Bool ?? false
            headingLevel = payload?["headingLevel"] as? Int

        case "outlineChanged":
            let headings = (payload?["headings"] as? [[String: Any]]) ?? []
            outlineHeadings = headings.compactMap { dict -> OutlineHeading? in
                guard let level = dict["level"] as? Int,
                      let text = dict["text"] as? String,
                      let position = dict["position"] as? Int else { return nil }
                return OutlineHeading(level: level, text: text, position: position)
            }
            statusText = "大纲 \(outlineHeadings.count) 项"
            onOutlineChanged?()

        case "openLink":
            if let urlString = payload?["url"] as? String, let url = URL(string: urlString), url.scheme != nil {
                NSWorkspace.shared.open(url)
            }

        case "requestSave":
            saveDocument()

        case "exportContent":
            let html = payload?["html"] as? String ?? ""
            pendingExport = false
            handleExportedContent(html)

        case "error":
            let detail = payload?["message"] as? String ?? "未知错误"
            AppLog.warning("编辑器前端错误: \(detail)")
            statusText = "前端错误"

        case "selectionExport":
            handleSelectionExport(payload)

        case "outlineSelectionChanged":
            if let position = payload?["position"] as? Int {
                activeOutlinePosition = position
                onOutlineChanged?()
            }

        case "contextMenuRequested":
            if let payload, let x = payload["clientX"] as? Double, let y = payload["clientY"] as? Double {
                showEditorContextMenu(clientX: x, clientY: y)
            }

        case "pasteImage":
            pasteFromClipboard()

        case "zoomWheel":
            let deltaY = payload?["deltaY"] as? Double ?? 0
            let source = payload?["source"] as? String ?? "pinch"
            if source == "wheel" {
                // ⌘+滚轮：由「使用 ⌘ + 滚轮进行缩放」设置单独控制；未开启则忽略
                guard SettingsService.shared.settings.ctrlWheelZoom else { break }
                // 累积小 delta 到阈值才跳档（一次滚轮格 ≈ 一档）
                if (wheelZoomAccumulator < 0 && deltaY > 0) || (wheelZoomAccumulator > 0 && deltaY < 0) {
                    wheelZoomAccumulator = 0
                }
                wheelZoomAccumulator += deltaY
                // 阈值 12：macOS 滚轮事件 delta 较小，一次滚轮格 ≈ 一档
                if abs(wheelZoomAccumulator) >= 12 {
                    if wheelZoomAccumulator < 0 {
                        zoomIn()
                    } else {
                        zoomOut()
                    }
                    continuousZoom = Double(zoomPercent)
                    wheelZoomAccumulator = 0
                }
            } else {
                // 触控板捏合（Ctrl+滚轮）：始终可用，连续平滑缩放
                continuousZoom = min(200, max(50, continuousZoom - deltaY * 0.25))
                applyZoomPercent(continuousZoom, persist: false)
                schedulePinchPersist(now: false)
            }

        case "dropFiles", "findResult", "commandResult", "selectionChanged":
            break

        default:
            AppLog.warning("未识别消息类型: \(type)")
        }
    }

    private func updateStatus(from payload: [String: Any]?) {
        guard let payload else { return }
        let blockType = payload["blockType"] as? String ?? ""
        let line = payload["line"] as? Int ?? 1
        let column = payload["column"] as? Int ?? 1
        let characterCount = payload["characterCount"] as? Int ?? 0
        statusText = "\(Self.blockTypeDisplayName(blockType)) · 行 \(line) 列 \(column) · \(characterCount) 字符"
    }

    private static func blockTypeDisplayName(_ blockType: String) -> String {
        switch blockType {
        case "paragraph": return "正文"
        case "heading1": return "标题 1"
        case "heading2": return "标题 2"
        case "heading3": return "标题 3"
        case "heading4": return "标题 4"
        case "heading5": return "标题 5"
        case "heading6": return "标题 6"
        case "blockquote": return "引用"
        case "codeBlock": return "代码块"
        case "bulletList": return "无序列表"
        case "orderedList": return "有序列表"
        case "taskList": return "任务列表"
        case "table": return "表格"
        case "image": return "图片"
        default: return blockType
        }
    }

    // MARK: - 宿主 -> 编辑器

    private func send(_ type: String, payload: [String: Any]? = nil, requestId: String? = nil) {
        var dict: [String: Any] = [
            "protocolVersion": 1,
            "type": type,
            "documentId": documentId,
            "revision": revision,
        ]
        if let payload {
            dict["payload"] = payload
        }
        if let requestId {
            dict["requestId"] = requestId
        }
        guard let jsonData = try? JSONSerialization.data(withJSONObject: dict),
              let json = String(data: jsonData, encoding: .utf8) else {
            AppLog.error("消息序列化失败: \(type)")
            return
        }
        // 用数组包裹字符串得到转义后的 JS 字符串字面量（裸 String 不能作为
        // NSJSONSerialization 顶层类型，会抛 ObjC 异常）。
        guard let literalData = try? JSONSerialization.data(withJSONObject: [json]),
              var literal = String(data: literalData, encoding: .utf8) else {
            AppLog.error("消息字面量序列化失败: \(type)")
            return
        }
        literal.removeFirst() // [" 
        literal.removeLast()  // "]
        let script = "window.postMessage(JSON.parse(\(literal)), '*')"
        webView?.evaluateJavaScript(script) { _, error in
            if let error {
                AppLog.warning("发送 \(type) 失败: \(error.localizedDescription)")
            }
        }
    }

    /// 样式目录：内置 + 用户主题目录（后者覆盖前者）。
    private var styleDirectories: [URL] {
        var dirs: [URL] = []
        if let builtIn = ResourceLocator.stylesDirectory { dirs.append(builtIn) }
        if let user = ResourceLocator.userThemesDirectory { dirs.append(user) }
        return dirs
    }

    func applyStyles() {
        guard !styleDirectories.isEmpty else {
            AppLog.error("样式资源缺失，跳过 applyStyles")
            return
        }
        let manager = StyleManager(directories: styleDirectories)
        attachStyleManager(manager)
        guard let manager else { return }

        var payload = manager.applyStylesPayload()
        let saved = SettingsService.shared.settings

        // 应用已保存的颜色主题
        if let theme = colorThemes.first(where: { $0.id == saved.colorTheme }) {
            payload["colorThemeCss"] = theme.css
            currentThemeId = theme.id
            applySystemAppearance(for: theme)
        } else {
            currentThemeId = manager.defaultThemeId
            if let fallback = colorThemes.first(where: { $0.id == currentThemeId }) {
                applySystemAppearance(for: fallback)
            }
        }
        // 应用已保存的排版样式
        if styles.contains(where: { $0.id == saved.markdownStyle }) {
            payload["activeStyle"] = saved.markdownStyle
            currentStyleId = saved.markdownStyle
        }
        send("applyStyles", payload: payload)
        onStylesReady?()
    }

    /// 偏好设置变更后应用到当前文档（不重复持久化）。
    func applyPreferences() {
        let settings = SettingsService.shared.settings
        applyStyles()
        applyVisualVariables(fontSize: nil, maxWidth: nil)
        applySourceIndent()
        if settings.restoreZoomOnOpen {
            applyZoom(settings.zoomPercent)
        }
        if settings.autoHideScrollbars {
            setAutoHideScrollbar(true)
        }
        applyScrollbarAppearance(dark: currentThemeIsDark)
    }

    private var currentThemeIsDark: Bool {
        guard let themeID = currentThemeId,
              let theme = colorThemes.first(where: { $0.id == themeID }) else { return false }
        return theme.isDark
    }

    func loadDocument(markdown: String, fileURL: URL?) {
        // 替换文档时清理上一个文档的快照
        RecoveryService.shared.delete(documentId: documentId)
        documentId = UUID().uuidString.lowercased()
        revision = 0
        isDirty = false
        documentURL = fileURL
        statusText = fileURL?.lastPathComponent ?? "未命名"
        if let fileURL {
            startExternalChangeWatch(for: fileURL)
        } else {
            stopExternalChangeWatch()
        }
        startRecoveryTimer()
        send("loadDocument", payload: ["markdown": markdown])
    }

    // MARK: - 崩溃恢复 / 自动保存（对应 C# RecoveryService + OnRecoveryTimerTick + OnAutoSaveTimerTick）

    private func startRecoveryTimer() {
        recoveryTimer?.invalidate()
        let interval = max(5, SettingsService.shared.settings.snapshotIntervalSeconds)
        let timer = Timer(timeInterval: TimeInterval(interval), repeats: true) { [weak self] _ in
            self?.recoveryTick()
        }
        RunLoop.main.add(timer, forMode: .common)
        recoveryTimer = timer
    }

    private func recoveryTick() {
        guard isReady, isDirty else { return }
        let settings = SettingsService.shared.settings
        if settings.autoSaveEnabled, documentURL != nil {
            // 自动保存：直接写回原文件
            writeCurrentDocument(to: documentURL!)
            return
        }
        // 恢复快照：把当前内容写入 Recovery 目录
        requestSnapshot { [weak self] result in
            guard let self, case .success(let markdown) = result else { return }
            RecoveryService.shared.writeSnapshot(
                documentId: self.documentId,
                path: self.documentURL?.path,
                markdown: markdown,
                revision: self.revision,
                displayName: self.documentURL?.lastPathComponent)
        }
    }

    /// 窗口关闭时清理：删除当前文档快照、停止定时器（对应 C# DeleteOwnFiles 的一部分）。
    func cleanupForClose() {
        recoveryTimer?.invalidate()
        recoveryTimer = nil
        stopExternalChangeWatch()
        workspaceWatcher?.stop()
        workspaceWatcher = nil
        RecoveryService.shared.delete(documentId: documentId)
    }

    // MARK: - 外部文件变更监控（对应 C# FileSystemWatcher + ExternalChangeDialog）

    private func startExternalChangeWatch(for url: URL) {
        stopExternalChangeWatch()
        let fd = open(url.path, O_EVTONLY)
        guard fd >= 0 else { return }
        monitoredFileDescriptor = fd
        let source = DispatchSource.makeFileSystemObjectSource(
            fileDescriptor: fd,
            eventMask: [.write, .delete, .rename, .extend],
            queue: .main)
        fileMonitorSource = source
        source.setEventHandler { [weak self] in
            let now = Date().timeIntervalSince1970
            guard now - (self?.lastExternalChange ?? 0) > 1.5 else { return }
            self?.lastExternalChange = now
            self?.handleExternalChange(url)
        }
        source.setCancelHandler {
            close(fd)
        }
        source.resume()
    }

    private func stopExternalChangeWatch() {
        fileMonitorSource?.cancel()
        fileMonitorSource = nil
        monitoredFileDescriptor = -1
    }

    private func handleExternalChange(_ url: URL) {
        guard FileManager.default.fileExists(atPath: url.path) else {
            statusText = "文件已被外部删除"
            return
        }
        guard let window = webView?.window else { return }
        let alert = NSAlert()
        alert.messageText = "文件已在外部修改"
        alert.informativeText = isDirty
            ? "\(url.lastPathComponent) 已被其他程序修改。重新加载将丢失当前未保存的更改。"
            : "\(url.lastPathComponent) 已被其他程序修改，是否重新加载？"
        alert.alertStyle = .warning
        alert.addButton(withTitle: "重新加载")
        alert.addButton(withTitle: "忽略")
        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertFirstButtonReturn else { return }
            do {
                let markdown = try String(contentsOf: url, encoding: .utf8)
                self?.loadDocument(markdown: markdown, fileURL: url)
                self?.statusText = "已重新加载外部更改"
            } catch {
                self?.statusText = "外部文件读取失败"
            }
        }
    }

    // MARK: - 命令

    func execute(_ command: String, text: String? = nil) {
        var payload: [String: Any] = ["command": command]
        if let text {
            payload["text"] = text
        }
        send("command", payload: payload)
    }

    func requestSnapshot(completion: @escaping (Result<String, Error>) -> Void) {
        pendingSnapshot = completion
        send("requestSnapshot")
    }

    func setStyle(_ id: String) {
        currentStyleId = id
        SettingsService.shared.update { $0.markdownStyle = id }
        execute("setStyle", text: id)
    }

    func setTheme(_ id: String) {
        currentThemeId = id
        SettingsService.shared.update { $0.colorTheme = id }
        guard let theme = colorThemes.first(where: { $0.id == id }) else { return }
        applySystemAppearance(for: theme)
        // 颜色主题通过 applyStyles 中的 colorThemeCss 注入；重发 applyStyles 即可换肤。
        if let manager = StyleManager(directories: styleDirectories) {
            var payload = manager.applyStylesPayload()
            payload["colorThemeCss"] = theme.css
            payload["activeStyle"] = currentStyleId
            send("applyStyles", payload: payload)
        }
    }

    private func applySystemAppearance(for theme: ColorThemeInfo) {
        DispatchQueue.main.async { [weak self] in
            let dark = theme.isDark
            NSApp.appearance = dark ? NSAppearance(named: .darkAqua) : nil
            self?.applyScrollbarAppearance(dark: dark)
        }
    }

    /// 滚动条外观：autoHideScrollbars 关 → 常显滚动条（跟随主题明暗）；开 → 系统 overlay。
    func applyScrollbarAppearance(dark: Bool) {
        let legacy = !SettingsService.shared.settings.autoHideScrollbars
        (webView?.superview as? EditorWebContainerView)?.applyThemeAppearance(
            dark: dark, legacyScrollers: legacy)
    }

    func toggleSourceMode() { execute("toggleSourceMode") }
    func showFind(showReplace: Bool) { execute(showReplace ? "replace" : "find") }

    /// 行内格式命令：空选时应用到整个文本块。
    func executeInlineFormat(_ command: String) {
        var payload: [String: Any] = ["command": command, "applyToCurrentTextBlockWhenEmpty": true]
        send("command", payload: payload)
    }

    /// 文档加载完成后应用持久化设置（缩放/自动隐藏滚动条）。
    func applyPostLoadSettings() {
        let settings = SettingsService.shared.settings
        if settings.restoreZoomOnOpen {
            setZoom(settings.zoomPercent)
        }
        if settings.autoHideScrollbars {
            setAutoHideScrollbar(true)
        }
        applySourceIndent()
    }

    /// 源码模式缩进宽度（对应首选项「源码模式 > 默认缩进宽度」，前端 CodeMirror indentUnit/tabSize）。
    func applySourceIndent() {
        let width = max(1, min(8, SettingsService.shared.settings.sourceIndentWidth))
        execute("setSourceIndent", text: "\(width)")
    }

    func setAutoHideScrollbar(_ enabled: Bool) {
        execute("setAutoHideScrollbar", text: enabled ? "1" : "0")
    }

    // MARK: - 缩放（对应 Windows 的 WebView2.ZoomFactor）

    func zoomIn() { nextZoom(delta: 1) }
    func zoomOut() { nextZoom(delta: -1) }
    func resetZoom() { setZoom(100) }

    func setZoom(_ percent: Int) {
        let target = Self.nearestZoom(percent)
        applyZoomPercent(Double(target), persist: true)
        continuousZoom = Double(target)
    }

    /// 仅应用缩放，不持久化（供偏好设置批量应用）。
    func applyZoom(_ percent: Int) {
        applyZoomPercent(Double(percent), persist: false)
        continuousZoom = Double(zoomPercent)
    }

    /// 应用缩放（支持连续值），可选持久化。
    private func applyZoomPercent(_ percent: Double, persist: Bool) {
        let clamped = min(200, max(50, percent))
        zoomPercent = Int(clamped.rounded())
        applyVisualVariables(fontSize: nil, maxWidth: nil)
        statusText = "缩放 \(zoomPercent)%"
        if persist {
            SettingsService.shared.update { $0.zoomPercent = zoomPercent }
        }
    }

    /// 捏合手势结束（300ms 无事件）后把连续值写回设置。
    private func schedulePinchPersist(now: Bool) {
        pinchPersistTimer?.invalidate()
        if now {
            SettingsService.shared.update { $0.zoomPercent = zoomPercent }
            return
        }
        let timer = Timer(timeInterval: 0.3, repeats: false) { [weak self] _ in
            guard let self else { return }
            SettingsService.shared.update { $0.zoomPercent = self.zoomPercent }
        }
        RunLoop.main.add(timer, forMode: .common)
        pinchPersistTimer = timer
    }

    /// 应用视觉设置基准变量（对应 C# ApplyCssVariables），随后叠加缩放。
    func applyVisualVariables(fontSize: Int?, maxWidth: Int?) {
        let settings = SettingsService.shared.settings
        let baseFont = Double(fontSize ?? settings.visualFontSize)
        let baseWidth = Double(maxWidth ?? settings.visualMaxContentWidth)
        let factor = Double(zoomPercent) / 100.0
        let targetFont = baseFont * factor
        let targetWidth = baseWidth * factor
        // 源码模式字号同样随缩放（--ml-source-font-size 基准值 × 缩放系数）
        let sourceFont = Double(settings.sourceFontSize) * factor
        let script = """
        document.documentElement.style.setProperty('--ml-line-height','\(String(format: "%.2f", settings.visualLineHeight))');
        document.documentElement.style.setProperty('--ml-font-size','\(String(format: "%.2f", targetFont))px');
        document.documentElement.style.setProperty('--ml-max-width','\(String(format: "%.2f", targetWidth))px');
        document.documentElement.style.setProperty('--ml-source-font-size','\(String(format: "%.2f", sourceFont))px');
        """
        webView?.evaluateJavaScript(script)
    }

    /// 对齐 C# NextZoom：在缩放档位中前后移动。
    /// 当前值不在档位（如捏合停在 125）时先吸附到最近档，避免误跳到 50%。
    func nextZoom(delta: Int) {
        let options = Self.zoomOptions
        let snapped = Self.nearestZoom(zoomPercent)
        guard let index = options.firstIndex(of: snapped) else {
            setZoom(options.first ?? 100)
            return
        }
        setZoom(options[max(0, min(options.count - 1, index + delta))])
    }

    /// 对齐 C# NearestZoom：吸附到最近缩放档位。
    private static func nearestZoom(_ percent: Int) -> Int {
        zoomOptions.min { abs($0 - percent) < abs($1 - percent) } ?? 100
    }

    // MARK: - 文档（新建/打开/保存/导出）

    func newDocument() {
        loadDocument(markdown: "", fileURL: nil)
    }

    func openDocument() {
        let panel = NSOpenPanel()
        panel.title = "打开 Markdown 文档"
        panel.allowedContentTypes = [.plainText, (UTType(filenameExtension: "md") ?? .plainText)]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        guard let window = webView?.window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.openDocument(at: url)
        }
    }

    func openDocument(at url: URL) {
        do {
            let markdown = try String(contentsOf: url, encoding: .utf8)
            loadDocument(markdown: markdown, fileURL: url)
            SettingsService.shared.addRecentFile(url.path)
            SettingsService.shared.update { $0.lastFile = url.path }
        } catch {
            AppLog.error("打开文档失败: \(url.path) \(error.localizedDescription)")
            presentError("无法打开文档：\(error.localizedDescription)")
        }
    }

    func saveDocument() {
        if let url = documentURL {
            writeCurrentDocument(to: url)
        } else {
            saveDocumentAs()
        }
    }

    func saveDocumentAs() {
        let panel = NSSavePanel()
        panel.title = "保存 Markdown 文档"
        panel.allowedContentTypes = [.plainText, (UTType(filenameExtension: "md") ?? .plainText)]
        panel.nameFieldStringValue = documentURL?.lastPathComponent ?? "未命名.md"
        guard let window = webView?.window else { return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.writeCurrentDocument(to: url)
        }
    }

    private func writeCurrentDocument(to url: URL) {
        requestSnapshot { [weak self] result in
            DispatchQueue.main.async {
                switch result {
                case .success(let rawMarkdown):
                    do {
                        // 新建文件按设置写入换行风格；已打开文件保留原样
                        var markdown = rawMarkdown
                        if self?.documentURL == nil,
                           SettingsService.shared.settings.newLineStyle == "crlf" {
                            markdown = rawMarkdown.replacingOccurrences(of: "\\r?\\n", with: "\\r\\n", options: .regularExpression)
                        }
                        try markdown.write(to: url, atomically: true, encoding: .utf8)
                        self?.documentURL = url
                        SettingsService.shared.update { $0.lastFile = url.path }
                        self?.isDirty = false
                        self?.statusText = "已保存"
                        AppLog.info("文档已保存: \(url.path)")
                    } catch {
                        self?.presentError("保存失败：\(error.localizedDescription)")
                    }
                case .failure(let error):
                    self?.presentError("获取文档内容失败：\(error.localizedDescription)")
                }
            }
        }
    }


    func presentError(_ message: String) {
        AppLog.error(message)
        statusText = "出错"
        if let window = webView?.window {
            let alert = NSAlert()
            alert.messageText = message
            alert.alertStyle = .warning
            alert.beginSheetModal(for: window)
        }
    }

    // MARK: - 工作区（对应 C# MainForm.Workspace）

    func loadWorkspace(_ path: String) {
        let fm = FileManager.default
        var isDirectory: ObjCBool = false
        guard fm.fileExists(atPath: path, isDirectory: &isDirectory), isDirectory.boolValue else { return }

        workspaceRoot = path
        SettingsService.shared.addRecentFolder(path)
        SettingsService.shared.update { $0.lastFolder = path }
        AppLog.info("打开工作区: \(path)")

        rescanWorkspace()

        // 自动监听工作区变化（删除刷新按钮）
        let watcher = WorkspaceWatcher()
        watcher.start(watching: path) { [weak self] in
            self?.rescanWorkspace()
        }
        workspaceWatcher = watcher
    }

    /// 重新扫描当前工作区（自动刷新 / 手动刷新共用）。
    func rescanWorkspace() {
        guard let root = workspaceRoot else { return }
        workspaceScanner?.cancel()
        workspaceTree = []
        onWorkspaceChanged?()
        let scanner = WorkspaceScanner(root: root) { [weak self] entries in
            self?.workspaceTree = entries
            self?.onWorkspaceChanged?()
        }
        workspaceScanner = scanner
        scanner.scan()
    }

    func closeWorkspace() {
        workspaceScanner?.cancel()
        workspaceScanner = nil
        workspaceWatcher?.stop()
        workspaceWatcher = nil
        workspaceRoot = nil
        workspaceTree = []
        onWorkspaceChanged?()
    }

    func openWorkspaceEntry(_ entry: WorkspaceEntry) {
        if entry.isDirectory {
            return // 由侧边栏展开处理
        }
        openDocument(at: URL(fileURLWithPath: entry.path))
    }

    func scrollToPosition(_ position: Int) {
        execute("scrollToPosition", text: "\(position)")
    }

    // MARK: - 视图状态（对应 Windows 视图菜单）

    func toggleSidebar() {
        sidebarVisible.toggle()
        SettingsService.shared.update { $0.sidebarVisible = sidebarVisible }
        onViewStateChanged?()
    }

    func showWorkspaceTab() {
        guard sidebarTabIndex != 0 else { return }
        sidebarTabIndex = 0
        onViewStateChanged?()
    }

    func showOutlineTab() {
        guard sidebarTabIndex != 1 else { return }
        sidebarTabIndex = 1
        onViewStateChanged?()
    }

    func setWorkspaceListMode(_ listMode: Bool) {
        guard workspaceListMode != listMode else { return }
        workspaceListMode = listMode
        onViewStateChanged?()
        if listMode, workspaceRoot != nil, workspaceDocuments.isEmpty {
            scanWorkspaceDocuments()
        }
    }

    func toggleStatusBar() {
        statusBarVisible.toggle()
        SettingsService.shared.update { $0.statusBarVisible = statusBarVisible }
        onViewStateChanged?()
    }

    func scanWorkspaceDocuments() {
        guard let root = workspaceRoot else { return }
        // 存入属性保持 scanner 存活（局部变量会提前释放导致异步扫描不回调）
        workspaceScanner?.cancel()
        let scanner = WorkspaceScanner(root: root) { _ in }
        workspaceScanner = scanner
        scanner.scanDocuments { [weak self] documents in
            self?.workspaceDocuments = documents
            self?.onWorkspaceChanged?()
        }
    }

    // MARK: - 插入链接 / 图片（对应 Windows LinkInputDialog / InsertImage）

    func insertLink() {
        guard let window = webView?.window else { return }
        let alert = NSAlert()
        alert.messageText = "插入超链接"
        alert.informativeText = "输入链接地址："
        alert.alertStyle = .informational
        alert.addButton(withTitle: "确定")
        alert.addButton(withTitle: "取消")
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 280, height: 24))
        field.placeholderString = "https://example.com"
        alert.accessoryView = field
        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertFirstButtonReturn else { return }
            let text = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !text.isEmpty else { return }
            self?.execute("setLink", text: text)
        }
    }

    func insertImageFromPicker() {
        guard let window = webView?.window else { return }
        let panel = NSOpenPanel()
        panel.title = "插入图片"
        panel.allowedContentTypes = [.png, .jpeg, .gif, .tiff, (UTType(filenameExtension: "webp") ?? .png)]
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            self?.insertImageFile(at: url)
        }
    }

    private var insertUrlObserver: NSObjectProtocol?

    /// 插入来自互联网的图片（对应 Windows InsertImageFromUrlAsync）：
    /// 直接以 Markdown 图片语法插入 URL（不下载），由前端渲染在线图片。
    func insertImageFromUrl() {
        guard let window = webView?.window else { return }
        let alert = NSAlert()
        alert.messageText = "插入来自互联网的图片"
        alert.informativeText = "输入图片地址，将以 Markdown 图片语法直接插入文档（不下载到本地）。"
        alert.alertStyle = .informational
        alert.addButton(withTitle: "插入")
        alert.addButton(withTitle: "取消")

        let urlField = NSTextField(string: "")
        urlField.placeholderString = "https://example.com/image.png"
        let altField = NSTextField(string: "")
        altField.placeholderString = "图片描述文字（可选）"

        let stack = NSStackView()
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 6
        stack.translatesAutoresizingMaskIntoConstraints = false
        let urlLabel = NSTextField(labelWithString: "图片地址：")
        urlLabel.font = .systemFont(ofSize: 12)
        let altLabel = NSTextField(labelWithString: "描述文字（Alt）：")
        altLabel.font = .systemFont(ofSize: 12)
        for view in [urlLabel, urlField, altLabel, altField] {
            stack.addArrangedSubview(view)
        }
        stack.widthAnchor.constraint(equalToConstant: 380).isActive = true

        alert.accessoryView = stack
        alert.window.initialFirstResponder = urlField

        let insertButton = alert.buttons.first
        insertButton?.isEnabled = false
        insertUrlObserver = NotificationCenter.default.addObserver(
            forName: NSControl.textDidChangeNotification,
            object: urlField,
            queue: .main
        ) { [weak insertButton] _ in
            let text = urlField.stringValue.trimmingCharacters(in: .whitespaces)
            let valid = URL(string: text).map { $0.scheme == "http" || $0.scheme == "https" } ?? false
            insertButton?.isEnabled = valid
        }

        alert.beginSheetModal(for: window) { [weak self] response in
            if let token = self?.insertUrlObserver {
                NotificationCenter.default.removeObserver(token)
                self?.insertUrlObserver = nil
            }
            guard response == .alertFirstButtonReturn else { return }
            let url = urlField.stringValue.trimmingCharacters(in: .whitespaces)
            let alt = altField.stringValue.trimmingCharacters(in: .whitespaces)
            guard URL(string: url).map({ $0.scheme == "http" || $0.scheme == "https" }) ?? false else {
                self?.presentError("仅支持 http/https 图片地址")
                return
            }
            self?.execute("insertImage", text: url + "\n" + alt)
            self?.statusText = "图片已插入文档"
        }
    }

    func openRecentFile(_ path: String) {
        openDocument(at: URL(fileURLWithPath: path))
    }

    func openRecentFolder(_ path: String) {
        loadWorkspace(path)
    }

    /// 打开用户主题目录（可写，可放入自定义 colors-*.css，重启后生效）。
    func revealThemeFolder() {
        guard let dir = ResourceLocator.userThemesDirectory else { return }
        NSWorkspace.shared.open(dir)
    }

    func showShortcuts() {
        AppWindowManager.shared.showShortcuts()
    }

    // MARK: - 初始文档

    /// 供窗口控制器在展示后调用：记录初始加载意图，编辑器就绪后真正执行。
    func openInitialDocument(path: String? = nil) {
        if let path {
            pendingInitialOpenPath = path
        } else {
            useStartupAction = true
        }
        if isReady {
            runInitialLoad()
        }
    }

    private func loadInitialDocument() {
        runInitialLoad()
    }

    private func runInitialLoad() {
        guard !didRunInitialLoad else { return }
        didRunInitialLoad = true

        if let path = pendingInitialOpenPath {
            openDocument(at: URL(fileURLWithPath: path))
            return
        }
        if let path = Self.argumentValue("--open") {
            openDocument(at: URL(fileURLWithPath: path))
            return
        }
        if useStartupAction {
            AppWindowManager.shared.performStartupAction()
            if workspaceRoot == nil && documentURL == nil {
                loadDocument(markdown: Self.sampleMarkdown, fileURL: nil)
            }
            return
        }
        loadDocument(markdown: Self.sampleMarkdown, fileURL: nil)
    }

    static func argumentValue(_ name: String) -> String? {
        let arguments = CommandLine.arguments
        guard let index = arguments.firstIndex(of: name), index + 1 < arguments.count else {
            return nil
        }
        return arguments[index + 1]
    }

    static let sampleMarkdown = """
    # MarkLeaf · macOS 原型

    > 这是 **MarkLeaf** 在 macOS 上的最小可运行原型：AppKit 原生外壳 + WKWebView 加载
    > 未经改动的 EditorWeb 前端（Tiptap/ProseMirror + CodeMirror 6）。

    ## 已经能用的

    - 原生菜单栏：**文件 / 格式 / 视图 / 窗口 / 帮助**
    - 文档打开、保存、另存为、导出 HTML
    - 排版样式切换与颜色主题（系统外观自动跟随深色主题）
    - 查找（⌘F）与查找替换（⌥⌘F）、源码模式（⌥⌘U）
    - 缩放（⌘+ / ⌘- / ⌘0）与状态栏

    ### 待办（对应移植路线图）

    1. 剪贴板 HTML（NSPasteboard）与 PDF 导出（WKWebView.createPDF）
    2. 工作区侧边栏 + 文件树（NSOutlineView）
    3. 大纲面板与光标联动
    4. 多窗口（NSWindowController）+ 窗口位置记忆
    5. 图片资源本地服务（assets.local → WKURLSchemeHandler）
    6. 分发：App Sandbox、签名、公证

    - [ ] 任务列表 1
    - [ ] 任务列表 2
    - [x] 已完成任务

    ```swift
    let editor = WKWebView()
    editor.load(URLRequest(url: markleafEditorURL))
    ```

    | 模块 | 复用策略 |
    |---|---|
    | EditorWeb 前端 | 100% 复用 |
    | 协议/会话/命令 | Swift 重写 |
    | UI 外壳 | AppKit 原生重写 |
    """
}
