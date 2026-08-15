import Foundation
import WebKit

/// 用自定义 scheme `markleaf://` 承载编辑器静态资源，
/// 对应 Windows 端 WebView2 的 `editor.local` 虚拟主机映射。
/// 自定义 scheme 拥有稳定源，使 index.html 的 CSP `'self'` 规则正常工作。
final class EditorSchemeHandler: NSObject, WKURLSchemeHandler {
    private let root: URL
    private let queue = DispatchQueue(label: "com.markleaf.scheme")
    private var activeTasks = Set<ObjectIdentifier>()

    init(root: URL) {
        self.root = root.standardizedFileURL
    }

    func webView(_ webView: WKWebView, start urlSchemeTask: WKURLSchemeTask) {
        let id = ObjectIdentifier(urlSchemeTask)
        queue.async { [weak self] in
            guard let self else { return }
            self.activeTasks.insert(id)
            self.handle(task: urlSchemeTask, id: id)
        }
    }

    func webView(_ webView: WKWebView, stop urlSchemeTask: WKURLSchemeTask) {
        let id = ObjectIdentifier(urlSchemeTask)
        queue.async { [weak self] in
            self?.activeTasks.remove(id)
        }
    }

    private func handle(task: WKURLSchemeTask, id: ObjectIdentifier) {
        guard let url = task.request.url else {
            finish(task: task, id: id, status: 400, mime: "text/plain", data: Data("bad request".utf8))
            return
        }

        var relativePath = url.path
        if relativePath.isEmpty || relativePath == "/" {
            relativePath = "/index.html"
        }
        let fileURL = root.appendingPathComponent(String(relativePath.dropFirst())).standardizedFileURL

        // 防目录穿越
        let rootPath = root.path
        let filePath = fileURL.path
        guard filePath == rootPath || filePath.hasPrefix(rootPath + "/") else {
            finish(task: task, id: id, status: 403, mime: "text/plain", data: Data("forbidden".utf8))
            return
        }

        guard let data = try? Data(contentsOf: fileURL) else {
            finish(task: task, id: id, status: 404, mime: "text/plain", data: Data("not found".utf8))
            return
        }
        finish(task: task, id: id, status: 200, mime: Self.mimeType(for: fileURL), data: data)
    }

    private func finish(task: WKURLSchemeTask, id: ObjectIdentifier, status: Int, mime: String, data: Data) {
        guard activeTasks.contains(id) else { return }
        guard let url = task.request.url,
              let response = HTTPURLResponse(
                url: url,
                statusCode: status,
                httpVersion: "HTTP/1.1",
                headerFields: [
                    "Content-Type": mime,
                    "Cache-Control": "no-store",
                ]) else {
            activeTasks.remove(id)
            return
        }
        task.didReceive(response)
        task.didReceive(data)
        task.didFinish()
        activeTasks.remove(id)
    }

    private static func mimeType(for url: URL) -> String {
        switch url.pathExtension.lowercased() {
        case "html", "htm": return "text/html; charset=utf-8"
        case "js", "mjs": return "text/javascript; charset=utf-8"
        case "css": return "text/css; charset=utf-8"
        case "json", "map": return "application/json"
        case "png": return "image/png"
        case "jpg", "jpeg": return "image/jpeg"
        case "gif": return "image/gif"
        case "webp": return "image/webp"
        case "svg": return "image/svg+xml"
        case "ico": return "image/x-icon"
        case "woff": return "font/woff"
        case "woff2": return "font/woff2"
        case "ttf": return "font/ttf"
        case "wasm": return "application/wasm"
        default: return "application/octet-stream"
        }
    }
}
