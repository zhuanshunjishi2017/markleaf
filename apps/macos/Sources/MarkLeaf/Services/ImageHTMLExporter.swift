import AppKit
import WebKit

/// Render each slice at the requested output resolution, independent of display scale.
final class ImageHTMLExporter: NSObject, WKNavigationDelegate {
    private var webView: WKWebView?
    private var completion: ((Result<[URL], Error>) -> Void)?
    private var saveBaseURL: URL?
    private var options: ExportOptions?
    private var strongSelf: ImageHTMLExporter?
    private var watchdog: DispatchWorkItem?
    private var urls: [URL] = []

    enum ImageExportError: LocalizedError {
        case timeout, snapshotFailed, invalidOptions, cancelled

        var errorDescription: String? {
            switch self {
            case .timeout: return L10n.t("图像导出超时")
            case .snapshotFailed: return L10n.t("无法生成图像内容")
            case .invalidOptions: return L10n.t("图像导出参数无效")
            case .cancelled: return L10n.t("已取消图像预览")
            }
        }
    }

    func export(
        html: String,
        options: ExportOptions,
        saveBaseURL: URL,
        completion: @escaping (Result<[URL], Error>) -> Void
    ) {
        guard ImageExportPolicy.isValid(options) else {
            completion(.failure(ImageExportError.invalidOptions))
            return
        }
        self.completion = completion
        self.saveBaseURL = saveBaseURL
        self.options = options
        self.strongSelf = self
        urls = []

        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        // Start small so scrollHeight measures content, including short documents.
        let webView = WKWebView(frame: NSRect(x: 0, y: 0, width: options.imageContentWidth, height: 1),
                                configuration: configuration)
        webView.navigationDelegate = self
        self.webView = webView
        webView.loadHTMLString(html, baseURL: nil)
        let watchdog = DispatchWorkItem { [weak self] in self?.finish(.failure(ImageExportError.timeout)) }
        self.watchdog = watchdog
        DispatchQueue.main.asyncAfter(deadline: .now() + 60, execute: watchdog)
    }

    func cancel() {
        finish(.failure(ImageExportError.cancelled))
    }

    private func finish(_ result: Result<[URL], Error>) {
        guard let completion else { return }
        self.completion = nil
        watchdog?.cancel()
        webView?.stopLoading()
        webView?.navigationDelegate = nil
        webView = nil
        completion(result)
        strongSelf = nil
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
        finish(.failure(error))
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        finish(.failure(error))
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        // Fonts and images can change wrapping and document height after didFinish.
        webView.callAsyncJavaScript("""
            await document.fonts.ready;
            await Promise.all(Array.from(document.images, image => image.decode().catch(() => {})));
            return Math.max(document.documentElement.scrollHeight, document.body.scrollHeight);
            """, arguments: [:], in: nil, in: .page) { [weak self] result in
            guard let self, self.webView === webView, let options = self.options else { return }
            guard case .success(let value) = result, let height = value as? Double,
                  height.isFinite, height > 0, height * options.imageScale < Double(Int.max) else {
                self.finish(.failure(ImageExportError.snapshotFailed))
                return
            }
            let pixelHeight = Int(ceil(height * options.imageScale))
            webView.setFrameSize(NSSize(width: options.imageContentWidth, height: height))
            self.captureSlice(webView, index: 0, totalPixelHeight: pixelHeight)
        }
    }

    private func captureSlice(_ webView: WKWebView, index: Int, totalPixelHeight: Int) {
        guard self.webView === webView, let options, let baseURL = saveBaseURL else { return }
        let maxHeight = Int(options.imageMaxHeight)
        let pixelY = index * maxHeight
        let pixelHeight = min(maxHeight, totalPixelHeight - pixelY)
        let pixelWidth = Int(options.imageContentWidth * options.imageScale)
        let sliceCount = (totalPixelHeight - 1) / maxHeight + 1
        let configuration = WKSnapshotConfiguration()
        configuration.rect = NSRect(x: 0, y: Double(pixelY) / options.imageScale,
                                    width: options.imageContentWidth,
                                    height: Double(pixelHeight) / options.imageScale)
        // WebKit's snapshotWidth is in points; its bitmap includes backing scale.
        let backingScale = webView.window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 1
        configuration.snapshotWidth = NSNumber(value: Double(pixelWidth) / backingScale)
        webView.takeSnapshot(with: configuration) { [weak self] image, error in
            guard let self, self.webView === webView else { return }
            do {
                guard let image else { throw error ?? ImageExportError.snapshotFailed }
                let target = sliceCount == 1 ? baseURL : baseURL.deletingLastPathComponent()
                    .appendingPathComponent("\(baseURL.deletingPathExtension().lastPathComponent)-\(index + 1).\(baseURL.pathExtension)")
                try autoreleasepool {
                    guard let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil),
                          let context = CGContext(data: nil, width: pixelWidth, height: pixelHeight,
                              bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
                        throw ImageExportError.snapshotFailed
                    }
                    let rect = CGRect(x: 0, y: 0, width: pixelWidth, height: pixelHeight)
                    // JPEG has no alpha; composite transparent documents onto white.
                    if options.imageFormat == "jpg" {
                        context.setFillColor(NSColor.white.cgColor)
                        context.fill(rect)
                    }
                    context.interpolationQuality = .high
                    context.draw(cgImage, in: rect)
                    guard let bitmap = context.makeImage(),
                          let data = NSBitmapImageRep(cgImage: bitmap).representation(
                            using: options.imageFormat == "jpg" ? .jpeg : .png,
                            properties: [.compressionFactor: options.imageJpegQuality / 100]) else {
                        throw ImageExportError.snapshotFailed
                    }
                    try data.write(to: target, options: .atomic)
                }
                self.urls.append(target)
                if index + 1 < sliceCount {
                    self.captureSlice(webView, index: index + 1, totalPixelHeight: totalPixelHeight)
                } else {
                    self.finish(.success(self.urls))
                }
            } catch {
                self.finish(.failure(error))
            }
        }
    }
}
