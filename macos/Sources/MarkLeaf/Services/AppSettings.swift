import Foundation

/// 应用设置：镜像 C# AppSettings 的核心子集，JSON 持久化到
/// ~/Library/Application Support/MarkLeaf/settings.json（原子写入，与 C# 一致）。
struct AppSettings: Codable {
    var schemaVersion = 3

    init() {}

    /// 容错解码：缺键时回退默认值（对应 C# JsonSettingsService.NormalizeCurrent 的容错思路）。
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try container.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 3
        displayLanguage = try container.decodeIfPresent(String.self, forKey: .displayLanguage) ?? Self.detectSystemLanguage()
        markdownStyle = try container.decodeIfPresent(String.self, forKey: .markdownStyle) ?? "serif"
        let decodedTheme = try container.decodeIfPresent(String.self, forKey: .colorTheme) ?? "colors-white-only"
        // colors-white.css 已被 Windows 版移除（由 colors-white-only.css 替代），旧配置迁移
        colorTheme = decodedTheme == "colors-white" ? "colors-white-only" : decodedTheme
        zoomPercent = try container.decodeIfPresent(Int.self, forKey: .zoomPercent) ?? 100
        restoreZoomOnOpen = try container.decodeIfPresent(Bool.self, forKey: .restoreZoomOnOpen) ?? true
        ctrlWheelZoom = try container.decodeIfPresent(Bool.self, forKey: .ctrlWheelZoom) ?? true
        autoHideScrollbars = try container.decodeIfPresent(Bool.self, forKey: .autoHideScrollbars) ?? false
        visualLineHeight = try container.decodeIfPresent(Double.self, forKey: .visualLineHeight) ?? 1.6
        visualFontSize = try container.decodeIfPresent(Int.self, forKey: .visualFontSize) ?? 16
        visualMaxContentWidth = try container.decodeIfPresent(Int.self, forKey: .visualMaxContentWidth) ?? 820
        sourceFontSize = try container.decodeIfPresent(Int.self, forKey: .sourceFontSize) ?? 14
        sourceIndentWidth = try container.decodeIfPresent(Int.self, forKey: .sourceIndentWidth) ?? 2
        startupAction = try container.decodeIfPresent(StartupAction.self, forKey: .startupAction) ?? .newDocument
        associateMarkdownFiles = try container.decodeIfPresent(Bool.self, forKey: .associateMarkdownFiles) ?? true
        associateTextFiles = try container.decodeIfPresent(Bool.self, forKey: .associateTextFiles) ?? true
        recordRecentFiles = try container.decodeIfPresent(Bool.self, forKey: .recordRecentFiles) ?? true
        recordRecentFolders = try container.decodeIfPresent(Bool.self, forKey: .recordRecentFolders) ?? true
        autoSaveEnabled = try container.decodeIfPresent(Bool.self, forKey: .autoSaveEnabled) ?? false
        snapshotIntervalSeconds = try container.decodeIfPresent(Int.self, forKey: .snapshotIntervalSeconds) ?? 30
        newLineStyle = try container.decodeIfPresent(String.self, forKey: .newLineStyle) ?? "lf"
        topMostWindow = try container.decodeIfPresent(Bool.self, forKey: .topMostWindow) ?? false
        clipboardImageHandling = try container.decodeIfPresent(String.self, forKey: .clipboardImageHandling) ?? "saveToDefault"
        fileImageHandling = try container.decodeIfPresent(String.self, forKey: .fileImageHandling) ?? "referenceOriginal"
        imageDefaultDirectory = try container.decodeIfPresent(String.self, forKey: .imageDefaultDirectory) ?? ""
        useRelativePaths = try container.decodeIfPresent(Bool.self, forKey: .useRelativePaths) ?? true
        prefixRelativeWithDotSlash = try container.decodeIfPresent(Bool.self, forKey: .prefixRelativeWithDotSlash) ?? true
        lastFolder = try container.decodeIfPresent(String.self, forKey: .lastFolder)
        lastFile = try container.decodeIfPresent(String.self, forKey: .lastFile)
        recentFolders = try container.decodeIfPresent([String].self, forKey: .recentFolders) ?? []
        recentFiles = try container.decodeIfPresent([String].self, forKey: .recentFiles) ?? []
        workspaceWidth = try container.decodeIfPresent(Int.self, forKey: .workspaceWidth) ?? 230
        outlineWidth = try container.decodeIfPresent(Int.self, forKey: .outlineWidth) ?? 230
        sidebarVisible = try container.decodeIfPresent(Bool.self, forKey: .sidebarVisible) ?? true
        statusBarVisible = try container.decodeIfPresent(Bool.self, forKey: .statusBarVisible) ?? true
        sidebarTab = try container.decodeIfPresent(String.self, forKey: .sidebarTab) ?? "workspace"
    }

    // 界面语言（i18n）：zh-Hans / zh-Hant / en
    var displayLanguage = AppSettings.detectSystemLanguage()

    /// 首次运行未设置语言时跟随系统语言。
    static func detectSystemLanguage() -> String {
        let lang = Locale.preferredLanguages.first ?? "zh-Hans"
        if lang.hasPrefix("zh-Hant") || lang.hasPrefix("zh-HK") || lang.hasPrefix("zh-TW") || lang.hasPrefix("zh-MO") {
            return "zh-Hant"
        }
        if lang.hasPrefix("en") { return "en" }
        return "zh-Hans"
    }

    // 外观
    var markdownStyle = "serif"
    var colorTheme = "colors-white-only"
    var zoomPercent = 100
    var restoreZoomOnOpen = true
    // 是否启用 ⌘ + 滚轮缩放（触控板捏合始终可用，不受此开关控制）
    var ctrlWheelZoom = true
    var autoHideScrollbars = false

    // 编辑器
    var visualLineHeight: Double = 1.6
    var visualFontSize = 16
    var visualMaxContentWidth = 820
    var sourceFontSize = 14
    var sourceIndentWidth = 2

    // 文件
    var startupAction = StartupAction.newDocument
    var associateMarkdownFiles = true
    var associateTextFiles = true
    var recordRecentFiles = true
    var recordRecentFolders = true
    var autoSaveEnabled = false
    var snapshotIntervalSeconds = 30
    var newLineStyle = "lf"
    var topMostWindow = false
    var clipboardImageHandling = "saveToDefault"
    var fileImageHandling = "referenceOriginal"
    var imageDefaultDirectory = ""
    var useRelativePaths = true
    var prefixRelativeWithDotSlash = true

    // 工作区
    var lastFolder: String?
    var lastFile: String?
    var recentFolders: [String] = []
    var recentFiles: [String] = []

    // 窗口
    var workspaceWidth = 230
    var outlineWidth = 230
    var sidebarVisible = true
    var statusBarVisible = true
    var sidebarTab = "workspace"

    enum StartupAction: String, Codable {
        case newDocument
        case openLastWorkspace
        case openLastWorkspaceAndFiles
    }
}

