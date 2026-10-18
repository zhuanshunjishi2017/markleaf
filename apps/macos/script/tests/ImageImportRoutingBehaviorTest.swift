import AppKit
import WebKit
@testable import MarkLeaf

final class ImageImportRecordingWebView: WKWebView {
    var commands = [String]()
    override func evaluateJavaScript(_ javaScriptString: String, completionHandler: (@MainActor (Any?, Error?) -> Void)? = nil) {
        commands.append(javaScriptString)
        completionHandler?(nil, nil)
    }
}

_ = NSApplication.shared
let session = EditorSession()
session.loadDocument(markdown: "text", fileURL: nil)
let web = ImageImportRecordingWebView(frame: .zero)
session.webView = web
for type in ImageImportPolicy.contentTypes {
    guard let ext = type.preferredFilenameExtension else { preconditionFailure("supported image types need a file extension") }
    precondition(ImageImportPolicy.supports(URL(fileURLWithPath: "/tmp/import-routing.\(ext)")),
                 "every format offered by the picker must be accepted by file imports")
}
session.insertImageFile(at: URL(fileURLWithPath: "/tmp/import-routing.png"))
precondition(web.commands.contains { $0.contains("insertImage") }, "supported images must reach the editor")
web.commands.removeAll()
let previousStatus = session.statusText
session.insertImageFile(at: URL(fileURLWithPath: "/tmp/import-routing.tiff"))
precondition(!web.commands.contains { $0.contains("insertImage") }, "unsupported formats must not be sent to the editor")
precondition(session.statusText != previousStatus, "rejected images must provide visible feedback")
print("PASS: unsupported image imports provide feedback and supported imports reach the editor")
