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
            // The shared image-export stylesheet keeps scrolling enabled for
            // Windows chunk capture. WKWebView must instead render the whole
            // document as one surface before taking its full-page snapshot;
            // otherwise the scroll layer can repeat the last viewport rows.
            document.documentElement.style.setProperty('overflow', 'visible', 'important');
            document.body.style.setProperty('overflow', 'visible', 'important');
            document.body.classList.contains('markleaf-export-image') && document.body.style.setProperty('height', 'auto', 'important');
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
            webView.layoutSubtreeIfNeeded()
            DispatchQueue.main.async { [weak self, weak webView] in
                guard let self, let webView else { return }
                self.captureFullPage(webView, totalPixelHeight: pixelHeight)
            }
        }
    }

    private func captureFullPage(_ webView: WKWebView, totalPixelHeight: Int) {
        guard self.webView === webView, let options, let baseURL = saveBaseURL else { return }
        let pixelWidth = Int(options.imageContentWidth * options.imageScale)
        let configuration = WKSnapshotConfiguration()
        configuration.rect = NSRect(x: 0, y: 0, width: options.imageContentWidth,
                                    height: Double(totalPixelHeight) / options.imageScale)
        // WebKit's snapshotWidth is in points; its bitmap includes backing scale.
        let backingScale = webView.window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 1
        configuration.snapshotWidth = NSNumber(value: Double(pixelWidth) / backingScale)
        webView.takeSnapshot(with: configuration) { [weak self] image, error in
            guard let self, self.webView === webView else { return }
            do {
                guard let image else { throw error ?? ImageExportError.snapshotFailed }
                try autoreleasepool {
                    guard let cgImage = image.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
                        throw ImageExportError.snapshotFailed
                    }
                    let outputWidth = cgImage.width
                    let outputHeight = cgImage.height
                    let maxHeight = Int(options.imageMaxHeight)
                    let sliceCount = (outputHeight - 1) / maxHeight + 1
                    for index in 0..<sliceCount {
                        let pixelY = index * maxHeight
                        let sliceHeight = min(maxHeight, outputHeight - pixelY)
                        let target = sliceCount == 1 ? baseURL : baseURL.deletingLastPathComponent()
                            .appendingPathComponent(String(format: "%@-%02d.%@",
                                baseURL.deletingPathExtension().lastPathComponent, index + 1, baseURL.pathExtension))
                        let cropY = outputHeight - pixelY - sliceHeight
                        guard let slice = cgImage.cropping(to: CGRect(x: 0, y: cropY, width: outputWidth, height: sliceHeight)),
                              let context = CGContext(data: nil, width: outputWidth, height: sliceHeight,
                                  bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
                            throw ImageExportError.snapshotFailed
                        }
                        let rect = CGRect(x: 0, y: 0, width: outputWidth, height: sliceHeight)
                        if options.imageFormat == "jpg" {
                            context.setFillColor(NSColor.white.cgColor)
                            context.fill(rect)
                        }
                        context.interpolationQuality = .high
                        context.draw(slice, in: rect)
                        guard let bitmap = context.makeImage(),
                              let data = NSBitmapImageRep(cgImage: bitmap).representation(
                                using: options.imageFormat == "jpg" ? .jpeg : .png,
                                properties: [.compressionFactor: options.imageJpegQuality / 100]) else {
                            throw ImageExportError.snapshotFailed
                        }
                        try data.write(to: target, options: .atomic)
                        self.urls.append(target)
                    }
                }
                self.finish(.success(self.urls))
            } catch {
                self.finish(.failure(error))
            }
        }
    }
}
