import AppKit
import WebKit

/// 只拦截需要缩放的修饰键滚轮。普通滚轮立即交还 WKWebView，让 WebKit 可以
/// 使用异步滚动路径，而不必等待页面中的阻塞式 JavaScript wheel 监听器。
final class EditorWebView: WKWebView {
    weak var editorSession: EditorSession?
    private var autoscrollTimer: Timer?
    private var autoscrollEvent: NSEvent?
    private var isHandlingSyntheticDrag = false

    override func rightMouseDown(with event: NSEvent) {
        // WebKit's native right-button handling can collapse a drag selection
        // before DOM listeners observe mousedown. Route visual-editor right
        // clicks through the existing trusted contextmenu handler instead.
        let viewPoint = convert(event.locationInWindow, from: nil)
        let clientX = viewPoint.x
        let clientY = isFlipped ? viewPoint.y : bounds.height - viewPoint.y
        let dispatch = """
        ((x, y) => {
          const editor = document.querySelector('#editor');
          const target = document.elementFromPoint(x, y);
          if (!editor || !target || !editor.contains(target)) return false;
          target.dispatchEvent(new MouseEvent('contextmenu', {
            bubbles: true,
            cancelable: true,
            view: window,
            button: 2,
            buttons: 0,
            clientX: x,
            clientY: y,
          }));
          return true;
        })(\(clientX), \(clientY))
        """
        evaluateJavaScript(dispatch) { [weak self] handled, _ in
            guard let self, handled as? Bool != true else { return }
            self.superRightMouseDown(event)
        }
    }

    private func superRightMouseDown(_ event: NSEvent) {
        super.rightMouseDown(with: event)
    }

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

    override func mouseDragged(with event: NSEvent) {
        // Keep the latest drag point even while a scroll/replay pair is in
        // flight. Replaying a stale bottom-edge event would re-extend an
        // upward selection and make the highlight appear stuck.
        autoscrollEvent = event
        guard !isHandlingSyntheticDrag else { return }
        scheduleAutoscroll(event)
        super.mouseDragged(with: event)
    }

    override func mouseUp(with event: NSEvent) {
        stopAutoscroll()
        super.mouseUp(with: event)
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        droppedFileURLs(from: sender).isEmpty
            ? super.draggingEntered(sender)
            : .copy
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        droppedFileURLs(from: sender).isEmpty
            ? super.draggingUpdated(sender)
            : .copy
    }

    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool {
        !droppedFileURLs(from: sender).isEmpty
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let urls = droppedFileURLs(from: sender)
        guard !urls.isEmpty else { return super.performDragOperation(sender) }

        let drop = EditorDropPolicy.classify(urls)
        for url in drop.images {
            editorSession?.insertImageFile(at: url)
        }
        for url in drop.documents {
            editorSession?.openDocument(at: url)
        }
        return !drop.isEmpty
    }

    private func droppedFileURLs(from sender: NSDraggingInfo) -> [URL] {
        sender.draggingPasteboard.readObjects(
            forClasses: [NSURL.self],
            options: [.urlReadingFileURLsOnly: true]
        ) as? [URL] ?? []
    }

    /// WKWebView does not reliably autoscroll a ProseMirror drag selection when
    /// the mouse reaches the viewport edge. Scroll from the host, then replay the
    /// current drag at the same window location so WebKit extends the selection.
    private func scheduleAutoscroll(_ event: NSEvent) {
        autoscrollEvent = event
        if autoscrollTimer != nil { return }
        autoscrollTimer = Timer(timeInterval: 1.0 / 60.0,
                                target: self,
                                selector: #selector(autoscrollTick),
                                userInfo: nil,
                                repeats: true)
        RunLoop.main.add(autoscrollTimer!, forMode: .common)
    }

