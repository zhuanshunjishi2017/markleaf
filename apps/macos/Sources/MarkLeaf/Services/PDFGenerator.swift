import AppKit
import WebKit

/// 纸张尺寸（毫米）→ 英寸，对应 C# EditorHostController.PaperSizeToInches。
enum PaperSize: String, CaseIterable {
    case a4 = "A4"
    case a3 = "A3"
    case a5 = "A5"
    case letter = "Letter"
    case legal = "Legal"

    var sizeInches: (width: Double, height: Double) {
        let (wMm, hMm): (Double, Double) = switch self {
        case .a3: (297.0, 420.0)
        case .a5: (148.0, 210.0)
        case .letter: (215.9, 279.4)
        case .legal: (215.9, 355.6)
        default: (210.0, 297.0)
        }
        return (wMm / 25.4, hMm / 25.4)
    }
}

struct ExportMargins {
    var top: Double = 18
    var bottom: Double = 18
    var left: Double = 15
    var right: Double = 15
}

/// 将编辑器导出的 HTML 渲染为 PDF（对应 C# PrintExportToPdfAsync）。
/// 离屏渲染导出：generatePDF 用 createPDF 生成 PDF；printPDF 走系统打印面板（纸张/边距/方向由面板控制）。
final class PDFGenerator: NSObject, WKNavigationDelegate {
    private var completion: ((Result<Data, Error>) -> Void)?
    private var printCompletion: ((Result<Bool, Error>) -> Void)?
    private var webView: WKWebView?
    private var printInfo: NSPrintInfo?
    private var targetWindow: NSWindow?
    private var showsPrintPanel = true
    private var watchdog: DispatchWorkItem?
    // 自持有：直到生成完成才释放（调用方为临时对象，无强引用会提前释放导致 delegate 失效）
    private var strongSelf: PDFGenerator?

    enum PrintError: LocalizedError {
        case timeout
        case noWindow

        var errorDescription: String? {
            switch self {
            case .timeout: return "打印面板启动超时"
            case .noWindow: return "缺少宿主窗口"
            }
        }
    }

