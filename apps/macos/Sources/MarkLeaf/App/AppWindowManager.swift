import AppKit
import UniformTypeIdentifiers

struct PreferencesRestoration: Equatable {
    let selectedPageIndex: Int
    let frame: NSRect
}

struct PreferencesRefreshState {
    let selectedPageIndex: Int
    let frame: NSRect
    let wasVisible: Bool

    var restoration: PreferencesRestoration? {
        wasVisible ? PreferencesRestoration(selectedPageIndex: selectedPageIndex, frame: frame) : nil
    }
}

/// 多窗口管理器：持有所有编辑器窗口，广播偏好设置变更。
final class AppWindowManager {
    static let shared = AppWindowManager()

    private(set) var windowControllers: [EditorWindowController] = []
    private(set) var windowSessions: [EditorWindowController: WindowSession] = [:]
    private var preferencesController: PreferencesWindowController?
    private var recoveryController: RecoveryWindowController?
    private var shortcutController: ShortcutWindowController?
    private var findPanelController: FindPanelController?
    private var updateCheckController: UpdateCheckController?
    private var startupActionState = StartupActionState()
    private var bootstrapState = StartupBootstrapState()
    private var memoryPressureSource: DispatchSourceMemoryPressure?
    private(set) var isTerminationCommitted = false
    private lazy var sessionScheduler = SessionWriteScheduler(store: .shared)

    init() {
        sessionScheduler.onWriteFailure = { [weak self] tabID in
            self?.markRecoveryUnavailable(tabID: DocumentTabID(tabID))
        }
        sessionScheduler.onSnapshotWritten = { [weak self] tabID, fileName in
            self?.markRecoveryAvailable(tabID: DocumentTabID(tabID), snapshotFileName: fileName)
        }
    }

    func markRecoveryUnavailable(tabID: DocumentTabID) {
        for (_, windowSession) in windowSessions {
            guard let tab = windowSession.tabStore.tab(withID: tabID) else { continue }
            tab.recoveryUnavailable = true
            windowSession.controller?.reloadTabBar()
            if let session = windowSession.activeTabSession, windowSession.tabStore.activeTabID == tabID {
                session.statusText = L10n.t("恢复保护暂时不可用")
            }
            return
        }
    }

    func markRecoveryAvailable(tabID: DocumentTabID, snapshotFileName: String? = nil) {
        for (_, windowSession) in windowSessions {
            guard let tab = windowSession.tabStore.tab(withID: tabID) else { continue }
            tab.recoveryUnavailable = false
            if let snapshotFileName { tab.snapshotFileName = snapshotFileName }
            windowSession.controller?.reloadTabBar()
            return
        }
    }

    func startMemoryPressureMonitoring() {
        guard memoryPressureSource == nil else { return }
        let source = DispatchSource.makeMemoryPressureSource(eventMask: [.warning, .critical], queue: .main)
        source.setEventHandler { [weak self] in self?.suspendBackgroundTabsUnderPressure() }
        source.setCancelHandler { }
        source.resume()
        memoryPressureSource = source
    }

    func suspendBackgroundTabsUnderPressure() {
        windowControllers.forEach { $0.suspendBackgroundTabsIfNeeded() }
    }

    func newWindow(documentPath: String? = nil) -> EditorWindowController {
        let windowSession = WindowSession()
        let session = EditorSession(workspace: windowSession.workspace)
        let controller = EditorWindowController(session: session)
        windowSession.controller = controller
        let tab = DocumentTab(
            path: nil,
            title: L10n.t("未命名"),
            encoding: SettingsService.shared.settings.defaultEncoding,
            newLine: DocumentNewLinePolicy.style(from: SettingsService.shared.settings.newLineStyle).rawValue
        )
        tab.untitledSequence = windowSession.tabStore.nextUntitledSequence()
        windowSession.tabStore.append(tab)
        windowSession.attach(session: session, to: tab.tabID)
        controller.windowSession = windowSession
        session.openViaWindow = { [weak windowSession] url in
            windowSession?.requestOpenFile(url)
        }
        session.workspace.windowProvider = { [weak controller] in controller?.window }
        windowSession.onOpenFile = { [weak controller] resolution, url in
            controller?.handleOpenResolution(resolution, url: url)
        }
        windowControllers.append(controller)
        windowSessions[controller] = windowSession
        controller.onWindowClose = { [weak self] closed in
            self?.windowControllers.removeAll { $0 === closed }
            self?.windowSessions.removeValue(forKey: closed)
        }
        controller.showWindow(nil)
        controller.openInitialDocument(path: documentPath)
        return controller
    }

