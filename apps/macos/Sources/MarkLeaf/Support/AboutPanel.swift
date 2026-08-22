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
    static func standardOptions(
        infoDictionary: [String: Any]?,
        descriptionText: String
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
        return [
            .applicationName: "MarkLeaf",
            .applicationVersion: marketVersion,
            .version: buildVersion,
            .credits: credits,
        ]
    }
}
