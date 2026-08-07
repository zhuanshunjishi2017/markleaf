import AppKit
import WebKit

/// 编辑器宿主视图：创建并持有 WKWebView，通过 markleaf:// 自定义 scheme 加载本地编辑器资源
/// （对应 Windows 端 WebView2 + editor.local 虚拟主机映射）。
final class EditorWebContainerView: NSView, WKNavigationDelegate {
    let webView: WKWebView
    private weak var session: EditorSession?
    private var didFinishLoadOnce = false

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

        webView = WKWebView(frame: .zero, configuration: configuration)
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

        session.webView = webView
        // 拖放：图片文件插入，md/txt 打开
        registerForDraggedTypes([.fileURL, .png, .tiff])
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
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        AppLog.error("编辑器导航失败: \(error.localizedDescription)")
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
