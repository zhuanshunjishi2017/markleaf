import AppKit
import WebKit

/// 只拦截需要缩放的修饰键滚轮。普通滚轮立即交还 WKWebView，让 WebKit 可以
/// 使用异步滚动路径，而不必等待页面中的阻塞式 JavaScript wheel 监听器。
final class EditorWebView: WKWebView {
    weak var editorSession: EditorSession?

    override func magnify(with event: NSEvent) {
        // 触控板捏合：AppKit 的 magnification 为缩放因子增量，放大为正。
        // 统一映射到 handleZoomWheel 的连续缩放路径。
        let point = convert(event.locationInWindow, from: nil)
        editorSession?.handleZoomWheel(
            deltaY: -Double(event.magnification) * 80,
            source: "pinch",
            clientX: Double(point.x),
            clientY: Double(point.y)
        )
    }

    override func scrollWheel(with event: NSEvent) {
        let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        let route = EditorScrollWheelRoutingPolicy.route(
            command: modifiers.contains(.command),
            control: modifiers.contains(.control)
        )

        guard route != .scroll else {
            super.scrollWheel(with: event)
            return
        }

        let point = convert(event.locationInWindow, from: nil)
        editorSession?.handleZoomWheel(
            // AppKit 向上滚动为正；DOM WheelEvent 向上滚动为负，维持原缩放方向。
            deltaY: -Double(event.scrollingDeltaY),
            source: route == .commandZoom ? "wheel" : "pinch",
            clientX: Double(point.x),
            clientY: Double(point.y)
        )
    }
}

/// 编辑器宿主视图：创建并持有 WKWebView，通过 markleaf:// 自定义 scheme 加载本地编辑器资源
/// （对应 Windows 端 WebView2 + editor.local 虚拟主机映射）。
final class EditorWebContainerView: NSView, WKNavigationDelegate {
    let webView: WKWebView
    private weak var session: EditorSession?
    private var didFinishLoadOnce = false
    private var reloadCoverView: NSView?

    init(session: EditorSession) {
        self.session = session
        let configuration = WKWebViewConfiguration()

        if let editorURL = ResourceLocator.editorWebDirectory {
            configuration.setURLSchemeHandler(EditorSchemeHandler(root: editorURL), forURLScheme: "markleaf")
        } else {
            AppLog.error("EditorWeb 资源目录缺失")
        }
        // 本地图片资源服务（assets.local → markleaf-asset://）
        configuration.setURLSchemeHandler(AssetSchemeHandler(), forURLScheme: "markleaf-asset")
        configuration.userContentController.add(session, name: "markleaf")

        // 开发期启用 Web 检查器（仅 Debug 构建）
        #if DEBUG
        configuration.preferences.setValue(true, forKey: "developerExtrasEnabled")
        #endif

        let editorWebView = EditorWebView(frame: .zero, configuration: configuration)
        editorWebView.editorSession = session
        // 主题 CSS 生效前的兜底底色，避免任何早揭示路径闪白。
        let initialDark = NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        editorWebView.underPageBackgroundColor = initialDark ? .black : .white
        webView = editorWebView
        super.init(frame: .zero)

        webView.translatesAutoresizingMaskIntoConstraints = false
        webView.navigationDelegate = self
        webView.allowsMagnification = false
        addSubview(webView)
        NSLayoutConstraint.activate([
            webView.leadingAnchor.constraint(equalTo: leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: trailingAnchor),
            webView.topAnchor.constraint(equalTo: topAnchor),
            webView.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])

        // 深色模式防白闪：前端就绪前保持隐藏，露出系统/主题背景（对齐 Windows 1.1.3）。
        webView.isHidden = true

        session.webView = webView
        // 拖放：图片文件插入，md/txt 打开
        registerForDraggedTypes([.fileURL, .png, .tiff])
        prepareForReload()
        loadEditor()
    }

    // MARK: - NSDraggingDestination

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        .copy
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let options: [NSPasteboard.ReadingOptionKey: Any] = [.urlReadingFileURLsOnly: true]
        guard let urls = sender.draggingPasteboard.readObjects(
            forClasses: [NSURL.self], options: options) as? [URL] else {
            return false
        }
        let imageExtensions = Set(["png", "jpg", "jpeg", "gif", "webp", "bmp"])
        let documentExtensions = Set(["md", "txt", "markdown"])
        for url in urls {
            let ext = url.pathExtension.lowercased()
            if imageExtensions.contains(ext) {
                session?.insertImageFile(at: url)
            } else if documentExtensions.contains(ext) {
                session?.openDocument(at: url)
            }
        }
        return !urls.isEmpty
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    /// 应用主题外观：color-scheme 让 WebKit 以深色绘制 overlay 滚动条/控件，
    /// appearance 同步系统控件。WKWebView 无公开的内部 NSScrollView，无法用 legacy 滚动条。
    func applyThemeAppearance(dark: Bool, legacyScrollers: Bool) {
        webView.appearance = dark ? NSAppearance(named: .darkAqua) : nil
        let scheme = dark ? "dark" : "light"
        webView.evaluateJavaScript("document.documentElement.style.colorScheme = '\(scheme)'") { _, error in
            if let error {
                AppLog.warning("color-scheme 注入失败: \(error.localizedDescription)")
            }
        }
        // 对齐 Windows 1.1.3：页面背景与宿主容器使用主题 --bg-primary，减少加载期明暗跳变。
        if let background = session?.themeBackgroundColor {
            webView.underPageBackgroundColor = background
            wantsLayer = true
            layer?.backgroundColor = background.cgColor
        }
    }

