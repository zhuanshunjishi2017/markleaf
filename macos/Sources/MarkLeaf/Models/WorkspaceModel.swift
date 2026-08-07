import Foundation

/// 工作区条目（对应 C# WorkspaceEntry）。
struct WorkspaceEntry: Identifiable {
    let id = UUID()
    let name: String
    let path: String
    let isDirectory: Bool
}

/// 大纲标题（对应 C# EditorOutline.Heading）。
struct OutlineHeading: Identifiable {
    let id = UUID()
    let level: Int
    let text: String
    let position: Int
}
