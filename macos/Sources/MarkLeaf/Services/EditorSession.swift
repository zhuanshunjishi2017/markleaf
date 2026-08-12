import AppKit
import UniformTypeIdentifiers
import WebKit

struct PreparedDocument: Equatable {
    let url: URL
    let markdown: String

    static func read(from url: URL) throws -> PreparedDocument {
        let standardized = url.standardizedFileURL.resolvingSymlinksInPath()
        return PreparedDocument(
            url: standardized,
            markdown: try String(contentsOf: standardized, encoding: .utf8)
        )
    }
}

/// 编辑器宿主会话：对应 C# EditorHostController + 文档管理。
/// - 作为 WKScriptMessageHandler 接收编辑器发来的消息（ready/snapshot/dirtyChanged...）
/// - 通过 evaluateJavaScript 向编辑器发送宿主消息（applyStyles/loadDocument/command...）
final class EditorSession: NSObject, WKScriptMessageHandler, WKNavigationDelegate {
    static func cjkLanguageScript(for tag: CJKLanguageTag) -> String {
        let value = tag.rawValue
        return "document.documentElement.setAttribute('lang','\(value)');document.documentElement.style.setProperty('--ml-cjk-lang','\(value)');"
    }
    // MARK: 可观察状态（AppKit 通过 onStateChanged 刷新 UI）