    func newWindow(preparedDocument: PreparedDocument) -> EditorWindowController {
        let windowSession = WindowSession()
        let session = EditorSession(workspace: windowSession.workspace)
        let controller = EditorWindowController(session: session)
        windowSession.controller = controller
        let tab = DocumentTab(
            path: nil,
            title: L10n.t("未命名"),
            encoding: SettingsService.shared.settings.defaultEncoding,
            newLine: DocumentNewLinePolicy.style(from: SettingsService.shared.settings.newLineStyle).rawValue
        )
        tab.untitledSequence = windowSession.tabStore.nextUntitledSequence()
        windowSession.tabStore.append(tab)
        windowSession.attach(session: session, to: tab.tabID)
        controller.windowSession = windowSession
        session.openViaWindow = { [weak windowSession] url in
            windowSession?.requestOpenFile(url)
        }
        session.workspace.windowProvider = { [weak controller] in controller?.window }
        windowSession.onOpenFile = { [weak controller] resolution, url in
            controller?.handleOpenResolution(resolution, url: url)
        }
        windowControllers.append(controller)
        windowSessions[controller] = windowSession
        controller.onWindowClose = { [weak self] closed in
            self?.windowControllers.removeAll { $0 === closed }
            self?.windowSessions.removeValue(forKey: closed)
        }
        controller.showWindow(nil)
        controller.openInitialDocument(prepared: preparedDocument)
        return controller
    }

    /// 顺序处理所有编辑器窗口的未保存文档后，回复 AppKit 是否允许退出。
    func requestApplicationTermination(completion: @escaping (Bool) -> Void) {
        let requests = windowControllers.map { controller in
            { (finish: @escaping (DocumentDispositionResult) -> Void) in
                let started = controller.session.requestDisposition(
                    for: .terminateApplication,
                    completion: finish
                )
                if !started { finish(.cancel) }
            }
        }
        SequentialDocumentDispositionQueue.run(requests) { [weak self] result in
            guard result == .proceed, let self else { completion(false); return }
            self.sessionScheduler.manifestProvider = { [weak self] in self?.buildSessionManifest() }
            self.sessionScheduler.flushNow(reason: .termination) { [weak self] _ in
                guard let self else { completion(true); return }
                do { try SessionStore.shared.commit(manifest: self.buildSessionManifest()) }
                catch { AppLog.error("退出会话提交失败: \(error.localizedDescription)") }
                self.isTerminationCommitted = true
                completion(true)
            }
        }
    }

    func buildSessionManifest() -> SessionManifest {
        let previous = SessionStore.shared.loadLatest().manifest
        let windows = windowControllers.compactMap { controller -> SessionWindowRecord? in
            guard let session = windowSessions[controller] else { return nil }
            let tabs = session.tabStore.tabs.map { tab in
                SessionTabRecord(tabID: tab.tabID.rawValue, path: tab.path, title: tab.title, untitledSequence: tab.untitledSequence, isDirty: tab.isDirty, revision: tab.contentRevision, encoding: tab.encoding, newLine: tab.newLine, fingerprintModificationSeconds: tab.fingerprintModificationSeconds, fingerprintSize: tab.fingerprintSize, cursorPosition: tab.cursorPosition, selectionAnchor: tab.selectionAnchor, selectionHead: tab.selectionHead, scrollTop: tab.scrollTop, snapshotFileName: tab.snapshotFileName)
            }
            let frame = controller.window?.frame
            return SessionWindowRecord(windowID: session.windowID, frameX: frame.map { Double($0.origin.x) }, frameY: frame.map { Double($0.origin.y) }, frameWidth: frame.map { Double($0.size.width) }, frameHeight: frame.map { Double($0.size.height) }, workspacePath: session.workspace.root, sidebarVisible: controller.session.sidebarVisible, sidebarTab: controller.session.sidebarTabIndex == 1 ? "outline" : "workspace", sidebarWidth: SettingsService.shared.settings.workspaceWidth, outlineDetached: controller.session.outlineDetached, outlineWidth: SettingsService.shared.settings.outlineWidth, statusBarVisible: controller.session.statusBarVisible, tabOrder: tabs.map(\.tabID), activeTabID: session.tabStore.activeTabID?.rawValue, tabs: tabs)
        }
        return SessionManifest(schemaVersion: SessionManifestCodec.currentSchemaVersion, generation: (previous?.generation ?? 0) + 1, savedAt: Date(), windows: windows)
    }

