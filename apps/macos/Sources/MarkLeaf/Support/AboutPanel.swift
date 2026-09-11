import AppKit

/// 构造系统原生 About 面板的选项。
///
/// 版本行由两个字段拼成：
/// - `applicationVersion`：market version（如 "1.3.1"），系统会自动加 "Version" 前缀；
/// - `version`：build 号（如 "310"），显示为 "Version 1.3.1 (310)"。
///
/// 版权行由 `Info.plist` 的 `NSHumanReadableCopyright` 提供；
/// 描述文字作为 credits 传入（帮助菜单里另有项目主页入口）。
enum AboutPanel {
    /// 关于面板的应用图标。
    ///
    /// 系统标准面板默认取 `NSApp.applicationIconImage`，该值依赖 LaunchServices
    /// 对 app bundle 的注册与图标缓存：刚生成、位于临时目录或尚未注册的包会退回
    /// 通用文档图标（关于面板显示为空白页）。这里优先直接读取 bundle 内的
    /// `AppIcon.icns`，只有读不到时才回退到系统提供的应用图标。
    static func icon(
        bundleResourceURL: URL?,
        applicationIcon: NSImage?
    ) -> NSImage? {
        if let bundleResourceURL,
           let image = NSImage(contentsOf: bundleResourceURL),
           image.isValid,
           !image.representations.isEmpty {
            return image
        }
        if let applicationIcon,
           applicationIcon.isValid,
           !applicationIcon.representations.isEmpty {
            return applicationIcon
        }
        return nil
    }

    static func standardOptions(
        infoDictionary: [String: Any]?,
        descriptionText: String,
        bundleResourceURL: URL? = Bundle.main.url(forResource: "AppIcon", withExtension: "icns"),
        applicationIcon: NSImage? = NSApp?.applicationIconImage
    ) -> [NSApplication.AboutPanelOptionKey: Any] {
        let marketVersion = infoDictionary?["CFBundleShortVersionString"] as? String ?? ""
        let buildVersion = infoDictionary?["CFBundleVersion"] as? String ?? ""
        let credits = NSAttributedString(
            string: descriptionText,
            attributes: [
                .font: NSFont.systemFont(ofSize: 12),
                .foregroundColor: NSColor.secondaryLabelColor,
            ]
        )
        var options: [NSApplication.AboutPanelOptionKey: Any] = [
            .applicationName: "MarkLeaf",
            .applicationVersion: marketVersion,
            .version: buildVersion,
            .credits: credits,
        ]
        if let icon = icon(bundleResourceURL: bundleResourceURL, applicationIcon: applicationIcon) {
            options[.applicationIcon] = icon
        }
        return options
    }
}