    var statusText = L10n.t("就绪") {
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
    private(set) var canStartFormatPainter = false
    private(set) var isFormatPainterArmed = false
    private(set) var headingLevel: Int?

    // 工作区 / 大纲
    private(set) var workspaceRoot: String?
    private(set) var workspaceTree: [WorkspaceEntry] = []
    private(set) var outlineHeadings: [OutlineHeading] = []
    private(set) var activeOutlinePosition: Int?

    var onWorkspaceChanged: (() -> Void)?
    var onOutlineChanged: (() -> Void)?
    var onOutlineSelectionChanged: (() -> Void)?
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
    private var startupRecoveryNotice: (documentID: String, text: String)?
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

    private let snapshotRequests = SnapshotRequestQueue()
    private let writeCoordinator = SerialWriteCoordinator()
    var pendingExport = false
    var pendingSelectionExport: ((Result<EditorSelectionExport, Error>) -> Void)?
    var pendingExportContext: ExportContext?
    private var didLoadInitialDocument = false
    private var didRunInitialLoad = false
    private var pendingInitialOpenPath: String?
    private var useStartupAction = false
    private let documentDisposition = DocumentDispositionCoordinator()
    private(set) var isReady = false

    var windowTitle: String {
        let base = documentURL?.lastPathComponent ?? L10n.t("未命名")
        return isDirty ? base + L10n.t(" — 已编辑") : base
    }

    var currentDocumentIdentifier: String { documentId }
    var pendingInitialDocumentPath: String? { pendingInitialOpenPath }
    var isDocumentDispositionInProgress: Bool { documentDisposition.isInProgress }
    private(set) var dispositionRequestCount = 0
    private var pendingInitialPreparedDocument: PreparedDocument?

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

    func handleEditorMessage(_ message: [String: Any]) {
        guard let type = message["type"] as? String else { return }
        let payload = message["payload"] as? [String: Any]

        switch type {
        case "ready":
            AppLog.info("编辑器就绪 (protocol v1)")
            isReady = true
            applyStyles()
            // 对齐 Windows 1.1.3：前端就绪后再揭示 WebView，避免深色模式白闪
            (webView?.superview as? EditorWebContainerView)?.revealEditor()
            if !didLoadInitialDocument {
                didLoadInitialDocument = true
                loadInitialDocument()
            }

        case "documentLoaded":
            AppLog.info("文档加载完成")
            statusText = L10n.t("已加载")
            applyPostLoadSettings()
            if let notice = startupRecoveryNotice(for: message) {
                statusText = notice
            }

        case "snapshot":
            let markdown = payload?["markdown"] as? String ?? ""
            snapshotRequests.completeNext(.success(markdown))

        case "dirtyChanged":
            let dirty = payload?["dirty"] as? Bool ?? false
            isDirty = dirty
            if dirty {
                clearStartupRecoveryNotice(for: message)
                statusText = L10n.t("已修改")
            } else if startupRecoveryNotice(for: message) == nil {
                statusText = L10n.t("已保存")
            }

        case "editorStatusChanged":
            if startupRecoveryNotice(for: message) == nil {
                updateStatus(from: payload)
            }

        case "commandStateChanged":
            isSourceMode = payload?["sourceMode"] as? Bool ?? false
            imageSelected = payload?["imageSelected"] as? Bool ?? false
            inTable = payload?["inTable"] as? Bool ?? false
            canUndo = payload?["canUndo"] as? Bool ?? false
            canRedo = payload?["canRedo"] as? Bool ?? false
            canStartFormatPainter = payload?["canStartFormatPainter"] as? Bool ?? false
            isFormatPainterArmed = payload?["formatPainterArmed"] as? Bool ?? false
            headingLevel = payload?["headingLevel"] as? Int

        case "outlineChanged":
            let headings = (payload?["headings"] as? [[String: Any]]) ?? []
            outlineHeadings = headings.compactMap { dict -> OutlineHeading? in
                guard let level = dict["level"] as? Int,
                      let text = dict["text"] as? String,
                      let position = dict["position"] as? Int else { return nil }
                return OutlineHeading(level: level, text: text, position: position)
            }
            if startupRecoveryNotice(for: message) == nil {
                statusText = L10n.f("大纲 %d 项", outlineHeadings.count)
            }
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
            let detail = payload?["message"] as? String ?? L10n.t("未知错误")
            AppLog.warning("编辑器前端错误: \(detail)")
            statusText = L10n.t("前端错误")

        case "selectionExport":
            handleSelectionExport(payload)

        case "outlineSelectionChanged":
            if let position = payload?["position"] as? Int {
                activeOutlinePosition = position
            } else {
                activeOutlinePosition = nil
            }
            onOutlineSelectionChanged?()

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

        case "findResult":
            if let payload, let current = payload["current"] as? Int, let total = payload["total"] as? Int {
                AppLog.info("findResult: current=\(current) total=\(total)")
                onFindResult?(current, total)
            }

        case "dropFiles", "commandResult", "selectionChanged":
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
        statusText = L10n.f("%@ · 行 %d 列 %d · %d 字符", Self.blockTypeDisplayName(blockType), line, column, characterCount)
    }

    func preserveStartupRecoveryNoticeForCurrentDocumentLoad(_ text: String) {
        startupRecoveryNotice = (documentId, text)
    }

    private func startupRecoveryNotice(for message: [String: Any]) -> String? {
        guard let notice = startupRecoveryNotice,
              message["documentId"] as? String == notice.documentID else { return nil }
        return notice.text
    }

    private func clearStartupRecoveryNotice(for message: [String: Any]) {
        guard startupRecoveryNotice(for: message) != nil else { return }
        startupRecoveryNotice = nil
    }

    private static func blockTypeDisplayName(_ blockType: String) -> String {
        switch blockType {
        case "paragraph": return L10n.t("正文")
        case "heading1": return L10n.t("标题 1")
        case "heading2": return L10n.t("标题 2")
        case "heading3": return L10n.t("标题 3")
        case "heading4": return L10n.t("标题 4")
        case "heading5": return L10n.t("标题 5")
        case "heading6": return L10n.t("标题 6")
        case "blockquote": return L10n.t("引用")
        case "codeBlock": return L10n.t("代码块")
        case "bulletList": return L10n.t("无序列表")
        case "orderedList": return L10n.t("有序列表")
        case "taskList": return L10n.t("任务列表")
        case "table": return L10n.t("表格")
        case "image": return L10n.t("图片")
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

        // 应用已保存的颜色主题；开启「与操作系统同步」时改用系统外观对应的默认主题
        if !saved.followSystemTheme, let theme = colorThemes.first(where: { $0.id == saved.colorTheme }) {
            payload["colorThemeCss"] = theme.css
            currentThemeId = theme.id
            applySystemAppearance(for: theme)
        } else {
            let dark = NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
            let id = manager.defaultThemeID(
                forDark: dark,
                preferredLight: saved.defaultLightThemeID,
                preferredDark: saved.defaultDarkThemeID
            ) ?? manager.defaultThemeId
            currentThemeId = id
            if let theme = colorThemes.first(where: { $0.id == id }) {
                payload["colorThemeCss"] = theme.css
                applySystemAppearance(for: theme)
            }
        }
        // 应用已保存的排版样式
        if styles.contains(where: { $0.id == saved.markdownStyle }) {
            payload["activeStyle"] = saved.markdownStyle
            currentStyleId = saved.markdownStyle
        }
        send("applyStyles", payload: payload)
        // 下发界面语言（前端查找栏等文案本地化）
        execute("setLanguage", text: SettingsService.shared.settings.displayLanguage)
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

    /// 当前主题的 --bg-primary 背景色（用于加载期打底，对齐 Windows DefaultBackgroundColor）。
    var themeBackgroundColor: NSColor? {
        guard let id = currentThemeId,
              let theme = colorThemes.first(where: { $0.id == id }),
              let hex = StyleManager.parseColorVariable("--bg-primary", in: theme.css) else { return nil }
        return NSColor(hexString: hex)
    }

    func loadDocument(markdown: String, fileURL: URL?) {
        // 替换文档时清理上一个文档的快照
        RecoveryService.shared.delete(documentId: documentId)
        startupRecoveryNotice = nil
        documentId = UUID().uuidString.lowercased()
        revision = 0
        isDirty = false
        documentURL = fileURL
        statusText = fileURL?.lastPathComponent ?? L10n.t("未命名")
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
            statusText = L10n.t("文件已被外部删除")
            return
        }
        guard let window = webView?.window else { return }
        let alert = NSAlert()
        alert.messageText = L10n.t("文件已在外部修改")
        alert.informativeText = isDirty
            ? L10n.f("%@ 已被其他程序修改。重新加载将丢失当前未保存的更改。", url.lastPathComponent)
            : L10n.f("%@ 已被其他程序修改，是否重新加载？", url.lastPathComponent)
        alert.alertStyle = .warning
        alert.addButton(withTitle: L10n.t("重新加载"))
        alert.addButton(withTitle: L10n.t("忽略"))
        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertFirstButtonReturn else { return }
            do {
                let markdown = try String(contentsOf: url, encoding: .utf8)
                self?.loadDocument(markdown: markdown, fileURL: url)
                self?.statusText = L10n.t("已重新加载外部更改")
            } catch {
                self?.statusText = L10n.t("外部文件读取失败")
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
        snapshotRequests.enqueue(completion)
        send("requestSnapshot")
    }

    /// 是否开启「与操作系统同步」（读取当前设置）。
    var isFollowSystemTheme: Bool {
        SettingsService.shared.settings.followSystemTheme
    }

    private var systemThemeObserver: NSObjectProtocol?

    /// 注册系统外观变化监听（浅色/深色切换时重新解析默认主题）。
    private func startFollowingSystemAppearance() {
        guard systemThemeObserver == nil else { return }
        systemThemeObserver = DistributedNotificationCenter.default().addObserver(
            forName: Notification.Name("AppleInterfaceThemeChangedNotification"),
            object: nil, queue: .main) { [weak self] _ in
            self?.applyFollowSystemTheme()
        }
    }

    /// 跟随系统外观：重新解析默认主题并换肤（开关切换或系统外观变化时调用）。
    func applyFollowSystemTheme() {
        guard isFollowSystemTheme else { return }
        let dark = NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        let settings = SettingsService.shared.settings
        guard let manager = StyleManager(directories: styleDirectories),
              let id = manager.defaultThemeID(
                forDark: dark,
                preferredLight: settings.defaultLightThemeID,
                preferredDark: settings.defaultDarkThemeID
              ), id != currentThemeId else { return }
        currentThemeId = id
        guard let theme = colorThemes.first(where: { $0.id == id }) else { return }
        applySystemAppearance(for: theme)
        var payload = manager.applyStylesPayload()
        payload["colorThemeCss"] = theme.css
        payload["activeStyle"] = currentStyleId
        send("applyStyles", payload: payload)
    }

    func setStyle(_ id: String) {
        currentStyleId = id
        SettingsService.shared.update { $0.markdownStyle = id }
        execute("setStyle", text: id)
    }

    func setTheme(_ id: String) {
        // 跟随系统外观时忽略手动主题选择（偏好设置与菜单均已禁用）
        guard !isFollowSystemTheme else { return }
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
            guard let self else { return }
            let dark = theme.isDark
            if self.isFollowSystemTheme {
                NSApp.appearance = nil
            } else {
                NSApp.appearance = dark ? NSAppearance(named: .darkAqua) : nil
            }
            self.applyScrollbarAppearance(dark: dark)
        }
    }

    /// 滚动条外观：autoHideScrollbars 关 → 常显滚动条（跟随主题明暗）；开 → 系统 overlay。
    func applyScrollbarAppearance(dark: Bool) {
        let legacy = !SettingsService.shared.settings.autoHideScrollbars
        (webView?.superview as? EditorWebContainerView)?.applyThemeAppearance(
            dark: dark, legacyScrollers: legacy)
    }

    func toggleSourceMode() { execute("toggleSourceMode") }
    var onFindResult: ((Int, Int) -> Void)?

    func showFind(showReplace: Bool) {
        AppWindowManager.shared.showFindPanel(for: self, replaceMode: showReplace)
    }

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

    /// 源码模式缩进宽度（对应偏好设置「源码模式 > 默认缩进宽度」，前端 CodeMirror indentUnit/tabSize）。
    /// 界面语言切换：重译当前状态文案并下发前端。
    func applyLanguage() {
        statusText = L10n.t(L10n.canonicalize(statusText))
        execute("setLanguage", text: SettingsService.shared.settings.displayLanguage)
    }

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
        statusText = L10n.f("缩放 %d%%", zoomPercent)
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
        // 源码字体：西文 + 中文独立选择（对齐 Windows fccc7ad）
        let sourceFontFamily = Self.quoteFont(settings.sourceFontFamily) + ", " + Self.quoteFont(settings.sourceCjkFontFamily) + ", monospace"
        let script = """
        document.documentElement.style.setProperty('--ml-line-height','\(String(format: "%.2f", settings.visualLineHeight))');
        document.documentElement.style.setProperty('--ml-font-size','\(String(format: "%.2f", targetFont))px');
        document.documentElement.style.setProperty('--ml-max-width','\(String(format: "%.2f", targetWidth))px');
        document.documentElement.style.setProperty('--ml-source-font-size','\(String(format: "%.2f", sourceFont))px');
        document.documentElement.style.setProperty('--ml-source-font-family','\(sourceFontFamily)');
        \(Self.cjkLanguageScript(for: settings.cjkLanguageTag))
        """
        webView?.evaluateJavaScript(script)
    }

    /// 将字体名转为 CSS 引号包裹的字符串（含空格名必须引号）。
    private static func quoteFont(_ name: String) -> String {
        let trimmed = name.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { return "monospace" }
        let escaped = trimmed.replacingOccurrences(of: "\"", with: "\\\"")
        return "\"\(escaped)\""
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
        panel.title = L10n.t("打开 Markdown 文档")
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
            let prepared = try PreparedDocument.read(from: url)
            requestDisposition(for: .replaceDocument) { [weak self] result in
                guard result == .proceed, let self else { return }
                self.loadPreparedDocument(prepared)
            }
        } catch {
            AppLog.error("打开文档失败: \(url.path) \(error.localizedDescription)")
            presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
        }
    }

    private func loadPreparedDocument(_ prepared: PreparedDocument) {
        loadDocument(markdown: prepared.markdown, fileURL: prepared.url)
        SettingsService.shared.addRecentFile(prepared.url.path)
        SettingsService.shared.update { $0.lastFile = prepared.url.path }
    }

    /// 统一未保存文档处置：关闭/替换/退出共用同一协调器。
    @discardableResult
    func requestDisposition(
        for reason: DocumentDispositionReason,
        completion: @escaping (DocumentDispositionResult) -> Void
    ) -> Bool {
        dispositionRequestCount += 1
        return documentDisposition.request(
            isDirty: isDirty,
            hasFileURL: documentURL != nil,
            reason: reason,
            settings: SettingsService.shared.settings,
            saveExisting: { [weak self] finish in
                guard let self, let fileURL = self.documentURL else {
                    finish(false)
                    return
                }
                self.writeCurrentDocument(to: fileURL, completion: finish)
            },
            saveAs: { [weak self] finish in
                self?.saveDocumentAs(completion: finish)
            },
            presentSavedPrompt: { [weak self] finish in
                guard let self, let window = self.webView?.window else {
                    finish(.cancel)
                    return
                }
                let alert = NSAlert()
                alert.messageText = L10n.f("是否保存对“%@”的修改？", self.windowTitle)
                alert.informativeText = L10n.t("如果不保存，您的更改将会丢失。")
                alert.alertStyle = .warning
                alert.addButton(withTitle: L10n.t("保存"))
                alert.addButton(withTitle: L10n.t("取消"))
                alert.addButton(withTitle: L10n.t("不保存"))
                alert.beginSheetModal(for: window) { response in
                    switch response {
                    case .alertFirstButtonReturn: finish(.save)
                    case .alertSecondButtonReturn: finish(.cancel)
                    default: finish(.discard)
                    }
                }
            },
            presentUntitledPrompt: { [weak self] finish in
                guard let self, let window = self.webView?.window else {
                    finish(.cancel)
                    return
                }
                let alert = NSAlert()
                alert.messageText = L10n.t("是否保留这个新文档？")
                alert.informativeText = L10n.t("如果不保存，这个文档将被删除。")
                alert.alertStyle = .warning
                alert.addButton(withTitle: L10n.t("保存…"))
                alert.addButton(withTitle: L10n.t("取消"))
                alert.addButton(withTitle: L10n.t("删除"))
                alert.beginSheetModal(for: window) { response in
                    switch response {
                    case .alertFirstButtonReturn: finish(.saveAs)
                    case .alertSecondButtonReturn: finish(.cancel)
                    default: finish(.delete)
                    }
                }
            },
            completion: completion
        )
    }

    func saveDocument(completion: ((Bool) -> Void)? = nil) {
        if let url = documentURL {
            writeCurrentDocument(to: url, completion: completion)
        } else {
            saveDocumentAs(completion: completion)
        }
    }

    func saveDocumentAs(completion: ((Bool) -> Void)? = nil) {
        let panel = NSSavePanel()
        panel.title = L10n.t("保存 Markdown 文档")
        panel.allowedContentTypes = [.plainText, (UTType(filenameExtension: "md") ?? .plainText)]
        panel.nameFieldStringValue = documentURL?.lastPathComponent ?? L10n.t("未命名.md")
        guard let window = webView?.window else { completion?(false); return }
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { completion?(false); return }
            self?.writeCurrentDocument(to: url, completion: completion)
        }
    }

