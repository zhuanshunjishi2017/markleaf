import Foundation

/// 选区导出（对应 C# EditorSelectionExport）。
struct EditorSelectionExport {
    var text: String
    var markdown: String
    var html: String
}

/// 导出请求上下文。
struct ExportContext {
    var options: ExportOptions
    var saveURL: URL
    var forPrint = false
    var htmlRequest: ExportHTMLRequestContext
}

/// A preview callback bound to the document active when HTML generation began.
struct PendingExportHTMLRequest {
    let context: ExportHTMLRequestContext
    let completion: (String) -> Void
}
