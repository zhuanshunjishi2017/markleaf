import Foundation
func expect(_ value: @autoclosure () -> Bool, _ message: String) {
    if !value() { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
// Session is the persistence/bridge boundary; the real model must route and reject stale UI actions.
final class Session: ThemeSettingsSession {
    var styles = [StyleDefinition(id: "default", displayName: "Default", css: "", dependsOn: nil), StyleDefinition(id: "latex", displayName: "LaTeX", css: "", dependsOn: nil)]
    var colorThemes = [ColorThemeInfo(id: "colors-default-light", displayName: "Light", css: "", isDark: false), ColorThemeInfo(id: "colors-dark", displayName: "Dark", css: "", isDark: true)]
    var currentStyleId = "default"
    var currentThemeId: String? = "colors-default-light"
    var isFollowSystemTheme = false
    var defaultLightThemeID = "colors-default-light"
    var defaultDarkThemeID = "colors-dark"
    var themeCalls: [String] = []
    var styleCalls: [String] = []
    func setTheme(_ id: String) { themeCalls.append(id); currentThemeId = id }
    func setStyle(_ id: String) { styleCalls.append(id); currentStyleId = id }
    func setFollowSystemTheme(_ enabled: Bool) { isFollowSystemTheme = enabled }
    func setDefaultLightThemeID(_ id: String) { defaultLightThemeID = id }
    func setDefaultDarkThemeID(_ id: String) { defaultDarkThemeID = id }
}
let first = Session(), second = Session()
second.currentThemeId = "colors-dark"; second.currentStyleId = "latex"
var active: Session? = first
let model = ThemeSettingsModel(sessionProvider: { active })
model.refresh()
expect(model.themes.map(\.id) == ["colors-default-light", "colors-dark"], "preserve canonical IDs")
expect(model.selectedThemeIndex == 0 && model.selectedStyleIndex == 0, "reflect current selections")
model.selectTheme(at: 1); model.selectStyle(at: 1)
expect(first.themeCalls == ["colors-dark"] && first.styleCalls == ["latex"], "apply each selection once through session")
model.selectTheme(at: 1); model.selectStyle(at: 1)
expect(first.themeCalls.count == 1 && first.styleCalls.count == 1, "repeated activation must not reapply")
first.isFollowSystemTheme = true; model.refresh(); model.selectTheme(at: 0)
expect(first.themeCalls.count == 1 && !model.canSelectTheme, "follow-system blocks explicit selection")
active = second
model.selectStyle(at: 0)
expect(second.styleCalls.isEmpty && model.selectedStyleIndex == 1, "reject stale selection on active-session switch")
model.selectStyle(at: 0)
expect(second.styleCalls == ["default"], "fresh selection reaches newly active session")
model.refresh()
expect(model.selectedThemeIndex == 1 && model.selectedStyleIndex == 0, "reopening reflects session")
active = nil; model.refresh(); model.selectTheme(at: 0)
expect(model.themes.isEmpty && model.selectedThemeIndex == nil, "empty editor clears selections")
print("Theme settings model tests passed")

enum AppLog { static func info(_ message: String) {}
static func warning(_ message: String) {}
static func error(_ message: String) {} }
