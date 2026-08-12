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
    private var preferencesController: PreferencesWindowController?
    private var recoveryController: RecoveryWindowController?
    private var shortcutController: ShortcutWindowController?
    private var findPanelController: FindPanelController?
    private var startupActionState = StartupActionState()
    private var bootstrapState = StartupBootstrapState()

    init() {}

    func newWindow(documentPath: String? = nil) -> EditorWindowController {
        let session = EditorSession()
        let controller = EditorWindowController(session: session)
        windowControllers.append(controller)
        controller.onWindowClose = { [weak self] closed in
            self?.windowControllers.removeAll { $0 === closed }
        }
        controller.showWindow(nil)
        controller.openInitialDocument(path: documentPath)
        return controller
    }

    func newWindow(preparedDocument: PreparedDocument) -> EditorWindowController {
        let session = EditorSession()
        let controller = EditorWindowController(session: session)
        windowControllers.append(controller)
        controller.onWindowClose = { [weak self] closed in
            self?.windowControllers.removeAll { $0 === closed }
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
        SequentialDocumentDispositionQueue.run(requests) { result in
            completion(result == .proceed)
        }
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

    var primarySession: EditorSession? {
        windowControllers.first?.session
    }

    /// 当前活跃（键窗口）会话；无键窗口时退回第一个。
    var activeSession: EditorSession? {
        activeWindowController?.session
    }

    /// 当前活跃（键窗口）控制器；窗口级命令（如专注模式）使用它路由。
    var activeWindowController: EditorWindowController? {
        windowControllers.first { $0.window?.isKeyWindow == true }
            ?? windowControllers.first
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
        return controller
    }

    func applyPreferencesToAll() {
        let topMost = SettingsService.shared.settings.topMostWindow
        for controller in windowControllers {
            controller.session.applyPreferences()
            controller.window?.level = topMost ? .floating : .normal
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

    /// 关于 MarkLeaf：macOS 原生关于面板（同 LyricsX，orderFrontStandardAboutPanel）。
    /// credits 排版：居中、行距、仓库链接可点击。
    func showAbout() {
        let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? AppVersion.fallback
        let linkURL = URL(string: "https://github.com/zhuanshunjishi2017/markleaf")!

        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .center
        paragraph.lineSpacing = 5
        paragraph.paragraphSpacing = 2

        let text = L10n.t("macOS 原生轻量化 Markdown 编辑器") + "\n\n" + L10n.t("作者：") + " zhuanshunjishi2017\n" + linkURL.absoluteString
        let credits = NSMutableAttributedString(string: text, attributes: [
            .font: NSFont.systemFont(ofSize: NSFont.smallSystemFontSize),
            .foregroundColor: NSColor.secondaryLabelColor,
            .paragraphStyle: paragraph,
        ])
        let linkRange = (text as NSString).range(of: linkURL.absoluteString)
        credits.addAttribute(.link, value: linkURL, range: linkRange)
        credits.addAttribute(.foregroundColor, value: NSColor.linkColor, range: linkRange)
        credits.addAttribute(.underlineStyle, value: NSUnderlineStyle.single.rawValue, range: linkRange)

        NSApp.orderFrontStandardAboutPanel(options: [
            .applicationName: "MarkLeaf",
            .applicationVersion: version,
            .credits: credits,
        ])
        NSApp.activate(ignoringOtherApps: true)
    }

    /// 查找与替换面板（原生弹出窗口）。
    func showFindPanel(for session: EditorSession, replaceMode: Bool) {
        let controller = FindPanelController(session: session, replaceMode: replaceMode)
        session.onFindResult = { [weak controller] current, total in
            DispatchQueue.main.async { controller?.updateResult(current: current, total: total) }
        }
        findPanelController = controller
        controller.showPanel()
    }

    /// 快捷键参考窗口。
    /// 跟随系统开关变化后刷新所有会话的主题（开→跟随系统；关→恢复手动主题）。
    func applyThemeModeToAll() {
        let follow = SettingsService.shared.settings.followSystemTheme
        for controller in windowControllers {
            if follow {
                controller.session.applyFollowSystemTheme()
            } else {
                controller.session.setTheme(SettingsService.shared.settings.colorTheme)
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
        if let session = activeSession {
            session.openDocument(at: target)
        } else {
            NSWorkspace.shared.open(target)
        }
    }

    func showShortcuts() {
        let controller = ShortcutWindowController()
        shortcutController = controller
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
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

        let openDocuments = windowControllers.compactMap { $0.session.documentURL }
        IncomingFileRouter.route(
            urls: urls,
            mode: SettingsService.shared.settings.externalFileOpenMode,
            activeEditor: activeWindowController != nil,
            openDocuments: openDocuments,
            activateExisting: { [weak self] url in
                self?.windowControllers.first { $0.session.documentURL == url }?
                    .window?.makeKeyAndOrderFront(nil)
                NSApp.activate(ignoringOtherApps: true)
            },
            replaceActive: { [weak self] url in
                self?.activeWindowController?.session.openDocument(at: url)
            },
            createWindow: { [weak self] url in
                guard let self else { return }
                do {
                    let prepared = try PreparedDocument.read(from: url)
                    _ = self.newWindow(preparedDocument: prepared)
                } catch {
                    AppLog.error("无法打开外部文档: \(url.path) \(error.localizedDescription)")
                    self.activeWindowController?.session.presentError(L10n.f("无法打开文档：%@", error.localizedDescription))
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
