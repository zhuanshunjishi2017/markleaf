import Foundation

/// The existing session owns persistence and editor bridge messages.
protocol ThemeSettingsSession: AnyObject {
    var styles: [StyleDefinition] { get }
    var colorThemes: [ColorThemeInfo] { get }
    var currentStyleId: String { get }
    var currentThemeId: String? { get }
    var isFollowSystemTheme: Bool { get }
    var defaultLightThemeID: String { get }
    var defaultDarkThemeID: String { get }
    func setStyle(_ id: String)
    func setTheme(_ id: String)
    func setFollowSystemTheme(_ enabled: Bool)
    func setDefaultLightThemeID(_ id: String)
    func setDefaultDarkThemeID(_ id: String)
}

final class ThemeSettingsModel {
    private let sessionProvider: () -> (any ThemeSettingsSession)?
    private weak var boundSession: (any ThemeSettingsSession)?
    private(set) var themes: [ColorThemeInfo] = []
    private(set) var styles: [StyleDefinition] = []
    private(set) var selectedThemeIndex: Int?
    private(set) var selectedStyleIndex: Int?
    private(set) var lightThemes: [ColorThemeInfo] = []
    private(set) var darkThemes: [ColorThemeInfo] = []
    private(set) var selectedLightDefaultIndex: Int?
    private(set) var selectedDarkDefaultIndex: Int?
    private(set) var canSelectTheme = false
    /// 颜色页按“浅色 / 深色”分组的行模型（对应 Windows 配色方案对话框的分组列表）。
    private(set) var colorRows: [ColorThemeRow] = []
    var hasSession: Bool { boundSession != nil }
    var followsSystem: Bool { boundSession?.isFollowSystemTheme ?? false }
    var currentStyleID: String? { boundSession?.currentStyleId }
    var currentThemeID: String? { boundSession?.currentThemeId }

    init(sessionProvider: @escaping () -> (any ThemeSettingsSession)?) {
        self.sessionProvider = sessionProvider
    }

    func refresh() {
        let session = sessionProvider()
        boundSession = session
        themes = session?.colorThemes ?? []
        styles = session?.styles ?? []
        lightThemes = themes.filter { !$0.isDark }
        darkThemes = themes.filter(\.isDark)
        selectedThemeIndex = themes.firstIndex { $0.id == session?.currentThemeId }
        selectedStyleIndex = styles.firstIndex { $0.id == session?.currentStyleId }
        selectedLightDefaultIndex = lightThemes.firstIndex { $0.id == session?.defaultLightThemeID }
        selectedDarkDefaultIndex = darkThemes.firstIndex { $0.id == session?.defaultDarkThemeID }
        canSelectTheme = session != nil && session?.isFollowSystemTheme == false
        colorRows = Self.colorRows(light: lightThemes, dark: darkThemes, themes: themes)
    }

    /// 分组标题 + 组内主题；分组标题不可选，只用于阅读顺序。
    static func colorRows(
        light: [ColorThemeInfo],
        dark: [ColorThemeInfo],
        themes: [ColorThemeInfo]
    ) -> [ColorThemeRow] {
        func rows(for group: String, _ list: [ColorThemeInfo]) -> [ColorThemeRow] {
            guard !list.isEmpty else { return [] }
            let indices = list.compactMap { theme in themes.firstIndex { $0.id == theme.id } }
            guard !indices.isEmpty else { return [] }
            return [.group(group)] + indices.map(ColorThemeRow.theme)
        }
        return rows(for: "浅色", light) + rows(for: "深色", dark)
    }

    /// 当前主题在颜色列表中的行号（用于选中态）。
    var selectedThemeRow: Int? {
        colorRows.firstIndex { row in
            guard case .theme(let index) = row else { return false }
            return index == selectedThemeIndex
        }
    }

    /// 行号到 themes 下标的映射；落在分组标题上时返回 nil。
    func themeIndex(atRow row: Int) -> Int? {
        guard colorRows.indices.contains(row), case .theme(let index) = colorRows[row] else { return nil }
        return index
    }

    func selectTheme(at index: Int) {
        guard let session = currentBoundSession(), canSelectTheme,
              themes.indices.contains(index), selectedThemeIndex != index else { return }
        session.setTheme(themes[index].id)
        refresh()
    }

    func selectStyle(at index: Int) {
        guard let session = currentBoundSession(), styles.indices.contains(index),
              selectedStyleIndex != index else { return }
        session.setStyle(styles[index].id)
        refresh()
    }

    func setFollowSystem(_ enabled: Bool) {
        guard let session = currentBoundSession(), session.isFollowSystemTheme != enabled else { return }
        session.setFollowSystemTheme(enabled)
        refresh()
    }

    func selectLightDefault(at index: Int) {
        guard let session = currentBoundSession(), lightThemes.indices.contains(index),
              selectedLightDefaultIndex != index else { return }
        session.setDefaultLightThemeID(lightThemes[index].id)
        refresh()
    }

    func selectDarkDefault(at index: Int) {
        guard let session = currentBoundSession(), darkThemes.indices.contains(index),
              selectedDarkDefaultIndex != index else { return }
        session.setDefaultDarkThemeID(darkThemes[index].id)
        refresh()
    }

    /// A click queued for the old editor must never apply its row to a new editor.
    private func currentBoundSession() -> (any ThemeSettingsSession)? {
        guard let current = sessionProvider(), current === boundSession else {
            refresh()
            return nil
        }
        return current
    }
}

/// 颜色主题列表的一行。
enum ColorThemeRow: Equatable {
    case group(String)
    case theme(Int)
}

extension Notification.Name {
    static let themeSettingsDidChange = Notification.Name("MarkLeaf.themeSettingsDidChange")
}
