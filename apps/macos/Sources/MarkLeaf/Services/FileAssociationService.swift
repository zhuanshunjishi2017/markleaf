import AppKit
import CoreServices
import UniformTypeIdentifiers

/// macOS 文件关联（对应 C# FileAssociationService）：
/// macOS 按 bundle 声明文档类型，无法像 Windows 那样按扩展名增删“打开方式”候选
/// （Info.plist 已把 MarkLeaf 列为 .md/.markdown/.txt 的打开方式）。
/// 因此「绑定文件打开方式」通过把 MarkLeaf 设为对应内容类型的**默认编辑器**来实现：
/// 勾选 → 先保存当前默认值，再把 MarkLeaf 设为默认；取消 → 还原之前保存的默认值。
/// 还原用 API 不总是传播，故补充直接写 LaunchServices 数据库 + 重启 lsd 兜底。
final class FileAssociationService {
    static let shared = FileAssociationService()

    /// 设置键 → 内容类型 UTI（.md/.markdown 共用 net.daringfireball.markdown）
    private let bindings: [(settingKey: String, uti: String)] = [
        ("associateMarkdownFiles", "net.daringfireball.markdown"),
        ("associateTextFiles", "public.plain-text"),
    ]

    private let savedDefaultPrefix = "SavedDefaultHandlerFor"

    /// 应用当前设置：勾选即绑定为默认编辑器，取消即还原。
    func apply(settings: AppSettings) {
        let bundleID = Bundle.main.bundleIdentifier ?? "com.markleaf.app"
        // 先注册 bundle，确保 LaunchServices 认识 MarkLeaf
        let bundleURL = Bundle.main.bundleURL
        if bundleURL.pathExtension == "app" {
            _ = LSRegisterURL(bundleURL as CFURL, true)
        }
        for (settingKey, uti) in bindings {
            let enabled = settingKey == "associateMarkdownFiles"
                ? settings.associateMarkdownFiles
                : settings.associateTextFiles
            if enabled {
                bind(uti: uti, bundleID: bundleID)
            } else {
                unbind(uti: uti, bundleID: bundleID)
            }
        }
    }

    private func bind(uti: String, bundleID: String) {
        let type = uti as CFString
        let defaultsKey = savedDefaultPrefix + ":" + uti
        guard let current = LSCopyDefaultRoleHandlerForContentType(type, .editor)?.takeRetainedValue() as String?,
              current != bundleID else {
            return
        }
        // 保存原默认值（仅保存一次，避免覆盖更早的原值）
        if UserDefaults.standard.string(forKey: defaultsKey) == nil {
            UserDefaults.standard.set(current, forKey: defaultsKey)
        }
        _ = LSSetDefaultRoleHandlerForContentType(type, .editor, bundleID as CFString)
        AppLog.info("文件关联已绑定 \(uti)：默认打开程序设为 MarkLeaf（原：\(current)）")
    }

    private func unbind(uti: String, bundleID: String) {
        let type = uti as CFString
        let defaultsKey = savedDefaultPrefix + ":" + uti
        guard let current = LSCopyDefaultRoleHandlerForContentType(type, .editor)?.takeRetainedValue() as String?,
              current == bundleID,
              let saved = UserDefaults.standard.string(forKey: defaultsKey) else {
            return
        }
        // API 还原（部分系统上不传播），再用数据库直改兜底
        _ = LSSetDefaultRoleHandlerForContentType(type, .editor, saved as CFString)
        Self.restoreHandlerInLSDatabase(contentType: uti, handler: saved)
        UserDefaults.standard.removeObject(forKey: defaultsKey)
        AppLog.info("文件关联已解除 \(uti)：还原默认打开程序为 \(saved)")
    }

    /// 直接编辑 LaunchServices 数据库还原默认打开程序（API 还原不生效时的兜底）。
    private static func restoreHandlerInLSDatabase(contentType: String, handler: String) {
        let path = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Preferences/com.apple.LaunchServices/com.apple.launchservices.secure.plist")
        guard FileManager.default.fileExists(atPath: path.path),
              let data = try? Data(contentsOf: path),
              var root = (try? PropertyListSerialization.propertyList(from: data, options: [], format: nil)) as? [String: Any],
              var handlers = root["LSHandlers"] as? [[String: Any]] else {
            return
        }
        var changed = false
        for index in handlers.indices where handlers[index]["LSHandlerContentType"] as? String == contentType {
            handlers[index]["LSHandlerRoleEditor"] = handler
            changed = true
        }
        guard changed else { return }
        root["LSHandlers"] = handlers
        if let out = try? PropertyListSerialization.data(fromPropertyList: root, format: .binary, options: 0) {
            try? out.write(to: path, options: .atomic)
        }
        // 重启 cfprefsd 与 LaunchServices 守护进程，使还原立即生效
        for daemon in ["cfprefsd", "lsd"] {
            let kill = Process()
            kill.executableURL = URL(fileURLWithPath: "/usr/bin/killall")
            kill.arguments = [daemon]
            try? kill.run()
        }
    }
}
