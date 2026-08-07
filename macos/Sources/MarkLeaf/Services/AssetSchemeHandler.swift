import Foundation
import WebKit

/// 本地图片资源服务：对应 C# EditorHostController.OnAssetResourceRequested。
/// 前端图片引用为 https://assets.local/image?path=<encoded>，WKWebView 无法注册 https scheme，
/// 因此由注入的 JS 把 src 重写为 markleaf-asset://image?path=...，再由此处理器读取本地文件。
final class AssetSchemeHandler: NSObject, WKURLSchemeHandler {
    private let queue = DispatchQueue(label: "com.markleaf.asset")
    private var activeTasks = Set<ObjectIdentifier>()

    private static let allowedExtensions: Set<String> = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"]

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
        guard let url = task.request.url,
              let components = URLComponents(url: url, resolvingAgainstBaseURL: false),
              let pathQuery = components.queryItems?.first(where: { $0.name == "path" })?.value,
              let decoded = pathQuery.removingPercentEncoding else {
            respond(task: task, id: id, status: 400, mime: "text/plain", data: Data("bad request".utf8))
            return
        }

        let path = (decoded as NSString).standardizingPath
        let ext = (path as NSString).pathExtension.lowercased()
        guard Self.allowedExtensions.contains("." + ext),
              FileManager.default.fileExists(atPath: path) else {
            respond(task: task, id: id, status: 404, mime: "text/plain", data: Data("not found".utf8))
            return
        }

        guard let data = try? Data(contentsOf: URL(fileURLWithPath: path)) else {
            respond(task: task, id: id, status: 404, mime: "text/plain", data: Data("not found".utf8))
            return
        }
        respond(task: task, id: id, status: 200, mime: Self.mimeType(forExtension: ext), data: data)
    }

    private func respond(task: WKURLSchemeTask, id: ObjectIdentifier, status: Int, mime: String, data: Data) {
        guard activeTasks.contains(id), let url = task.request.url,
              let response = HTTPURLResponse(
                url: url, statusCode: status, httpVersion: "HTTP/1.1",
                headerFields: ["Content-Type": mime, "Cache-Control": "no-store"]) else {
            activeTasks.remove(id)
            return
        }
        task.didReceive(response)
        task.didReceive(data)
        task.didFinish()
        activeTasks.remove(id)
    }

    private static func mimeType(forExtension ext: String) -> String {
        switch ext {
        case "png": return "image/png"
        case "jpg", "jpeg": return "image/jpeg"
        case "gif": return "image/gif"
        case "webp": return "image/webp"
        case "bmp": return "image/bmp"
        default: return "application/octet-stream"
        }
    }
}
