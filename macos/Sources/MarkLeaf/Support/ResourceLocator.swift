import Foundation

/// 资源定位：优先应用包内 Resources，其次 SwiftPM 开发目录 <package>/Resources。
enum ResourceLocator {
    static var editorWebDirectory: URL? {
        directory(named: "EditorWeb")
    }

    static var stylesDirectory: URL? {
        directory(named: "Styles")
    }

    /// 用户主题目录：~/Library/Application Support/MarkLeaf/Themes（可写，重启后生效）。
    /// 「打开主题文件夹」打开这里；用户放入 colors-*.css / 排版样式 css 后重启即可选用。
    static var userThemesDirectory: URL? {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        let dir = base.appendingPathComponent("MarkLeaf/Themes", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let readme = dir.appendingPathComponent("README.md")
        if !FileManager.default.fileExists(atPath: readme.path) {
            let content = """
            # MarkLeaf 用户主题目录

            把你自定义的颜色主题（`colors-*.css`）或排版样式（`*.css`）放到这里，
            重启 MarkLeaf 后即可在「首选项 ▸ 外观」中选用。

            主题文件顶部注释块可声明元数据：
            `@name: 主题显示名` 与 `@mode: light|dark`。
            """
            try? content.write(to: readme, atomically: true, encoding: .utf8)
        }
        return dir
    }

    static func directory(named name: String) -> URL? {
        let fm = FileManager.default
        if let bundleURL = Bundle.main.resourceURL?.appendingPathComponent(name),
           fm.fileExists(atPath: bundleURL.path) {
            return bundleURL
        }
        let cwd = URL(fileURLWithPath: fm.currentDirectoryPath)
        let devURL = cwd.appendingPathComponent("Resources").appendingPathComponent(name)
        if fm.fileExists(atPath: devURL.path) {
            return devURL
        }
        // 兜底：从可执行文件所在目录向上找 <package>/Resources/<name>
        let executableURL = URL(fileURLWithPath: CommandLine.arguments[0])
        var probe = executableURL.deletingLastPathComponent()
        for _ in 0..<6 {
            let candidate = probe.appendingPathComponent("Resources").appendingPathComponent(name)
            if fm.fileExists(atPath: candidate.path) {
                return candidate
            }
            probe.deleteLastPathComponent()
        }
        return nil
    }
}