    /// 在设置、图标、文件关联和菜单完成配置后，建立唯一的初始窗口。
    func completeBootstrapAndEnsureInitialWindow() {
        switch bootstrapState.complete() {
        case .createInitialWindow(let documentPath, let additionalDocumentPaths):
            _ = startupActionState.consume()
            let initialController: EditorWindowController
            if let documentPath {
                do {
                    let prepared = try PreparedDocument.read(from: URL(fileURLWithPath: documentPath))
                    initialController = newWindow(preparedDocument: prepared)
                } catch {
                    AppLog.error("无法打开启动文档: \(documentPath) \(error.localizedDescription)")
                    initialController = newWindow()
                    initialController.session.presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
                }
            } else {
                initialController = newWindow()
            }
            for path in additionalDocumentPaths {
                do {
                    let prepared = try PreparedDocument.read(from: URL(fileURLWithPath: path))
                    _ = newWindow(preparedDocument: prepared)
                } catch {
                    AppLog.error("无法打开外部文档: \(path) \(error.localizedDescription)")
                    initialController.session.presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
                }
            }
        case .noOp:
            return
        }
    }

    @discardableResult
    func restoreFullSession(explicitFile: String?) -> Bool {
        guard explicitFile == nil, SettingsService.shared.settings.startupAction == .restoreSession else { return false }
        let loaded = SessionStore.shared.loadLatest()
        guard let manifest = loaded.manifest, !manifest.windows.isEmpty else { return false }
        _ = startupActionState.consume()
        let screens = NSScreen.screens.map(\.visibleFrame)
        let frames = WindowFrameRestorationPolicy.stagger(manifest.windows.map { record in
            let frame = CGRect(x: record.frameX ?? 100, y: record.frameY ?? 100, width: record.frameWidth ?? 1100, height: record.frameHeight ?? 760)
            return WindowFrameRestorationPolicy.constrain(frame, screens: screens)
        })
        for (index, record) in manifest.windows.enumerated() {
            let workspace = WorkspaceContext()
            if let path = record.workspacePath { workspace.load(path) }
            let windowSession = WindowSession(windowID: record.windowID, workspace: workspace)
            let ordered = record.tabOrder.compactMap { id in record.tabs.first { $0.tabID == id } } + record.tabs.filter { !record.tabOrder.contains($0.tabID) }
            for item in ordered {
                let tab = DocumentTab(tabID: DocumentTabID(item.tabID), path: item.path, title: item.title, encoding: item.encoding, newLine: item.newLine, untitledSequence: item.untitledSequence)
                tab.isDirty = item.isDirty; tab.contentRevision = item.revision
                tab.fingerprintModificationSeconds = item.fingerprintModificationSeconds; tab.fingerprintSize = item.fingerprintSize
                tab.cursorPosition = item.cursorPosition; tab.selectionAnchor = item.selectionAnchor; tab.selectionHead = item.selectionHead
                tab.scrollTop = item.scrollTop; tab.snapshotFileName = item.snapshotFileName
                windowSession.tabStore.append(tab, activate: false)
            }
            guard !windowSession.tabStore.tabs.isEmpty else { continue }
            windowSession.tabStore.activate(record.activeTabID.map(DocumentTabID.init) ?? windowSession.tabStore.tabs[0].tabID)
            let controller = EditorWindowController(session: EditorSession(workspace: workspace))
            controller.windowSession = windowSession
            windowControllers.append(controller); windowSessions[controller] = windowSession
            controller.onWindowClose = { [weak self] closed in
                self?.windowControllers.removeAll { $0 === closed }; self?.windowSessions.removeValue(forKey: closed)
            }
            if index < frames.count { controller.window?.setFrame(frames[index], display: false) }
            controller.showWindow(nil); controller.restoreInitialTabIfNeeded()
        }
        return !windowControllers.isEmpty
    }

