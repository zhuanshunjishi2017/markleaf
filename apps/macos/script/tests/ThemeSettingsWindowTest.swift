import AppKit
import Foundation
final class SettingsService {
    static let shared = SettingsService()
    struct Settings { var displayLanguage = "zh-Hans" }
    var settings = Settings()
}
func expect(_ value: @autoclosure () -> Bool, _ message: String) { if !value() { fputs("FAIL: \(message)\n", stderr); exit(1) } }
func descendants(_ view: NSView) -> [NSView] { view.subviews.flatMap { [$0] + descendants($0) } }
final class Session: ThemeSettingsSession {
    var styles = [StyleDefinition(id: "serif", displayName: "衬线", css: "", dependsOn: nil), StyleDefinition(id: "latex", displayName: "LaTeX", css: "", dependsOn: nil)]
    var colorThemes = [ColorThemeInfo(id: "colors-default-light", displayName: "浅色", css: "", isDark: false), ColorThemeInfo(id: "colors-dark", displayName: "深色", css: "", isDark: true)]
    var currentStyleId = "latex"
    var currentThemeId: String? = "colors-dark"
    var isFollowSystemTheme = false
    func setStyle(_ id: String) { currentStyleId = id }
    func setTheme(_ id: String) { currentThemeId = id }
}
_ = NSApplication.shared
for language in ["zh-Hans", "zh-Hant", "en", "ja"] {
SettingsService.shared.settings.displayLanguage = language
for appearance: NSAppearance.Name in [.aqua, .darkAqua] {
let first = Session(), second = Session()
second.currentStyleId = "serif"; second.currentThemeId = "colors-default-light"
var active: Session? = first
let controller = ThemeSettingsWindowController(sessionProvider: { active }, onOptionalFonts: {})
controller.refresh()
let window = controller.window!
expect(!window.canBecomeMain, "settings must preserve the active editor main window")
window.appearance = NSAppearance(named: appearance)
expect(window.styleMask.contains(.resizable), "theme window must resize")
expect(window.contentMinSize.width >= 620, "two lists remain usable at minimum size")
let tables = descendants(window.contentView!).compactMap { $0 as? NSTableView }
expect(tables.count == 2, "unified window shows color and typography lists")
let colors = tables.first { $0.identifier?.rawValue == "theme-colors" }!
let styles = tables.first { $0.identifier?.rawValue == "theme-styles" }!
expect(colors.selectedRow == 1 && styles.selectedRow == 1, "opening highlights active selections")
expect(colors.accessibilityLabel() == L10n.t("颜色主题") && styles.accessibilityLabel() == L10n.t("排版样式"), "VoiceOver identifies each list")
styles.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
expect(first.currentStyleId == "serif", "row selection applies immediately")
active = second; controller.refresh()
expect(colors.selectedRow == 0 && styles.selectedRow == 0, "rebinding reflects new session")
expect(second.currentStyleId == "serif", "refresh does not apply old state")
second.isFollowSystemTheme = true; controller.refresh()
expect(!colors.isEnabled, "follow-system disables color selection")
for width in [620.0, 980.0] {
    window.setContentSize(NSSize(width: width, height: 440)); window.contentView!.layoutSubtreeIfNeeded()
    expect(colors.enclosingScrollView!.frame.width >= 270 && styles.enclosingScrollView!.frame.width >= 270, "columns remain readable when resized")
}
}
}
print("Theme settings AppKit window tests passed")

enum AppLog { static func info(_ message: String) {}
static func warning(_ message: String) {}
static func error(_ message: String) {} }
