import Foundation

/// 工作区树在异步扫描和 AppKit 行布局交错时使用的安全策略。
enum WorkspaceTreeDataSourcePolicy {
    static func safeIndex(_ index: Int, count: Int) -> Int? {
        guard index >= 0, index < count else { return nil }
        return index
    }

    static func shouldRestoreSelection(activePath: String?, entryPath: String?) -> Bool {
        guard let activePath, let entryPath else { return false }
        return URL(fileURLWithPath: activePath).standardizedFileURL.path
            == URL(fileURLWithPath: entryPath).standardizedFileURL.path
    }
}