    @objc private func autoscrollTick() {
        guard let event = autoscrollEvent else {
            stopAutoscroll()
            return
        }
        let viewPoint = convert(event.locationInWindow, from: nil)
        let direction = EditorAutoscrollPolicy.direction(
            y: viewPoint.y,
            viewportHeight: bounds.height,
            edge: 56
        )
        guard direction != 0 else { return }
        let speed = CGFloat(EditorAutoscrollPolicy.speed(overflow: abs(direction)))
        let delta = speed * direction
        isHandlingSyntheticDrag = true
        let checkAndScroll = """
        (() => {
          const element = document.scrollingElement || document.documentElement;
          const maximum = Math.max(0, element.scrollHeight - element.clientHeight);
          const before = element.scrollTop;
          const next = Math.max(0, Math.min(before + \(delta), maximum));
          element.scrollTop = next;
          return next - before;
        })()
        """
        evaluateJavaScript(checkAndScroll) { [weak self] result, _ in
            let scrolled = (result as? Double) ?? 0
            guard let self, self.isHandlingSyntheticDrag, let event = self.autoscrollEvent, scrolled != 0 else {
                self?.stopAutoscroll()
                return
            }
            self.replayDrag(event)
            self.isHandlingSyntheticDrag = false
        }
    }

    private func replayDrag(_ event: NSEvent) {
        super.mouseDragged(with: event)
    }

    private func stopAutoscroll() {
        autoscrollTimer?.invalidate()
        autoscrollTimer = nil
        autoscrollEvent = nil
        isHandlingSyntheticDrag = false
    }
}

enum EditorAutoscrollPolicy {
    static func direction(y: CGFloat, viewportHeight: CGFloat, edge: CGFloat) -> CGFloat {
        let bottomStart = viewportHeight - edge
        if y < edge { return -(edge - y) }
        if y > bottomStart { return y - bottomStart }
        return 0
    }

    static func speed(overflow: CGFloat) -> Int {
        Int(min(24, max(6, round(overflow / 5))))
    }
}

/// 编辑器宿主视图：创建并持有 WKWebView，通过 markleaf:// 自定义 scheme 加载本地编辑器资源
/// （对应 Windows 端 WebView2 + editor.local 虚拟主机映射）。
final class EditorWebContainerView: NSView, WKNavigationDelegate {
    let webView: WKWebView
    private weak var session: EditorSession?
    private var didFinishLoadOnce = false
    private var reloadCoverView: NSView?
    /// WebView 之下的主题色垫层。WKWebView 关闭 drawsBackground 后，内部
    /// scroll view 在局部重绘（如点击原子节点触发的同步失效）时会让未
    /// 重绘区域透出宿主窗口下层内容（壁纸/其它视图），表现为跨段深色伪影。
    /// 垫一层主题色后，任何脏区透出的都是文档底色。
    private var themedBackingView: NSView?
    private(set) var hasThemedFrame = false

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
        let fallbackBackground = NSColor.windowBackgroundColor
        let initialBackground = session.themeBackgroundColor ?? fallbackBackground
        editorWebView.underPageBackgroundColor = initialBackground
        // WKWebView's internal scroll view can briefly expose its default white
        // backing while its viewport is resized (notably during sidebar collapse).
        // Keep that backing transparent so the themed editor surface remains the
        // only color visible during the resize transaction.
        editorWebView.setValue(false, forKey: "drawsBackground")
        webView = editorWebView
        super.init(frame: .zero)

        webView.translatesAutoresizingMaskIntoConstraints = false
        webView.navigationDelegate = self
        webView.allowsMagnification = false

        let backing = NSView()
        backing.translatesAutoresizingMaskIntoConstraints = false
        backing.wantsLayer = true
        backing.layer?.backgroundColor = initialBackground.cgColor
        addSubview(backing, positioned: .below, relativeTo: nil)
        themedBackingView = backing

        addSubview(webView)
        NSLayoutConstraint.activate([
            webView.leadingAnchor.constraint(equalTo: leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: trailingAnchor),
            webView.topAnchor.constraint(equalTo: topAnchor),
            webView.bottomAnchor.constraint(equalTo: bottomAnchor),
            backing.leadingAnchor.constraint(equalTo: leadingAnchor),
            backing.trailingAnchor.constraint(equalTo: trailingAnchor),
            backing.topAnchor.constraint(equalTo: topAnchor),
            backing.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])

