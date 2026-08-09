import AppKit
import UniformTypeIdentifiers

/// 多窗口管理器：持有所有编辑器窗口，广播偏好设置变更。
final class AppWindowManager {
    static let shared = AppWindowManager()

    private(set) var windowControllers: [EditorWindowController] = []
    private var preferencesController: PreferencesWindowController?
    private var recoveryController: RecoveryWindowController?
    private var shortcutController: ShortcutWindowController?
    private var findPanelController: FindPanelController?
    private var startupActionState = StartupActionState()

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

    /// 完成启动时仅在文件打开回调尚未创建窗口的情况下建立初始窗口。
    func ensureInitialWindow() {
        guard windowControllers.isEmpty else { return }
        _ = newWindow()
    }

    var primarySession: EditorSession? {
        windowControllers.first?.session
    }

    /// 当前活跃（键窗口）会话；无键窗口时退回第一个。
    var activeSession: EditorSession? {
        windowControllers.first { $0.window?.isKeyWindow == true }?.session
            ?? windowControllers.first?.session
    }

    /// 文件 > 在新窗口中打开…（对应 Windows AppCommand.OpenDocumentInNewWindow）。
    func openDocumentInNewWindow() {
        guard let session = activeSession, let window = session.webView?.window else { return }
        let panel = NSOpenPanel()
        panel.title = L10n.t("在新窗口中打开")
        panel.allowedContentTypes = [.plainText, (UTType(filenameExtension: "md") ?? .plainText)]
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard response == .OK, let url = panel.url else { return }
            _ = self?.newWindow(documentPath: url.path)
        }
    }

    /// 打开偏好设置窗口（单例）。
    func showPreferences() {
        if preferencesController == nil {
            guard let session = primarySession else { return }
            let controller = PreferencesWindowController(
                styles: session.styles,
                themes: session.colorThemes)
            controller.onSettingsChanged = { [weak self] in
                self?.applyPreferencesToAll()
            }
            preferencesController = controller
        }
        preferencesController?.showWindow(nil)
        preferencesController?.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    /// 界面语言切换：重建菜单、重建偏好设置窗口、刷新所有编辑器窗口与前端。
    func applyLanguage() {
        NativeMenuBuilder.refreshIfNeeded()
        // 重建偏好设置（标签在 init 时按当前语言生成）
        if let prefs = preferencesController {
            prefs.window?.close()
            preferencesController = nil
        }
        for controller in windowControllers {
            controller.applyLanguage()
        }
        findPanelController?.applyLanguage()
        showPreferences()
        NSApp.activate(ignoringOtherApps: true)
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
        let version = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.1.0"
        let linkURL = URL(string: "https://github.com/zhuanshunjishi2017/markleaf")!

        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .center
        paragraph.lineSpacing = 5
        paragraph.paragraphSpacing = 2

        let text = "macOS 原生轻量化 Markdown 编辑器\n\n作者：zhuanshunjishi2017\n\(linkURL.absoluteString)"
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

    /// 打开文件（Finder 关联 / 命令行）。
    func openDocumentInFrontWindow(_ url: URL) {
        if let session = primarySession {
            routeIncomingDocument(url, to: session)
        } else {
            _ = newWindow(documentPath: url.path)
        }
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
