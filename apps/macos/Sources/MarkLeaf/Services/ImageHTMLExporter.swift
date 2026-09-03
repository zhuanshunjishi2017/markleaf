import AppKit
import WebKit

/// 将导出 HTML 渲染为长图；高度超过单张上限时按像素高度拆分。
final class ImageHTMLExporter: NSObject, WKNavigationDelegate {
    private var webView: WKWebView?
    private var completion: ((Result<[URL], Error>) -> Void)?
    private var saveBaseURL: URL?
    private var options: ExportOptions?
    private var strongSelf: ImageHTMLExporter?

    enum ImageExportError: LocalizedError {
        case timeout
        case snapshotFailed

        var errorDescription: String? {
            switch self {
            case .timeout: return "图像导出超时"
            case .snapshotFailed: return "无法生成图像内容"
            }
        }
    }

    func export(
        html: String,
        options: ExportOptions,
        saveBaseURL: URL,
        completion: @escaping (Result<[URL], Error>) -> Void
    ) {
        self.completion = completion
        self.saveBaseURL = saveBaseURL
        self.options = options
        self.strongSelf = self

        let configuration = WKWebViewConfiguration()
        let width = max(320, min(4000, options.imageContentWidth))
        let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: width, height: 800), configuration: configuration)
        webView.navigationDelegate = self
        self.webView = webView
        webView.loadHTMLString(html, baseURL: nil)

        let watchdog = DispatchWorkItem { [weak self] in
            self?.finish(.failure(ImageExportError.timeout))
        }
        self.watchdog = watchdog
        DispatchQueue.main.asyncAfter(deadline: .now() + 20, execute: watchdog)
    }

    private var watchdog: DispatchWorkItem?

    private func finish(_ result: Result<[URL], Error>) {
        watchdog?.cancel()
        webView?.removeFromSuperview()
        webView = nil
        completion?(result)
        completion = nil
        strongSelf = nil
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        let width = max(320, min(4000, options?.imageContentWidth ?? 1200))
        webView.evaluateJavaScript("Math.max(document.documentElement.scrollHeight, document.body.scrollHeight)") {
            [weak self] result, _ in
            let logicalHeight = max(800, (result as? Double) ?? 800)
            webView.frame = NSRect(x: 0, y: 0, width: width, height: logicalHeight)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) {
                let rect = NSRect(x: 0, y: 0, width: width, height: logicalHeight)
                let configuration = WKSnapshotConfiguration()
                configuration.rect = rect
                webView.takeSnapshot(with: configuration) { [weak self] image, _ in
                    guard let self, let image else {
                        self?.finish(.failure(ImageExportError.snapshotFailed))
                        return
                    }
                    self.writeImages(from: image)
                }
            }
        }
    }

    private func writeImages(from image: NSImage) {
        guard let options,
              let baseURL = saveBaseURL,
              let cgImage = image.cgImage(
                forProposedRect: nil, context: nil, hints: nil
              ) else {
            finish(.failure(ImageExportError.snapshotFailed))
            return
        }

        let scale = max(1, min(4, options.imageScale))
        let slicePixelHeight = Int((options.imageMaxHeight / scale).rounded())
        let slices = max(1, Int(ceil(Double(cgImage.height) / Double(slicePixelHeight))))
        let isJPEG = options.imageFormat == "jpg"
        let fileType: NSBitmapImageRep.FileType = isJPEG ? .jpeg : .png

        do {
            var urls: [URL] = []
            for index in 0..<slices {
                let y = index * slicePixelHeight
                let height = min(slicePixelHeight, cgImage.height - y)
                guard let cropped = cgImage.cropping(to: CGRect(
                    x: 0, y: y, width: cgImage.width, height: height
                )) else { continue }
                let rep = NSBitmapImageRep(cgImage: cropped)
                guard let data = rep.representation(using: fileType, properties: [
                    .compressionFactor: isJPEG ? options.imageJpegQuality / 100 : 1.0,
                ]) else { continue }

                let url = slices == 1
                    ? baseURL
                    : URL(fileURLWithPath: "\(baseURL.deletingPathExtension().path)-\(index + 1).\(baseURL.pathExtension)")
                try data.write(to: url)
                urls.append(url)
            }
            guard urls.isEmpty == false else {
                finish(.failure(ImageExportError.snapshotFailed))
                return
            }
            finish(.success(urls))
        } catch {
            finish(.failure(error))
        }
    }
}