        // 深色模式防白闪：前端就绪前保持隐藏，露出系统/主题背景（对齐 Windows 1.1.3）。
        webView.isHidden = true

        wantsLayer = true
        layer?.backgroundColor = initialBackground.cgColor

        session.webView = webView
        // 拖放：图片文件插入，md/txt 打开
        registerForDraggedTypes([.fileURL, .png, .tiff])
        prepareForReload()
        loadEditor()
    }

    // MARK: - NSDraggingDestination

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        droppedFileURLs(from: sender).isEmpty ? super.draggingEntered(sender) : .copy
    }

    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation {
        droppedFileURLs(from: sender).isEmpty ? super.draggingUpdated(sender) : .copy
    }

    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool {
        !droppedFileURLs(from: sender).isEmpty
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let urls = droppedFileURLs(from: sender)
        guard !urls.isEmpty else { return super.performDragOperation(sender) }

        let drop = EditorDropPolicy.classify(urls)
        for url in drop.images {
            session?.insertImageFile(at: url)
        }
        for url in drop.documents {
            session?.openDocument(at: url)
        }
        return !urls.isEmpty
    }

    private func droppedFileURLs(from sender: NSDraggingInfo) -> [URL] {
        sender.draggingPasteboard.readObjects(
            forClasses: [NSURL.self],
            options: [.urlReadingFileURLsOnly: true]
        ) as? [URL] ?? []
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    /// 应用主题外观：color-scheme 让 WebKit 以深色绘制 overlay 滚动条/控件，
    /// appearance 同步系统控件。WKWebView 无公开的内部 NSScrollView，无法用 legacy 滚动条。
    func applyThemeAppearance(dark: Bool) {
        let themedAppearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        // Keep light mode explicit. Inheriting through nil can lag one
        // appearance-propagation pass behind the parent window during a
        // dark-to-light switch, leaving WebKit controls on the old palette.
        webView.appearance = themedAppearance
        // WKWebView keeps its overlay scrollers in an internal NSScrollView.
        // Updating only the web view appearance leaves the old dark knob/track
        // cached when switching back to light mode, so invalidate both levels.
        let scrollView = webKitScrollView()
        scrollView?.appearance = themedAppearance
        scrollView?.verticalScroller?.appearance = themedAppearance
        scrollView?.horizontalScroller?.appearance = themedAppearance
        scrollView?.needsLayout = true
        scrollView?.layoutSubtreeIfNeeded()
        scrollView?.needsDisplay = true
        scrollView?.verticalScroller?.needsDisplay = true
        scrollView?.horizontalScroller?.needsDisplay = true
        let scheme = dark ? "dark" : "light"
        webView.evaluateJavaScript("document.documentElement.style.colorScheme = '\(scheme)'") { [weak self] _, error in
            if let error {
                AppLog.warning("color-scheme 注入失败: \(error.localizedDescription)")
            }
            // The CSS color-scheme update can trigger a later WebKit repaint;
            // invalidate once more after that transaction has completed.
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                let scrollView = self.webKitScrollView()
                scrollView?.appearance = themedAppearance
                scrollView?.verticalScroller?.appearance = themedAppearance
                scrollView?.verticalScroller?.needsDisplay = true
                scrollView?.needsDisplay = true
            }
        }
        // 对齐 Windows 1.1.3：页面背景与宿主容器使用主题 --bg-primary，减少加载期明暗跳变。
        if let background = session?.themeBackgroundColor {
            webView.underPageBackgroundColor = background
            webView.setValue(false, forKey: "drawsBackground")
            wantsLayer = true
            layer?.backgroundColor = background.cgColor
            // The ready path resolves the saved theme after the cover was
            // created with the catalog default. Repaint it before the frontend
            // acknowledges styles so a tab-transition frame can never expose
            // the previous theme color.
            reloadCoverView?.wantsLayer = true
            reloadCoverView?.layer?.backgroundColor = background.cgColor
        }
    }

    /// WKWebView's AppKit scroll view is private, so locate it through the
    /// normal view tree instead of relying on a platform-specific property.
    private func webKitScrollView() -> NSScrollView? {
        func find(in view: NSView) -> NSScrollView? {
            if let scrollView = view as? NSScrollView { return scrollView }
            for child in view.subviews {
                if let scrollView = find(in: child) { return scrollView }
            }
            return nil
        }
        return find(in: webView)
    }

    /// 编辑器前端就绪后揭示 WebView。
    func revealEditor() {
        hasThemedFrame = true
        wantsLayer = true
        layer?.backgroundColor = (session?.themeBackgroundColor ?? .windowBackgroundColor).cgColor
        if let cover = reloadCoverView {
            cover.wantsLayer = true
            NSAnimationContext.runAnimationGroup({ context in
                context.duration = 0.08
                context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                cover.animator().alphaValue = 0
            }, completionHandler: {
                cover.removeFromSuperview()
            })
            reloadCoverView = nil
        }
        webView.isHidden = false
    }

    /// 重载前先把旧页面从视觉树中移开；否则 WKWebView 会在深色模式中
    /// 先闪一帧未样式化的白色页面。
    func prepareForReload() {
        hasThemedFrame = false
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
        // A newly created session can resolve its saved theme after the fallback
        // cover is installed. Repaint that cover before snapshot validation;
        // otherwise the validator compares WebKit pixels with a stale fallback
        // color and delays reveal until the 1-second timeout.
        wantsLayer = true
        layer?.backgroundColor = target.cgColor
        reloadCoverView?.wantsLayer = true
        reloadCoverView?.layer?.backgroundColor = target.cgColor
        themedBackingView?.wantsLayer = true
        themedBackingView?.layer?.backgroundColor = target.cgColor
        webView.underPageBackgroundColor = target
        webView.window?.backgroundColor = target
        let startedAt = Date()
        var stableReadyCount = 0

        func captureAndCheck() {
            webView.takeSnapshot(with: configuration) { [weak self] image, error in
                DispatchQueue.main.async {
                    guard let self else { return }
                    var pixels: [NSColor?] = []
                    if let cgImage = image?.cgImage(
                        forProposedRect: nil, context: nil, hints: nil
                    ) {
                        let rep = NSBitmapImageRep(cgImage: cgImage)
                        let width = max(1, rep.pixelsWide)
                        let height = max(1, rep.pixelsHigh)
                        let inset: CGFloat = 16
                        let samplePoints = [
                            CGPoint(x: inset, y: inset),
                            CGPoint(x: Double(width) * 0.25, y: inset),
                            CGPoint(x: Double(width) * 0.5, y: inset),
                            CGPoint(x: Double(width) * 0.75, y: inset),
                            CGPoint(x: Double(width) - inset, y: inset),
                            CGPoint(x: inset, y: Double(height) * 0.5),
                            CGPoint(x: Double(width) - inset, y: Double(height) * 0.5),
                            CGPoint(x: inset, y: Double(height) - inset),
                            CGPoint(x: Double(width) * 0.5, y: Double(height) - inset),
                            CGPoint(x: Double(width) - inset, y: Double(height) - inset),
                        ]
                        pixels = samplePoints.map { point in
                            rep.colorAt(
                                x: min(width - 1, max(0, Int(point.x))),
                                y: min(height - 1, max(0, Int(point.y)))
                            )
                        }
                    }
                    let elapsed = Date().timeIntervalSince(startedAt)
                    if ThemeFrameReadinessPolicy.isReady(pixels: pixels, target: target) {
                        stableReadyCount += 1
                    } else {
                        stableReadyCount = 0
                    }
                    if !ThemeFrameReadinessPolicy.shouldContinueWaiting(
                        didCapture: image != nil,
                        pixels: pixels,
                        target: target,
                        elapsed: elapsed,
                        matchingStableCount: stableReadyCount
                    ) {
                        if image == nil, let error {
                            AppLog.warning("等待主题首帧失败，按兜底路径揭示: \(error.localizedDescription)")
                        } else if elapsed >= ThemeFrameReadinessPolicy.timeout {
                            AppLog.warning("主题首帧校验超时，按兜底路径揭示")
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
