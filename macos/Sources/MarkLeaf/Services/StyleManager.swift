import Foundation

/// 排版样式（对应 C# StyleService.StyleDefinition）
struct StyleDefinition: Identifiable {
    let id: String
    let displayName: String
    let css: String
    let dependsOn: String?
}

/// 颜色主题（对应 C# ColorThemeService.ColorTheme）
struct ColorThemeInfo: Identifiable {
    let id: String
    let displayName: String
    let css: String
    let isDark: Bool
}

/// 扫描内置样式目录 + 用户主题目录，复刻 C# StyleService + ColorThemeService 的加载逻辑：
/// base.css 为基础样式；colors-*.css 为颜色主题；其余 css 为排版样式，按 @depends 拓扑排序。
/// 多目录时后者（用户目录）覆盖前者（内置），实现用户自定义主题。
final class StyleManager {
    let baseCss: String
    let styles: [StyleDefinition]
    let colorThemes: [ColorThemeInfo]
    let defaultStyleId: String
    let defaultThemeId: String?

    private let macOSFontOverride = """
    /* ---- macOS 原生适配（MarkLeaf for macOS） ---- */
    .markleaf-document {
      font-family: -apple-system, "PingFang SC", "Hiragino Sans GB", "Songti SC", "Times New Roman", serif;
    }
    .markleaf-document pre,
    .markleaf-document code {
      font-family: "SF Mono", ui-monospace, Menlo, Monaco, Consolas, monospace;
    }
    /* 滚动条跟随主题：::-webkit-scrollbar 是 Chromium 专用，WKWebView(WebKit) 不支持，
       改用 WebKit 支持的 scrollbar-color（滑块/轨道取主题变量）。 */
    html, body {
      scrollbar-color: var(--scrollbar-idle, #888888) var(--bg-primary, transparent);
    }
    .cm-scroller {
      scrollbar-color: var(--scrollbar-idle, #888888) var(--bg-primary, transparent);
      font-size: var(--ml-source-font-size, 14px);
    }
    """

    init?(directories: [URL]) {
        let fm = FileManager.default

        // 收集所有 css：后者（用户目录）覆盖前者（内置）同名文件
        var cssByFile: [String: String] = [:]
        for directory in directories {
            guard let files = try? fm.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil) else {
                AppLog.warning("无法读取样式目录: \(directory.path)")
                continue
            }
            for fileURL in files.sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) {
                guard fileURL.pathExtension == "css",
                      let css = try? String(contentsOf: fileURL, encoding: .utf8) else { continue }
                cssByFile[fileURL.lastPathComponent] = css
            }
        }
        guard !cssByFile.isEmpty else {
            AppLog.error("未找到任何样式文件")
            return nil
        }

        var base = ""
        var themeList: [ColorThemeInfo] = []
        var rawStyles: [StyleDefinition] = []

        for (fileName, css) in cssByFile.sorted(by: { $0.key < $1.key }) {
            if fileName == "base.css" {
                base = css
                continue
            }
            if fileName.hasPrefix("colors-") {
                let id = fileName.replacingOccurrences(of: ".css", with: "")
                let meta = Self.parseMetadata(css, fallbackID: id)
                let isDark = meta.mode == "dark"
                themeList.append(ColorThemeInfo(id: id, displayName: meta.name, css: css, isDark: isDark))
                continue
            }

            let id = fileName.replacingOccurrences(of: ".css", with: "")
            let meta = Self.parseMetadata(css, fallbackID: id)
            rawStyles.append(StyleDefinition(id: id, displayName: meta.name, css: css, dependsOn: meta.dependsOn))
        }

        self.baseCss = base
        self.colorThemes = themeList
        self.styles = Self.topologicalSort(rawStyles)
        AppLog.info("样式加载完成: \(themeList.count) 个颜色主题, \(rawStyles.count) 个排版样式")
        self.defaultStyleId = self.styles.first?.id ?? "serif"
        if let white = themeList.first(where: { $0.id == "white" }) {
            self.defaultThemeId = white.id
        } else {
            self.defaultThemeId = themeList.first?.id
        }
    }

    /// 组装 applyStyles 载荷（对应 C# EditorHostController.ApplyStyles）。
    func applyStylesPayload() -> [String: Any] {
        var payload: [String: Any] = [:]
        payload["baseCss"] = baseCss + macOSFontOverride
        if let theme = colorThemes.first(where: { $0.id == defaultThemeId }) ?? colorThemes.first {
            payload["colorThemeCss"] = theme.css
        }
        payload["styles"] = styles.map { style -> [String: Any] in
            var dict: [String: Any] = [
                "id": style.id,
                "displayName": style.displayName,
                "css": style.css,
            ]
            if let dependsOn = style.dependsOn {
                dict["dependsOn"] = dependsOn
            }
            return dict
        }
        payload["activeStyle"] = defaultStyleId
        return payload
    }

    // MARK: - Helpers

    private struct Metadata {
        var name: String
        var dependsOn: String?
        var mode: String?
    }

    private static func parseMetadata(_ css: String, fallbackID: String) -> Metadata {
        var name = fallbackID
        var dependsOn: String?
        var mode: String?
        // 只扫描首个 /* ... */ 注释块
        guard let open = css.range(of: "/*"), let close = css.range(of: "*/", range: open.upperBound..<css.endIndex) else {
            return Metadata(name: name, dependsOn: nil, mode: nil)
        }
        let comment = String(css[open.upperBound..<close.lowerBound])
        for line in comment.split(separator: "\n") {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.hasPrefix("@name:") {
                let value = trimmed.dropFirst("@name:".count).trimmingCharacters(in: .whitespaces)
                if !value.isEmpty { name = value }
            } else if trimmed.hasPrefix("@depends:") {
                let value = trimmed.dropFirst("@depends:".count).trimmingCharacters(in: .whitespaces)
                if !value.isEmpty { dependsOn = value }
            } else if trimmed.hasPrefix("@mode:") {
                let value = trimmed.dropFirst("@mode:".count).trimmingCharacters(in: .whitespaces)
                if !value.isEmpty { mode = value }
            }
        }
        return Metadata(name: name, dependsOn: dependsOn, mode: mode)
    }

    /// 依赖拓扑排序：被依赖的样式先注入（对应 C# TopologicalSort）。
    private static func topologicalSort(_ styles: [StyleDefinition]) -> [StyleDefinition] {
        var sorted: [StyleDefinition] = []
        var remaining = styles
        var added = Set<String>()
        var changed = true

        while !remaining.isEmpty && changed {
            changed = false
            var kept: [StyleDefinition] = []
            for style in remaining {
                if style.dependsOn == nil || added.contains(style.dependsOn!) {
                    sorted.append(style)
                    added.insert(style.id)
                    changed = true
                } else {
                    kept.append(style)
                }
            }
            remaining = kept
        }
        sorted.append(contentsOf: remaining)
        return sorted
    }
}
