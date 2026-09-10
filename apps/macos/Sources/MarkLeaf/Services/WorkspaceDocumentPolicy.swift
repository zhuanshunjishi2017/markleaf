import Foundation

/// 工作区树、列表和搜索的文本范围；以 Windows WorkspaceService 为准。
enum WorkspaceDocumentPolicy {
    static func includes(fileExtension: String) -> Bool {
        switch fileExtension.lowercased() {
        case "md", "txt": return true
        default: return false
        }
    }
}