    var primarySession: EditorSession? {
        windowControllers.first?.windowSession?.activeTabSession
    }

    /// 当前活跃（键窗口）会话 = 活动窗口的活动标签会话；无键窗口时退回第一个窗口。
    var activeSession: EditorSession? {
        activeWindowController?.windowSession?.activeTabSession
            ?? windowControllers.first?.windowSession?.activeTabSession
    }

    /// Window-level state (sidebar/status bar) must remain controllable even
    /// after the last document tab has been closed.
    var activeViewStateSession: EditorSession? {
        guard let controller = activeWindowController ?? windowControllers.first else { return nil }
        return windowSessions[controller]?.controller?.session ?? controller.session
    }

    /// 当前活跃窗口的会话边界。
    var activeWindowSession: WindowSession? {
        activeWindowController.flatMap { windowSessions[$0] }
    }

    /// 当前活动查找面板（供窗口层右键/菜单跟随活动标签使用）。
    var currentFindPanel: FindPanelController? {
        findPanelController
    }

    /// 找到一个其标签身份命中指定文件的窗口；命中则激活对应标签并前置。
    private func controller(containing url: URL) -> EditorWindowController? {
        let identity = FileIdentityPolicy.identity(for: url)
        for (controller, windowSession) in windowSessions {
            if let tab = windowSession.tabStore.tab(withIdentity: identity) {
                if tab.tabID != windowSession.tabStore.activeTabID {
                    controller.activateTab(tab.tabID, animated: true)
                }
                return controller
            }
        }
        return nil
    }

    /// 当前活跃（键窗口）控制器；窗口级命令（如专注模式）使用它路由。
    var activeWindowController: EditorWindowController? {
        windowControllers.first { $0.window?.isKeyWindow == true }
            ?? windowControllers.first { $0.window?.isMainWindow == true }
            ?? windowControllers.first
    }

    /// 响应 Dock 图标重开：前置已有编辑窗口，否则创建一个新的空白编辑窗口。
    func handleApplicationReopen(hasVisibleWindows: Bool) {
        let action = ApplicationLifecyclePolicy.reopenAction(
            hasVisibleWindows: hasVisibleWindows,
            hasEditorWindow: !windowControllers.isEmpty
        )

        switch action {
        case .none:
            return
        case .showExistingWindow:
            guard let controller = activeWindowController ?? windowControllers.first else { return }
            controller.showWindow(nil)
            controller.window?.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
        case .createNewWindow:
            _ = newWindow()
            NSApp.activate(ignoringOtherApps: true)
        }
    }