    func generatePDF(
        html: String,
        paperSize: PaperSize,
        landscape: Bool,
        margins: ExportMargins,
        completion: @escaping (Result<Data, Error>) -> Void
    ) {
        self.completion = completion
        self.strongSelf = self
        AppLog.info("PDFGenerator: 启动 (纸张 \(paperSize.rawValue), 横向=\(landscape))")

        let size = paperSize.sizeInches
        let pointsPerInch: CGFloat = 72
        let printInfo = NSPrintInfo()
        printInfo.paperSize = NSSize(
            width: (landscape ? size.height : size.width) * pointsPerInch,
            height: (landscape ? size.width : size.height) * pointsPerInch)
        printInfo.orientation = landscape ? .landscape : .portrait
        printInfo.topMargin = margins.top
        printInfo.bottomMargin = margins.bottom
        printInfo.leftMargin = margins.left
        printInfo.rightMargin = margins.right
        printInfo.horizontalPagination = .fit
        printInfo.verticalPagination = .automatic
        printInfo.isHorizontallyCentered = false
        printInfo.isVerticallyCentered = false
        self.printInfo = printInfo

        let configuration = WKWebViewConfiguration()
        #if DEBUG
        configuration.preferences.setValue(true, forKey: "developerExtrasEnabled")
        #endif
        // 按纸张尺寸设置离屏 webview 帧（72dpi 点），createPDF 按该尺寸分页
        let pageWidth = CGFloat(landscape ? size.height : size.width) * pointsPerInch
        let pageHeight = CGFloat(landscape ? size.width : size.height) * pointsPerInch
        let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: pageWidth, height: pageHeight), configuration: configuration)
        webView.navigationDelegate = self
        self.webView = webView

        // 15 秒看门狗
        let watchdog = DispatchWorkItem { [weak self] in
            AppLog.error("PDFGenerator: 超时，回退 createPDF")
            self?.fallbackToCreatePDF()
        }
        self.watchdog = watchdog
        DispatchQueue.main.asyncAfter(deadline: .now() + 15, execute: watchdog)

        // 注入 CSS @page 规则：边距交给 CSS（createPDF 忽略 NSPrintInfo 边距），
        // 并让主题背景（--bg-primary）铺满整页（对齐 Windows fccc7ad 的 PDF 修复）。
        let pageRule = Self.injectPageMargins(into: html, margins: margins)
        let adjustedHTML = Self.fixLocalImagePaths(in: pageRule)
        webView.loadHTMLString(adjustedHTML, baseURL: nil)
        AppLog.info("PDFGenerator: HTML 已加载 (\(html.count) 字符)")
    }

    /// 注入 @page 边距 + 背景色（对齐 Windows EditorHostController.PrintExportToPdfAsync）。
    /// createPDF 会忽略 NSPrintInfo 的边距设置，因此边距必须通过 CSS @page 生效。
    private static func injectPageMargins(into html: String, margins: ExportMargins) -> String {
        let rule = "@page { margin: \(margins.top)mm \(margins.right)mm \(margins.bottom)mm \(margins.left)mm; background-color: var(--bg-primary); }\nhtml { background: var(--bg-primary); }"
        if let range = html.range(of: "</style>") {
            return html.replacingCharacters(in: range, with: rule + "\n</style>")
        }
        // 兜底：无 <style> 时在 <head> 末尾（或文档开头）插入 <style>
        if let headRange = html.range(of: "</head>") {
            let style = "<style>\n" + rule + "\n</style>\n"
            return html.replacingCharacters(in: headRange, with: style + "</head>")
        }
        return "<style>\n" + rule + "\n</style>\n" + html
    }

    private static func fixLocalImagePaths(in html: String) -> String {
        html.replacingOccurrences(of: "src=\"/", with: "src=\"file:///")
    }

    // MARK: - WKNavigationDelegate

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        AppLog.info("PDFGenerator: 页面渲染完成")
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            if self.printCompletion != nil {
                self.runPrintPanel(webView: webView)
            } else if let printInfo = self.printInfo {
                self.runPrint(webView: webView, printInfo: printInfo)
            }
        }
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        AppLog.error("PDFGenerator: 导航失败 \(error.localizedDescription)")
        if printCompletion != nil {
            finishPrint(.failure(error))
        } else {
            finish(.failure(error))
        }
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        AppLog.error("PDFGenerator: 初始导航失败 \(error.localizedDescription)")
        if printCompletion != nil {
            finishPrint(.failure(error))
        } else {
            finish(.failure(error))
        }
    }

    // MARK: - 系统打印面板（导出 PDF 走系统打印面板，纸张/边距/方向由面板控制）

    /// 将导出 HTML 载入离屏 WKWebView，并弹出系统打印面板（可“存储为 PDF”）。
    /// completion(.success(true)) = 已打印或已保存为 PDF；.success(false) = 用户取消。
    func printPDF(
        html: String,
        paperSize: PaperSize,
        landscape: Bool,
        margins: ExportMargins,
        window: NSWindow,
        showsPanel: Bool = true,
        saveURL: URL? = nil,
        completion: @escaping (Result<Bool, Error>) -> Void
    ) {
        self.printCompletion = completion
        self.strongSelf = self
        self.targetWindow = window
        self.showsPrintPanel = showsPanel
        AppLog.info("PDFGenerator: 启动系统打印面板 (纸张 \(paperSize.rawValue), 横向=\(landscape))")

        let size = paperSize.sizeInches
        let pointsPerInch: CGFloat = 72
        let info = NSPrintInfo()
        info.paperSize = NSSize(
            width: (landscape ? size.height : size.width) * pointsPerInch,
            height: (landscape ? size.width : size.height) * pointsPerInch)
        info.orientation = landscape ? .landscape : .portrait
        info.topMargin = margins.top
        info.bottomMargin = margins.bottom
        info.leftMargin = margins.left
        info.rightMargin = margins.right
        info.horizontalPagination = .fit
        info.verticalPagination = .automatic
        info.isHorizontallyCentered = false
        info.isVerticallyCentered = false
        if !showsPanel, let saveURL {
            info.jobDisposition = .save
            info.dictionary()[NSPrintInfo.AttributeKey.jobSavingURL] = saveURL
        }
        self.printInfo = info

        // 初始帧按一页大小；printOperation(with:) 会自行按打印信息跨页分页。
        // 复用共享打印宿主窗口/WebView：打印操作在取消后仍可能引用打印视图，
        // 若在此销毁视图会触发 over-release 崩溃（SIGSEGV）。
        let pageWidth = CGFloat(landscape ? size.height : size.width) * pointsPerInch
        let pageHeight = CGFloat(landscape ? size.width : size.height) * pointsPerInch
        let host = PrintHost.shared
        host.window.setContentSize(NSSize(width: pageWidth, height: pageHeight))
        host.webView.frame = NSRect(x: 0, y: 0, width: pageWidth, height: pageHeight)
        let webView = host.webView
        webView.navigationDelegate = self
        self.webView = webView

        // 30 秒看门狗：仅覆盖“面板未弹出”的异常；面板弹出后取消。
        let watchdog = DispatchWorkItem { [weak self] in
            AppLog.error("PDFGenerator: 打印面板启动超时")
            self?.finishPrint(.failure(PrintError.timeout))
        }
        self.watchdog = watchdog
        DispatchQueue.main.asyncAfter(deadline: .now() + 30, execute: watchdog)

        // 系统打印面板负责纸张与边距，不注入 @page 边距；
        // 额外强制输出 CSS 背景色（WebKit 打印默认不渲染背景），并固定本地图片路径。
        let printHTML = Self.forcePrintBackgrounds(in: html)
        let adjustedHTML = Self.fixLocalImagePaths(in: printHTML)
        webView.loadHTMLString(adjustedHTML, baseURL: nil)
        AppLog.info("PDFGenerator: 打印 HTML 已加载 (\(html.count) 字符)")
    }

    /// WebKit 打印默认不输出 CSS 背景色/背景图；强制输出以保持与 createPDF 时代一致。
    private static func forcePrintBackgrounds(in html: String) -> String {
        let rule = "*, *::before, *::after { -webkit-print-color-adjust: exact; print-color-adjust: exact; }"
        if let range = html.range(of: "</style>") {
            return html.replacingCharacters(in: range, with: rule + "\n</style>")
        }
        if let headRange = html.range(of: "</head>") {
            let style = "<style>\n" + rule + "\n</style>\n"
            return html.replacingCharacters(in: headRange, with: style + "</head>")
        }
        return "<style>\n" + rule + "\n</style>\n" + html
    }

    private func runPrintPanel(webView: WKWebView) {
        guard let printInfo, let targetWindow else {
            finishPrint(.failure(PrintError.noWindow))
            return
        }
        // WKWebView 需要挂在窗口中并完成布局，printOperation(with:) 才能渲染整篇内容并跨页分页。
        // 不能放到大幅离屏位置（如 x=-100000），也不能被另一个 WKWebView 完全遮挡，否则输出空白；
        // 共享宿主窗口放在 (-4000,-4000) 离屏，打印面板仍挂在主窗口上。
        webView.layoutSubtreeIfNeeded()
        webView.displayIfNeeded()

        // 离屏窗口中的 WKWebView 需要先完成一次合成渲染，立即打印会得到空白页。
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in
            guard let self else { return }
            let operation = webView.printOperation(with: printInfo)
            operation.showsPrintPanel = self.showsPrintPanel
            operation.showsProgressPanel = self.showsPrintPanel
            self.watchdog?.cancel()
            if self.showsPrintPanel {
                AppLog.info("PDFGenerator: 弹出系统打印面板")
            } else {
                AppLog.info("PDFGenerator: 直接保存 PDF（不弹面板）")
            }
            operation.runModal(for: targetWindow, delegate: self, didRun: #selector(self.printDidRun(_:success:contextInfo:)), contextInfo: nil)
        }
    }

    @objc private func printDidRun(_ printOperation: NSPrintOperation, success: Bool, contextInfo: UnsafeMutableRawPointer?) {
        AppLog.info("PDFGenerator: 打印面板结束 success=\(success)")
        finishPrint(.success(success))
    }

    private func finishPrint(_ result: Result<Bool, Error>) {
        watchdog?.cancel()
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            self.printCompletion?(result)
            self.printCompletion = nil
            // 打印窗口/WebView 由共享宿主持有并跨导出复用；这里只解除本实例的引用，
            // 绝不在打印操作仍可能引用视图时销毁它们，避免 over-release 崩溃。
            self.webView?.navigationDelegate = nil
            self.webView = nil
            self.printInfo = nil
            self.targetWindow = nil
            self.showsPrintPanel = true
            self.strongSelf = nil
        }
    }

    private func runPrint(webView: WKWebView, printInfo: NSPrintInfo) {
        // NSPrintOperation.run() 在 WKWebView 无头场景会挂起等保存面板；
        // 改用 createPDF（按 webview 帧尺寸分页），可靠且无需交互。
        watchdog?.cancel()
        AppLog.info("PDFGenerator: 调用 createPDF (帧 \(Int(webView.frame.width))x\(Int(webView.frame.height)))")
        webView.createPDF(configuration: WKPDFConfiguration()) { result in
            DispatchQueue.main.async {
                switch result {
                case .success(let data):
                    AppLog.info("PDFGenerator: 成功 (\(data.count) bytes)")
                    self.finish(.success(data))
                case .failure(let error):
                    AppLog.error("PDFGenerator: createPDF 失败 \(error.localizedDescription)")
                    self.finish(.failure(error))
                }
            }
        }
    }

    /// 回退：createPDF（无纸张/边距控制，但保证产出 PDF）。
    private func fallbackToCreatePDF() {
        watchdog?.cancel()
        guard let webView, let completion else { return }
        self.completion = nil
        webView.createPDF(configuration: WKPDFConfiguration()) { result in
            DispatchQueue.main.async {
                switch result {
                case .success(let data):
                    AppLog.info("PDFGenerator: createPDF 回退成功 (\(data.count) bytes)")
                    completion(.success(data))
                case .failure(let error):
                    AppLog.error("PDFGenerator: createPDF 回退失败 \(error.localizedDescription)")
                    completion(.failure(error))
                }
                self.strongSelf = nil
            }
        }
    }

    private func finish(_ result: Result<Data, Error>) {
        watchdog?.cancel()
        DispatchQueue.main.async { [weak self] in
            self?.completion?(result)
            self?.completion = nil
            self?.webView = nil
            self?.strongSelf = nil
        }
    }
}

