import Foundation

/// 工作区条目（对应 C# WorkspaceEntry）。
final class WorkspaceEntry: Identifiable {
    let id = UUID()
    let name: String
    let path: String
    let isDirectory: Bool
    var preview: String?

    init(name: String, path: String, isDirectory: Bool, preview: String? = nil) {
        self.name = name
        self.path = path
        self.isDirectory = isDirectory
        self.preview = preview
    }
}

/// 大纲标题（对应 C# EditorOutline.Heading）。
struct OutlineHeading: Identifiable {
    let id = UUID()
    let level: Int
    let text: String
    let position: Int
}
