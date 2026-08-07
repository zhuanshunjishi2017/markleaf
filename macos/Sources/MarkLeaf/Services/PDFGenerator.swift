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
/// 使用离屏 WKWebView + NSPrintOperation（纸张/边距/方向）；打印失败时回退 createPDF。
final class PDFGenerator: NSObject, WKNavigationDelegate {
    private var completion: ((Result<Data, Error>) -> Void)?
    private var webView: WKWebView?
    private var printInfo: NSPrintInfo?
    private var watchdog: DispatchWorkItem?
    // 自持有：直到生成完成才释放（调用方为临时对象，无强引用会提前释放导致 delegate 失效）
    private var strongSelf: PDFGenerator?

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

        let adjustedHTML = Self.fixLocalImagePaths(in: html)
        webView.loadHTMLString(adjustedHTML, baseURL: nil)
        AppLog.info("PDFGenerator: HTML 已加载 (\(html.count) 字符)")
    }

    private static func fixLocalImagePaths(in html: String) -> String {
        html.replacingOccurrences(of: "src=\"/", with: "src=\"file:///")
    }

    // MARK: - WKNavigationDelegate

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        AppLog.info("PDFGenerator: 页面渲染完成")
        guard let printInfo else { return }
        DispatchQueue.main.async { [weak self] in
            self?.runPrint(webView: webView, printInfo: printInfo)
        }
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        AppLog.error("PDFGenerator: 导航失败 \(error.localizedDescription)")
        finish(.failure(error))
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        AppLog.error("PDFGenerator: 初始导航失败 \(error.localizedDescription)")
        finish(.failure(error))
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