/// 系统打印面板专用的离屏宿主窗口。
/// 每次导出都会创建新的 NSPrintOperation，操作结束后（尤其用户点击“取消”时）
/// WebKit 仍可能在 autorelease 排空阶段引用打印视图；如果此时销毁宿主窗口/WebView，
/// 会触发 over-release 崩溃（SIGSEGV）。因此让窗口与 WebView 跨导出复用，
/// 打印结束后只解除当前生成器的引用，视图本身保持存活。
private final class PrintHost {
    static let shared = PrintHost()

    let window: NSWindow
    let webView: WKWebView

    private init() {
        let configuration = WKWebViewConfiguration()
        #if DEBUG
        configuration.preferences.setValue(true, forKey: "developerExtrasEnabled")
        #endif
        let initialFrame = NSRect(x: 0, y: 0, width: 800, height: 800)
        let webView = WKWebView(frame: initialFrame, configuration: configuration)
        let window = NSWindow(
            contentRect: initialFrame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false)
        window.contentView?.addSubview(webView)
        window.setFrameOrigin(NSPoint(x: -4000, y: -4000))
        // WKWebView 需要所在窗口保持“已上屏”状态才能合成渲染，否则打印输出空白页。
        window.orderFrontRegardless()
        self.window = window
        self.webView = webView
    }
}