    /// 文件 > 在新窗口中打开…（对应 Windows AppCommand.OpenDocumentInNewWindow）。
    func openDocumentInNewWindow() {
        guard let session = activeSession, let window = session.webView?.window else { return }
        let panel = NSOpenPanel()
        panel.title = L10n.t("在新窗口中打开")
        panel.allowedContentTypes = [.plainText, (UTType(filenameExtension: "md") ?? .plainText)]
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url, let self else { return }
            do {
                let prepared = try PreparedDocument.read(from: url)
                _ = self.newWindow(preparedDocument: prepared)
            } catch {
                AppLog.error("无法打开文档: \(url.path) \(error.localizedDescription)")
                self.activeSession?.presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
            }
        }
    }

    /// 打开偏好设置窗口（单例）。
    func showPreferences() {
        if preferencesController == nil {
            preferencesController = makePreferences()
        }
        preferencesController?.showWindow(nil)
        preferencesController?.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// 界面语言切换：重建菜单、重建偏好设置窗口、刷新所有编辑器窗口与前端。
    func applyLanguage() {
        NativeMenuBuilder.refreshIfNeeded()

        let refreshState = preferencesController.flatMap { controller -> PreferencesRefreshState? in
            guard let window = controller.window else { return nil }
            return PreferencesRefreshState(
                selectedPageIndex: controller.selectedPageIndex,
                frame: window.frame,
                wasVisible: window.isVisible
            )
        }

        preferencesController?.window?.close()
        preferencesController = nil

        for controller in windowControllers {
            controller.applyLanguage()
        }
        findPanelController?.applyLanguage()

        guard let restoration = refreshState?.restoration,
              let controller = makePreferences(restoration: restoration)
        else { return }
        preferencesController = controller
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func makePreferences(
        restoration: PreferencesRestoration? = nil
    ) -> PreferencesWindowController? {
        guard let session = primarySession else { return nil }
        let controller = PreferencesWindowController(
            styles: session.styles,
            themes: session.colorThemes,
            initialSelectedPageIndex: restoration?.selectedPageIndex ?? 0
        )
        if let frame = restoration?.frame {
            controller.window?.setFrame(frame, display: false)
        }
        controller.onSettingsChanged = { [weak self] in
            self?.applyPreferencesToAll()
        }
        // 窗口关闭（取消/应用/重设）后丢弃实例，下次打开重建，避免残留未提交的控件值。
        controller.onClose = { [weak self] in
            self?.preferencesController = nil
        }
        return controller
    }

    func applyPreferencesToAll() {
        let topMost = SettingsService.shared.settings.topMostWindow
        for controller in windowControllers {
            (controller.windowSession?.activeTabSession ?? controller.session).applyPreferences()
            controller.window?.level = topMost ? .floating : .normal
            controller.applyViewState()
        }
    }

    /// 样式/主题变更（如导入主题）后：重新发送样式到各窗口、重建偏好设置与菜单。
    func reloadStyles() {
        // 先应用样式（applyPreferences -> applyStyles 会刷新各会话的 styles/colorThemes）
        applyPreferencesToAll()
        NativeMenuBuilder.refreshIfNeeded()
        // 重建偏好设置（以显示新导入的主题）
        if let prefs = preferencesController {
            prefs.window?.close()
            preferencesController = nil
        }
    }

    /// 启动行为：将设置解析为一次性的加载计划，并在指定会话中执行。
    @discardableResult
    func performStartupAction(for session: EditorSession, explicitFile: String? = nil) -> Bool {
        guard startupActionState.consume() else { return false }
        let settings = SettingsService.shared.settings
        let fileManager = FileManager.default
        let plan = StartupActionResolver.resolve(
            action: settings.startupAction,
            lastFolder: settings.lastFolder,
            lastFile: settings.lastFile,
            explicitFile: explicitFile,
            isDirectory: { path in
                var isDirectory: ObjCBool = false
                return fileManager.fileExists(atPath: path, isDirectory: &isDirectory) && isDirectory.boolValue
            },
            isFile: { path in
                var isDirectory: ObjCBool = false
                return fileManager.fileExists(atPath: path, isDirectory: &isDirectory) && !isDirectory.boolValue
            })
        AppLog.info("执行启动计划: \(plan)")

        switch plan.operation {
        case .newDocument:
            session.newDocument()
        case .openExplicitFile(let path), .openFile(let path):
            session.openDocument(at: URL(fileURLWithPath: path))
        case .openWorkspace(let path):
            session.loadWorkspace(path)
            session.newDocument()
        case .openWorkspaceAndFile(let workspace, let file):
            session.loadWorkspace(workspace)
            session.openDocument(at: URL(fileURLWithPath: file))
        }

        if let notice = plan.notice {
            let status: String
            switch notice {
            case .missingWorkspace:
                status = L10n.t("上次工作区不可用，已打开可用内容")
            case .missingFile:
                status = L10n.t("上次文件不可用，已打开可用内容")
            case .missingWorkspaceAndFile:
                status = L10n.t("上次工作区和文件均不可用，已新建文档")
            }
            session.preserveStartupRecoveryNoticeForCurrentDocumentLoad(status)
        }
        return true
    }

    /// 关于 MarkLeaf：所有语言统一使用英文版本格式。
    func showAbout() {
        let options = AboutPanel.standardOptions(
            infoDictionary: Bundle.main.infoDictionary,
            descriptionText: L10n.t("macOS 原生轻量化 Markdown 编辑器")
        )
        NSApp.orderFrontStandardAboutPanel(options: options)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// 查找与替换面板（原生弹出窗口）。
    func showFindPanel(for session: EditorSession, showingReplace: Bool = false) {
        let controller: FindPanelController
        if let existing = findPanelController {
            existing.updateSession(session)
            controller = existing
        } else {
            controller = FindPanelController(session: session)
            findPanelController = controller
        }
        session.onFindResult = { [weak controller] current, total in
            DispatchQueue.main.async { controller?.updateResult(current: current, total: total) }
        }
        controller.showPanel(showingReplace: showingReplace)
    }

    /// 快捷键参考窗口。
    /// 跟随系统开关变化后刷新所有会话的主题（开→跟随系统；关→恢复手动主题）。
    func applyThemeModeToAll() {
        let follow = SettingsService.shared.settings.followSystemTheme
        for controller in windowControllers {
            if follow {
                (controller.windowSession?.activeTabSession ?? controller.session).applyFollowSystemTheme()
            } else {
                (controller.windowSession?.activeTabSession ?? controller.session).setTheme(SettingsService.shared.settings.colorTheme)
            }
        }
        preferencesController?.syncFollowSystemThemeState()
    }

    /// 打开「更新内容」（对应 Windows ShowChangelog：按语言复制到可写缓存目录后在当前窗口打开）。
    func openChangelog() {
        guard let source = ChangelogResource.bundledURL(
            in: Bundle.main,
            displayLanguage: SettingsService.shared.settings.displayLanguage
        ) else {
            activeSession?.statusText = L10n.t("无法打开更新内容")
            return
        }
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        let cacheDir = base.appendingPathComponent("MarkLeaf/Cache", isDirectory: true)
        try? FileManager.default.createDirectory(at: cacheDir, withIntermediateDirectories: true)
        let target = ChangelogResource.cachedURL(for: source, cacheDirectory: cacheDir)
        do {
            try? FileManager.default.removeItem(at: target)
            try FileManager.default.copyItem(at: source, to: target)
        } catch {
            activeSession?.statusText = L10n.t("无法打开更新内容")
            return
        }

        do {
            let markdown = try String(contentsOf: target, encoding: .utf8)
            let prepared = PreparedDocument(url: target, markdown: markdown, isReadOnly: true)
            // 菜单跟踪期间新窗口无法成为 key window，等跟踪结束后再创建并激活，
            // 避免只读窗口虽然打开却仍停留在旧窗口焦点上。
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in
                guard let self else { return }
                if let existing = self.controller(containing: target) {
                    existing.window?.makeKeyAndOrderFront(nil)
                    NSApp.activate(ignoringOtherApps: true)
                    return
                }
                let controller = self.newWindow(preparedDocument: prepared)
                controller.window?.makeKeyAndOrderFront(nil)
                NSApp.activate(ignoringOtherApps: true)
            }
        } catch {
            activeSession?.statusText = L10n.t("无法打开更新内容")
        }
    }

    /// 打开可编辑的欢迎文档；每次从应用资源刷新缓存副本，避免修改内置资源。
    func openWelcome() {
        guard let source = WelcomeResource.bundledURL(
            in: Bundle.main,
            displayLanguage: SettingsService.shared.settings.displayLanguage
        ) else {
            activeSession?.statusText = L10n.t("未找到欢迎文档")
            return
        }
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        let cacheDir = base.appendingPathComponent("MarkLeaf/Cache", isDirectory: true)
        let target = WelcomeResource.cachedURL(cacheDirectory: cacheDir)
        if let existing = controller(containing: target) {
            existing.window?.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        do {
            try FileManager.default.createDirectory(at: cacheDir, withIntermediateDirectories: true)
            if FileManager.default.fileExists(atPath: target.path) {
                try FileManager.default.removeItem(at: target)
            }
            try FileManager.default.copyItem(at: source, to: target)
            let markdown = try String(contentsOf: target, encoding: .utf8)
            let prepared = PreparedDocument(url: target, markdown: markdown, isReadOnly: false)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in
                guard let self else { return }
                let controller = self.newWindow(preparedDocument: prepared)
                controller.window?.makeKeyAndOrderFront(nil)
                NSApp.activate(ignoringOtherApps: true)
            }
        } catch {
            activeSession?.statusText = L10n.t("无法打开欢迎文档")
        }
    }

    func showShortcuts() {
        let controller = ShortcutWindowController()
        shortcutController = controller
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// 检查 GitHub 是否有新版本。
    func checkForUpdates() {
        let controller = UpdateCheckController()
        updateCheckController = controller
        controller.begin()
    }

    /// 恢复未保存的文件（对应 C# RecoverUnsavedFiles）。
    func showRecoveryDialog() {
        let pending = RecoveryService.pendingRecoveries()
        if pending.isEmpty {
            let alert = NSAlert()
            alert.messageText = L10n.t("未发现需要恢复的文件。")
            alert.alertStyle = .informational
            alert.addButton(withTitle: L10n.t("好"))
            if let window = activeSession?.webView?.window {
                alert.beginSheetModal(for: window)
            } else {
                alert.runModal()
            }
            return
        }
        let controller = RecoveryWindowController(snapshots: pending)
        recoveryController = controller // 持有，避免按钮 target 失效
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// 打开外部文件（Finder 关联 / Open With / Dock / 命令行），支持多文件与去重。
    func openExternalDocuments(_ urls: [URL]) {
        let paths = urls.filter(\.isFileURL).map { IncomingFileRouter.normalized($0).path }
        guard !paths.isEmpty else { return }
        if bootstrapState.cacheIncomingDocumentsIfNeeded(paths) { return }

        // 去重覆盖所有窗口的所有标签：后台标签中的重复文件也应激活而非二次打开。
        let openDocuments = windowControllers.flatMap { controller -> [URL] in
            guard let windowSession = controller.windowSession else { return [] }
            return windowSession.tabStore.tabs.compactMap { tab in
                windowSession.session(for: tab.tabID)?.documentURL
            }
        }
        IncomingFileRouter.route(
            urls: urls,
            mode: SettingsService.shared.settings.externalFileOpenMode,
            activeEditor: activeWindowController != nil,
            openDocuments: openDocuments,
            activateExisting: { [weak self] url in
                guard let self else { return }
                for controller in self.windowControllers {
                    guard let windowSession = controller.windowSession else { continue }
                    if let tab = windowSession.tabStore.tabs.first(where: { tab in
                        guard let documentURL = windowSession.session(for: tab.tabID)?.documentURL else { return false }
                        return IncomingFileRouter.normalized(documentURL) == url
                    }) {
                        controller.window?.makeKeyAndOrderFront(nil)
                        controller.activateTab(tab.tabID, animated: true)
                        break
                    }
                }
                NSApp.activate(ignoringOtherApps: true)
            },
            replaceActive: { [weak self] url in
                self?.activeWindowController?.windowSession?.activeTabSession?.openDocument(at: url)
            },
            newTabInActiveWindow: { [weak self] url in
                guard let self, let controller = self.activeWindowController else { return }
                controller.window?.makeKeyAndOrderFront(nil)
                controller.windowSession?.requestOpenFile(url)
            },
            createWindow: { [weak self] url in
                guard let self else { return }
                do {
                    let prepared = try PreparedDocument.read(from: url)
                    _ = self.newWindow(preparedDocument: prepared)
                } catch {
                    AppLog.error("无法打开外部文档: \(url.path) \(error.localizedDescription)")
                    self.activeWindowController?.windowSession?.activeTabSession?
                        .presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
                }
            }
        )
    }

    /// 将 Finder 文件意图路由到已有会话；内部可见以覆盖冷启动关联文件路径。
    func routeIncomingDocument(_ url: URL, to session: EditorSession) {
        switch startupActionState.disposition(forIncomingFile: url.path) {
        case .pendingInitialIntent(let path):
            session.openInitialDocument(path: path)
        case .openImmediately(let path):
            session.openDocument(at: URL(fileURLWithPath: path))
        }
    }
}