    private func writeCurrentDocument(to url: URL, completion: ((Bool) -> Void)? = nil) {
        writeCoordinator.enqueue { [weak self] finish in
            guard let self else {
                completion?(false)
                finish()
                return
            }
            self.requestSnapshot { [weak self] result in
                DispatchQueue.main.async {
                    guard let self else {
                        completion?(false)
                        finish()
                        return
                    }
                    switch result {
                    case .success(let rawMarkdown):
                        do {
                            // 新建文件按设置写入换行风格；已打开文件保留原样
                            var markdown = rawMarkdown
                            if self.documentURL == nil,
                               SettingsService.shared.settings.newLineStyle == "crlf" {
                                markdown = rawMarkdown.replacingOccurrences(of: "\\r?\\n", with: "\\r\\n", options: .regularExpression)
                            }
                            try markdown.write(to: url, atomically: true, encoding: .utf8)
                            self.documentURL = url
                            SettingsService.shared.update { $0.lastFile = url.path }
                            self.isDirty = false
                            self.statusText = L10n.t("已保存")
                            AppLog.info("文档已保存: \(url.path)")
                            completion?(true)
                        } catch {
                            self.presentError(L10n.f("保存失败：%@", error.localizedDescription))
                            completion?(false)
                        }
                    case .failure(let error):
                        self.presentError(L10n.f("获取文档内容失败：%@", error.localizedDescription))
                        completion?(false)
                    }
                    finish()
                }
            }
        }
    }


