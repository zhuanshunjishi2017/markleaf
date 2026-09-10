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

extension Notification.Name {
    static let themeSettingsDidChange = Notification.Name("MarkLeaf.themeSettingsDidChange")
}