final class SettingsService {
    static let shared = SettingsService()

    private(set) var settings: AppSettings = AppSettings()

    private var settingsURL: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        let dir = base.appendingPathComponent("MarkLeaf", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("settings.json")
    }

    var onChange: (() -> Void)?

    func load() {
        let fileExists = FileManager.default.fileExists(atPath: settingsURL.path)
        guard fileExists,
              let data = try? Data(contentsOf: settingsURL) else {
            AppLog.warning("设置加载失败: 文件不存在或不可读 (\(settingsURL.path), exists=\(fileExists))")
            settings = AppSettings()
            return
        }
        do {
            let decoded = try JSONDecoder().decode(AppSettings.self, from: data)
            settings = decoded
            AppLog.info("设置已加载: style=\(decoded.markdownStyle) theme=\(decoded.colorTheme) zoom=\(decoded.zoomPercent)")
        } catch {
            AppLog.warning("设置解码失败: \(error)")
            settings = AppSettings()
        }
    }

    func save() {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(settings) else { return }
        // 原子写入
        let temp = settingsURL.appendingPathExtension("tmp")
        do {
            try data.write(to: temp, options: .atomic)
            _ = try? FileManager.default.replaceItemAt(settingsURL, withItemAt: temp)
        } catch {
            AppLog.error("设置保存失败: \(error.localizedDescription)")
        }
        onChange?()
    }

    // MARK: - 便捷访问

    func update(_ mutate: (inout AppSettings) -> Void) {
        var updated = settings
        mutate(&updated)
        settings = updated
        save()
    }

    func addRecentFile(_ path: String) {
        guard settings.recordRecentFiles else { return }
        var files = settings.recentFiles.filter { $0 != path }
        files.insert(path, at: 0)
        settings.recentFiles = Array(files.prefix(10))
        save()
    }

    func addRecentFolder(_ path: String) {
        guard settings.recordRecentFolders else { return }
        var folders = settings.recentFolders.filter { $0 != path }
        folders.insert(path, at: 0)
        settings.recentFolders = Array(folders.prefix(10))
        save()
    }
}