    func presentError(_ message: String) {
        AppLog.error(message)
        statusText = L10n.t("出错")
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

    @discardableResult
    func moveWorkspaceEntry(from sourceURL: URL, toDirectory targetDirectory: URL) throws -> URL {
        guard let workspaceRoot else { throw WorkspaceMoveError.outsideWorkspace }
        let disposition = try WorkspaceMovePolicy.disposition(
            source: sourceURL,
            targetDirectory: targetDirectory,
            workspaceRoot: URL(fileURLWithPath: workspaceRoot, isDirectory: true)
        )
        guard case .move(let destination) = disposition else {
            return sourceURL.standardizedFileURL.resolvingSymlinksInPath()
        }
        let movesOpenDocument = documentURL?.standardizedFileURL == sourceURL.standardizedFileURL
        if movesOpenDocument { stopExternalChangeWatch() }
        do {
            try FileManager.default.moveItem(at: sourceURL, to: destination)
        } catch {
            if movesOpenDocument, let documentURL { startExternalChangeWatch(for: documentURL) }
            throw error
        }
        if movesOpenDocument {
            documentURL = destination
            statusText = destination.lastPathComponent
            startExternalChangeWatch(for: destination)
            SettingsService.shared.update { $0.lastFile = destination.path }
        }
        rescanWorkspace()
        return destination
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
        alert.messageText = L10n.t("插入超链接")
        alert.informativeText = L10n.t("输入链接地址：")
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("确定"))
        alert.addButton(withTitle: L10n.t("取消"))
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
        panel.title = L10n.t("插入图片")
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
        alert.messageText = L10n.t("插入来自互联网的图片")
        alert.informativeText = L10n.t("请输入图片URL：")
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("插入"))
        alert.addButton(withTitle: L10n.t("取消"))

