import Foundation

/// 窗口内可比较的文件身份。只保留规范化路径；大小写规则由卷属性决定，
/// 符号链接在生成身份前解析，防止同一文件因路径形式不同产生重复标签。
struct FileIdentity: Hashable {
    let normalizedPath: String
}

enum FileIdentityPolicy {
    /// 纯路径规范化：标准化（去 `.`/`..`），卷大小写不敏感时折叠为小写。
    static func normalize(path: String, volumeIsCaseSensitive: Bool) -> String {
        let standardized = URL(fileURLWithPath: path).standardizedFileURL.path
        guard !volumeIsCaseSensitive else { return standardized }

        let remainder = standardized.dropFirst()
        if let rootEnd = remainder.firstIndex(of: "/") {
            let root = standardized[standardized.startIndex...rootEnd]
            let trailing = standardized[standardized.index(after: rootEnd)...]
            return String(root + trailing.lowercased())
        }
        return standardized.lowercased()
    }

    /// 解析符号链接并读取卷大小写属性后生成身份。
    static func identity(for url: URL, fileManager: FileManager = .default) -> FileIdentity {
        let resolved = url.standardizedFileURL.resolvingSymlinksInPath()
        let caseSensitive = (try? resolved.resourceValues(forKeys: [.volumeSupportsCaseSensitiveNamesKey]))?
            .volumeSupportsCaseSensitiveNames ?? true
        return FileIdentity(normalizedPath: normalize(path: resolved.path, volumeIsCaseSensitive: caseSensitive))
    }

    static func identity(forPath path: String) -> FileIdentity {
        identity(for: URL(fileURLWithPath: path))
    }

    static func matches(path: String, identity: FileIdentity) -> Bool {
        FileIdentityPolicy.identity(forPath: path) == identity
    }
}
