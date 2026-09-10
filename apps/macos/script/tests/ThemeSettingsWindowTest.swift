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
    var defaultLightThemeID = "colors-default-light"
    var defaultDarkThemeID = "colors-dark"
    func setStyle(_ id: String) { currentStyleId = id }
    func setTheme(_ id: String) { currentThemeId = id }
    func setFollowSystemTheme(_ enabled: Bool) { isFollowSystemTheme = enabled }
    func setDefaultLightThemeID(_ id: String) { defaultLightThemeID = id }
    func setDefaultDarkThemeID(_ id: String) { defaultDarkThemeID = id }
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
let segmented = descendants(window.contentView!).compactMap { $0 as? NSSegmentedControl }.first
expect(segmented?.segmentCount == 2, "theme settings should separate colors and typography")
let buttons = descendants(window.contentView!).compactMap { $0 as? NSButton }
expect(buttons.contains { $0.title == L10n.t("与操作系统同步") }, "follow-system should be available inside the window")
let popups = descendants(window.contentView!).compactMap { $0 as? NSPopUpButton }
expect(popups.count >= 2, "default light and dark themes should be selectable inside the window")
expect(buttons.contains { $0.title == L10n.t("添加主题…") }, "add theme should be available")
expect(buttons.contains { $0.title == L10n.t("打开主题文件夹…") }, "open theme folder should be available")
let colors = tables.first { $0.identifier?.rawValue == "theme-colors" }!
let styles = tables.first { $0.identifier?.rawValue == "theme-styles" }!
// 颜色列表按“浅色 / 深色”分组：fixture 的主题为 [浅色, 深色]，
// 因此行序为 [分组“浅色”, 浅色, 分组“深色”, 深色]。
expect(colors.numberOfRows == 4, "color list shows both groups and their themes")
let colorDelegate = colors.delegate
expect(colorDelegate?.tableView?(colors, isGroupRow: 0) == true, "light group header row")
expect(colorDelegate?.tableView?(colors, isGroupRow: 1) == false, "theme row is selectable content")
expect(colorDelegate?.tableView?(colors, isGroupRow: 2) == true, "dark group header row")
expect(colorDelegate?.tableView?(colors, shouldSelectRow: 0) == false, "group headers are not selectable")
expect(colors.selectedRow == 3 && styles.selectedRow == 1, "opening highlights active selections")
expect(colors.accessibilityLabel() == L10n.t("颜色主题") && styles.accessibilityLabel() == L10n.t("排版样式"), "VoiceOver identifies each list")
styles.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
expect(first.currentStyleId == "serif", "row selection applies immediately")
colors.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
expect(first.currentThemeId == "colors-default-light", "theme row maps through the grouped list")
active = second; controller.refresh()
expect(colors.selectedRow == 1 && styles.selectedRow == 0, "rebinding reflects new session")
expect(second.currentStyleId == "serif", "refresh does not apply old state")
second.isFollowSystemTheme = true; controller.refresh()
expect(!colors.isEnabled, "follow-system disables color selection")
for width in [620.0, 980.0] {
    window.setContentSize(NSSize(width: width, height: 440)); window.contentView!.layoutSubtreeIfNeeded()
    expect(colors.enclosingScrollView!.frame.width >= 270 && styles.enclosingScrollView!.frame.width >= 270, "columns remain readable when resized")
}
}
}
// 缺字样式必须出现可点击徽标；点击后打开可选字体窗口（注入固定的缺字结果，避免依赖本机字体）。
var optionalFontsRequested = false
let badgeSession = Session()
let badgeController = ThemeSettingsWindowController(
    sessionProvider: { badgeSession },
    onOptionalFonts: { optionalFontsRequested = true },
    missingPacksProvider: { $0 == "latex" ? [OptionalFontCatalog.packs[0]] : [] }
)
let badgeWindow = badgeController.window!
let badgeStyles = descendants(badgeWindow.contentView!)
    .compactMap { $0 as? NSTableView }
    .first { $0.identifier?.rawValue == "theme-styles" }!
badgeController.refresh()
let latexIndex = badgeSession.styles.firstIndex { $0.id == "latex" }!
let latexCell = badgeStyles.view(atColumn: 0, row: latexIndex, makeIfNecessary: true)!
let badge = descendants(latexCell).compactMap { $0 as? NSButton }.first { $0.title == L10n.t("缺字体") }
expect(badge != nil, "styles missing font packs must show a badge")
let serifCell = badgeStyles.view(atColumn: 0, row: 0, makeIfNecessary: true)!
expect(
    descendants(serifCell).compactMap { $0 as? NSButton }.isEmpty,
    "styles without missing packs must not show the badge"
)
badge?.performClick(nil)
expect(optionalFontsRequested, "badge opens the optional fonts window")
print("Theme settings AppKit window tests passed")

enum AppLog { static func info(_ message: String) {}
static func warning(_ message: String) {}
static func error(_ message: String) {} }
