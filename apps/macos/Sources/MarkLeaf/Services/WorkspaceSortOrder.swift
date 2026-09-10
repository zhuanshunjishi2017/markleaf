import Foundation

/// 工作区文档排序方式（对齐 Windows MainForm.Workspace.Sort）。
enum WorkspaceSortOrder: String, Codable, CaseIterable {
    case fileNameAscending
    case fileNameDescending
    case modifiedTimeAscending
    case modifiedTimeDescending
}
