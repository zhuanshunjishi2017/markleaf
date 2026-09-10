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

    /// AppKit 按对象身份保存展开状态；同一路径刷新时复用仍存在的条目。
    static func retainingIdentity(_ entries: [WorkspaceEntry], from previous: [WorkspaceEntry]) -> [WorkspaceEntry] {
        let existing = Dictionary(uniqueKeysWithValues: previous.map { ($0.path, $0) })
        return entries.map { entry in
            guard let old = existing[entry.path], old.isDirectory == entry.isDirectory else { return entry }
            old.preview = entry.preview
            return old
        }
    }
}

/// 大纲标题（对应 C# EditorOutline.Heading）。
struct OutlineHeading: Identifiable {
    let id = UUID()
    let level: Int
    let text: String
    let position: Int
}
