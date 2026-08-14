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
        let printInfo = NSPrintInfo()
        printInfo.paperSize = NSSize(
            width: landscape ? size.height : size.width,
            height: landscape ? size.width : size.height)
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
        let pointsPerInch: CGFloat = 72
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
        completion: @escaping (Result<Bool, Error>) -> Void
    ) {
        self.printCompletion = completion
        self.strongSelf = self
        self.targetWindow = window
        AppLog.info("PDFGenerator: 启动系统打印面板 (纸张 \(paperSize.rawValue), 横向=\(landscape))")

        let size = paperSize.sizeInches
        let info = NSPrintInfo()
        info.paperSize = NSSize(
            width: landscape ? size.height : size.width,
            height: landscape ? size.width : size.height)
        info.orientation = landscape ? .landscape : .portrait
        info.topMargin = margins.top
        info.bottomMargin = margins.bottom
        info.leftMargin = margins.left
        info.rightMargin = margins.right
        info.horizontalPagination = .fit
        info.verticalPagination = .automatic
        info.isHorizontallyCentered = false
        info.isVerticallyCentered = false
        self.printInfo = info

        let configuration = WKWebViewConfiguration()
        #if DEBUG
        configuration.preferences.setValue(true, forKey: "developerExtrasEnabled")
        #endif
        // 初始帧按一页大小；printOperation(with:) 会自行按打印信息跨页分页。
        let pointsPerInch: CGFloat = 72
        let pageWidth = CGFloat(landscape ? size.height : size.width) * pointsPerInch
        let pageHeight = CGFloat(landscape ? size.width : size.height) * pointsPerInch
        let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: pageWidth, height: pageHeight), configuration: configuration)
        webView.navigationDelegate = self
        self.webView = webView

        // 30 秒看门狗：仅覆盖“面板未弹出”的异常；面板弹出后取消。
        let watchdog = DispatchWorkItem { [weak self] in
            AppLog.error("PDFGenerator: 打印面板启动超时")
            self?.finishPrint(.failure(PrintError.timeout))
        }
        self.watchdog = watchdog
        DispatchQueue.main.asyncAfter(deadline: .now() + 30, execute: watchdog)

        // 系统打印面板负责纸张与边距，不注入 @page 边距；仅固定本地图片路径。
        let adjustedHTML = Self.fixLocalImagePaths(in: html)
        webView.loadHTMLString(adjustedHTML, baseURL: nil)
        AppLog.info("PDFGenerator: 打印 HTML 已加载 (\(html.count) 字符)")
    }

    private func runPrintPanel(webView: WKWebView) {
        guard let printInfo, let targetWindow else {
            finishPrint(.failure(PrintError.noWindow))
            return
        }
        // WKWebView 需要挂在窗口并完成布局，printOperation(with:) 才能正确渲染整篇内容并跨页分页。
        if webView.superview == nil {
            webView.frame.origin = NSPoint(x: -100000, y: 0)
            targetWindow.contentView?.addSubview(webView, positioned: .below, relativeTo: nil)
        }
        webView.layoutSubtreeIfNeeded()
        webView.displayIfNeeded()

        let operation = webView.printOperation(with: printInfo)
        operation.showsPrintPanel = true
        operation.showsProgressPanel = true
        watchdog?.cancel()
        AppLog.info("PDFGenerator: 弹出系统打印面板")
        operation.runModal(for: targetWindow, delegate: self, didRun: #selector(self.printDidRun(_:success:contextInfo:)), contextInfo: nil)
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
            self.webView?.removeFromSuperview()
            self.webView = nil
            self.printInfo = nil
            self.targetWindow = nil
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