    /// 编辑器前端就绪后揭示 WebView。
    func revealEditor() {
        webView.isHidden = false
        reloadCoverView?.removeFromSuperview()
        reloadCoverView = nil
    }

    /// 重载前先把旧页面从视觉树中移开；否则 WKWebView 会在深色模式中
    /// 先闪一帧未样式化的白色页面。
    func prepareForReload() {
        let background = session?.themeBackgroundColor ?? .windowBackgroundColor
        if reloadCoverView == nil {
            let cover = NSView()
            cover.translatesAutoresizingMaskIntoConstraints = false
            cover.wantsLayer = true
            reloadCoverView = cover
            addSubview(reloadCoverView!, positioned: .above, relativeTo: webView)
            NSLayoutConstraint.activate([
                cover.leadingAnchor.constraint(equalTo: leadingAnchor),
                cover.trailingAnchor.constraint(equalTo: trailingAnchor),
                cover.topAnchor.constraint(equalTo: topAnchor),
                cover.bottomAnchor.constraint(equalTo: bottomAnchor),
            ])
        }
        reloadCoverView?.wantsLayer = true
        reloadCoverView?.layer?.backgroundColor = background.cgColor
        reloadCoverView?.needsDisplay = true
        reloadCoverView?.displayIfNeeded()
        needsDisplay = true
        displayIfNeeded()
        webView.isHidden = true
    }

    /// CSS 注入回执只说明 DOM 已更新；必须等 WebKit 提交新帧后取消隐藏，
    /// 否则深色主题仍可能先呈现一帧样式注入前的白色位图。
    func revealEditorAfterScreenUpdate() {
        let isCovered = reloadCoverView != nil
        if isCovered {
            webView.isHidden = false
        }
        let configuration = WKSnapshotConfiguration()
        configuration.afterScreenUpdates = true
        let target = session?.themeBackgroundColor ?? .windowBackgroundColor
        let startedAt = Date()

        func captureAndCheck() {
            webView.takeSnapshot(with: configuration) { [weak self] image, error in
                DispatchQueue.main.async {
                    guard let self else { return }
                    var pixel: NSColor?
                    if let cgImage = image?.cgImage(
                        forProposedRect: nil, context: nil, hints: nil
                    ) {
                        let rep = NSBitmapImageRep(cgImage: cgImage)
                        let point = CGPoint(
                            x: min(16, max(0, rep.pixelsWide - 1)),
                            y: min(16, max(0, rep.pixelsHigh - 1))
                        )
                        pixel = rep.colorAt(x: Int(point.x), y: Int(point.y))
                    }
                    let elapsed = Date().timeIntervalSince(startedAt)
                    if !ThemeFrameReadinessPolicy.shouldContinueWaiting(
                        didCapture: image != nil,
                        pixel: pixel,
                        target: target,
                        elapsed: elapsed
                    ) {
                        if image == nil, let error {
                            AppLog.warning("等待主题首帧失败，按兜底路径揭示: \(error.localizedDescription)")
                        }
                        self.revealEditor()
                        return
                    }
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.016) {
                        captureAndCheck()
                    }
                }
            }
        }

        captureAndCheck()
    }

    private func loadEditor() {
        guard let editorURL = ResourceLocator.editorWebDirectory else { return }
        let indexPath = editorURL.appendingPathComponent("index.html")
        guard FileManager.default.fileExists(atPath: indexPath.path) else {
            AppLog.error("编辑器 index.html 缺失: \(indexPath.path)")
            return
        }
        webView.load(URLRequest(url: URL(string: "markleaf://editor/index.html")!))
    }

    // MARK: - WKNavigationDelegate

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        AppLog.info("编辑器页面加载完成")
        didFinishLoadOnce = true
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        AppLog.error("编辑器加载失败: \(error.localizedDescription)")
        revealEditor()
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        AppLog.error("编辑器导航失败: \(error.localizedDescription)")
        revealEditor()
    }

    func webView(_ webView: WKWebView,
                 decidePolicyFor navigationAction: WKNavigationAction,
                 decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        // 只允许编辑器自身的 markleaf:// 导航；外部链接交给系统浏览器。
        if let scheme = navigationAction.request.url?.scheme,
           scheme != "markleaf", scheme != "about" {
            if let url = navigationAction.request.url, scheme == "http" || scheme == "https" {
                NSWorkspace.shared.open(url)
            }
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }
}

extension NSColor {
    /// 从 `#RRGGBB` 或 `#RRGGBBAA` 解析颜色（对应 Windows DefaultBackgroundColor 的 hex 解析）。
    convenience init?(hexString: String) {
        let cleaned = hexString.replacingOccurrences(of: "#", with: "")
        guard cleaned.count == 6 || cleaned.count == 8,
              let value = UInt64(cleaned, radix: 16) else { return nil }
        let mask: UInt64 = 0xFF
        if cleaned.count == 8 {
            self.init(srgbRed: CGFloat((value >> 24) & mask) / 255,
                      green: CGFloat((value >> 16) & mask) / 255,
                      blue: CGFloat((value >> 8) & mask) / 255,
                      alpha: CGFloat(value & mask) / 255)
        } else {
            self.init(srgbRed: CGFloat((value >> 16) & mask) / 255,
                      green: CGFloat((value >> 8) & mask) / 255,
                      blue: CGFloat(value & mask) / 255,
                      alpha: 1)
        }
    }
}