        // 注意：NSAlert 的 accessoryView 需用显式 frame（Auto Layout 的 NSStackView 会塌陷为 0 高，输入框不可见）
        let accessory = NSView(frame: NSRect(x: 0, y: 0, width: 380, height: 28))
        let urlField = NSTextField(string: "")
        urlField.frame = NSRect(x: 0, y: 0, width: 380, height: 24)
        urlField.placeholderString = "https://example.com/image.png"
        accessory.addSubview(urlField)

        alert.accessoryView = accessory
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
            guard URL(string: url).map({ $0.scheme == "http" || $0.scheme == "https" }) ?? false else {
                self?.presentError(L10n.t("仅支持 http/https 图片地址"))
                return
            }
            self?.execute("insertImage", text: url + "\n图片")
            self?.statusText = L10n.t("图片已插入文档")
        }
    }

    func openRecentFile(_ path: String) {
        openDocument(at: URL(fileURLWithPath: path))
    }

    func openRecentFolder(_ path: String) {
        loadWorkspace(path)
    }

    /// 导入主题（对齐 Windows AddThemeFromFile）：选择 CSS 复制到用户主题目录，并刷新样式。
    func importTheme() {
        guard let window = webView?.window else { return }
        guard let dir = ResourceLocator.userThemesDirectory else {
            presentError(L10n.t("未找到主题样式文件夹"))
            return
        }
        let panel = NSOpenPanel()
        panel.title = L10n.t("选择主题 CSS 文件")
        if let cssType = UTType(filenameExtension: "css") {
            panel.allowedContentTypes = [cssType]
        }
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let source = panel.url else { return }
            self?.validateAndImportTheme(from: source, to: dir, window: window)
        }
    }

    /// 校验主题文件名与内容后导入；不合格时弹窗提示、不复制。
    private func validateAndImportTheme(from source: URL, to dir: URL, window: NSWindow) {
        let fileName = source.lastPathComponent
        guard StyleManager.isThemeFileName(fileName),
              let css = try? String(contentsOf: source, encoding: .utf8),
              StyleManager.isValidThemeContent(css) else {
            let alert = NSAlert()
            alert.messageText = L10n.t("不是有效的主题文件")
            alert.informativeText = L10n.t("请选择 colors-*.css 文件，且内容需包含 @type: color-theme 标记或至少一个可解析的颜色变量。")
            alert.alertStyle = .warning
            alert.beginSheetModal(for: window)
            return
        }
        let dest = dir.appendingPathComponent(fileName)
        if FileManager.default.fileExists(atPath: dest.path) {
            let alert = NSAlert()
            alert.messageText = L10n.f("主题文件“%@”已存在，是否覆盖？", dest.lastPathComponent)
            alert.alertStyle = .warning
            alert.addButton(withTitle: L10n.t("覆盖"))
            alert.addButton(withTitle: L10n.t("取消"))
            alert.buttons.first?.hasDestructiveAction = true
            alert.beginSheetModal(for: window) { resp in
                guard resp == .alertFirstButtonReturn else { return }
                self.copyThemeFile(from: source, to: dest)
            }
        } else {
            copyThemeFile(from: source, to: dest)
        }
    }

    private func copyThemeFile(from source: URL, to dest: URL) {
        do {
            try? FileManager.default.removeItem(at: dest)
            try FileManager.default.copyItem(at: source, to: dest)
            statusText = L10n.f("已添加主题：%@", dest.lastPathComponent)
            AppWindowManager.shared.reloadStyles()
        } catch {
            presentError(L10n.f("无法复制主题文件：%@", error.localizedDescription))
        }
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
        startFollowingSystemAppearance()
        if let path {
            pendingInitialOpenPath = path
        } else {
            useStartupAction = true
        }
        if isReady {
            runInitialLoad()
        }
    }

    /// 供窗口控制器在展示后调用：直接装载已预读的文档（绕过一次性启动解析器）。
    func openInitialDocument(prepared: PreparedDocument) {
        startFollowingSystemAppearance()
        pendingInitialPreparedDocument = prepared
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

        if let prepared = pendingInitialPreparedDocument {
            loadPreparedDocument(prepared)
            return
        }

        let explicitPath = pendingInitialOpenPath ?? Self.argumentValue("--open")
        if useStartupAction || explicitPath != nil {
            if !AppWindowManager.shared.performStartupAction(for: self, explicitFile: explicitPath) {
                newDocument()
            }
            return
        }
        newDocument()
    }

    static func argumentValue(_ name: String) -> String? {
        let arguments = CommandLine.arguments
        guard let index = arguments.firstIndex(of: name), index + 1 < arguments.count else {
            return nil
        }
        return arguments[index + 1]
    }

}
