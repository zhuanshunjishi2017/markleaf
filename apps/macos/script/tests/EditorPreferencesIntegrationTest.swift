import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
func descendants(_ view: NSView) -> [NSView] {
    [view] + view.subviews.flatMap(descendants)
}
func activate(_ control: NSControl) {
    expect(control.action != nil, "control must have a bound action")
    expect(NSApp.sendAction(control.action!, to: control.target, from: control), "control action must be handled")
}
let app = NSApplication.shared
app.setActivationPolicy(.prohibited)
SettingsService.shared.update {
    $0.displayLanguage = "zh-Hans"
    $0.sqlFormatterDialect = .postgres
}
let preferences = PreferencesWindowController(styles: [], themes: [], initialSelectedPageIndex: 1)
let prefsRoot = preferences.window!.contentView!
prefsRoot.layoutSubtreeIfNeeded()
let controls = descendants(prefsRoot)
let inline = controls.compactMap { $0 as? NSButton }.first { $0.title == "内联常见操作" }
expect(inline != nil, "Editor preferences must expose inline common actions")
expect(inline!.state == .off, "default quick buttons should leave the hide-icons checkbox unchecked")
expect(!controls.compactMap { $0 as? NSPopUpButton }.contains { $0.itemTitles.contains("ANSI") }, "SQL dialect must no longer live in Preferences")
expect(!controls.compactMap { $0 as? NSButton }.contains { $0.title == "代码格式化器…" }, "Preferences must not expose a formatter manager entry")
expect(!controls.compactMap { $0 as? NSTextField }.contains { $0.stringValue == "代码格式化器" }, "Preferences must not retain the formatter section heading")

let manager = CodeFormatterManagerWindowController()
let root = manager.window!.contentView!
let popup = descendants(root).compactMap { $0 as? NSPopUpButton }.first { $0.itemTitles.contains("ANSI") }
expect(popup != nil, "formatter manager must expose SQL dialect")
let table = descendants(root).compactMap { $0 as? NSTableView }.first!
let sqlIndex = ExternalCodeFormatterCatalog.tools.firstIndex { $0.languages.contains("sql") }!
expect(popup!.isHiddenOrHasHiddenAncestor, "SQL dialect must be hidden for the initially selected non-SQL tool")
table.selectRowIndexes(IndexSet(integer: sqlIndex), byExtendingSelection: false)
expect(!popup!.isHiddenOrHasHiddenAncestor, "selecting SQL should reveal its dialect")
expect(popup!.titleOfSelectedItem == "PostgreSQL", "moving the SQL control must preserve the saved dialect")
popup!.selectItem(withTitle: "MySQL")
activate(popup!)
expect(SettingsService.shared.settings.sqlFormatterDialect == .mysql, "SQL popup must save immediately")
inline!.state = .on
activate(inline!)
expect(!SettingsService.shared.settings.inlineCommonActions, "checking inline common actions must hide quick buttons immediately")
expect(SettingsService.shared.settings.sqlFormatterDialect == .mysql, "open preferences must not overwrite the new SQL dialect")
let reloaded = SettingsService(environment: ProcessInfo.processInfo.environment)
reloaded.load()
expect(!reloaded.settings.inlineCommonActions && reloaded.settings.sqlFormatterDialect == .mysql, "both preferences must survive disk reload")
let reopened = PreferencesWindowController(styles: [], themes: [], initialSelectedPageIndex: 1)
let reopenedInline = descendants(reopened.window!.contentView!).compactMap { $0 as? NSButton }.first { $0.title == "内联常见操作" }!
expect(reopenedInline.state == .on, "reopened preferences must retain the checked hide-icons state")
inline!.state = .off
activate(inline!)
expect(SettingsService.shared.settings.inlineCommonActions, "unchecking inline common actions must restore quick buttons")
SettingsService.shared.update { $0.sqlFormatterDialect = .sqlite }
manager.refresh()
expect(popup!.titleOfSelectedItem == "SQLite", "manager refresh must reflect current settings")
expect(table.selectedRow == sqlIndex && !popup!.isHiddenOrHasHiddenAncestor, "refresh must retain SQL selection and dialect visibility")
for size in [NSSize(width: 620, height: 320), NSSize(width: 720, height: 400)] {
    manager.window!.setContentSize(size)
    root.layoutSubtreeIfNeeded()
    let scroll = descendants(root).compactMap { $0 as? NSScrollView }.first!
    let probe = descendants(root).compactMap { $0 as? NSButton }.first { $0.title == "探测" }!
    let popupFrame = popup!.convert(popup!.bounds, to: root)
    let probeFrame = probe.convert(probe.bounds, to: root)
    expect(scroll.frame.height > 100, "formatter list should retain usable height at minimum size")
    let popupAlignment = popup!.alignmentRect(forFrame: popupFrame)
    let probeAlignment = probe.alignmentRect(forFrame: probeFrame)
    print("SQL layout: window=\(root.bounds.size), probe=\(probeFrame), dialect=\(popupFrame), alignmentY=\(probeAlignment.midY)/\(popupAlignment.midY)")
    expect(popupAlignment.minX > probeAlignment.maxX, "SQL dialect must be positioned to the right of Probe")
    expect(abs(popupAlignment.midY - probeAlignment.midY) < 1, "SQL dialect and Probe must be vertically aligned")
    expect(popupFrame.maxX < root.bounds.maxX - 16, "SQL dialect should not overflow the window")
    expect(!root.hasAmbiguousLayout && !popup!.hasAmbiguousLayout, "SQL row layout must be unambiguous")
}
table.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
expect(popup!.isHiddenOrHasHiddenAncestor, "switching away from SQL should hide dialect controls")
expect(SettingsService.shared.settings.sqlFormatterDialect == .sqlite, "hiding SQL controls must retain the saved dialect")
table.deselectAll(nil)
expect(popup!.isHiddenOrHasHiddenAncestor, "no selected language should not reveal SQL controls")
table.selectRowIndexes(IndexSet(integer: sqlIndex), byExtendingSelection: false)
expect(!popup!.isHiddenOrHasHiddenAncestor && popup!.titleOfSelectedItem == "SQLite", "returning to SQL must restore the selected dialect")
for language in ["en", "zh-Hant", "ja"] {
    expect(L10n.translate("内联常见操作", language: language) != "内联常见操作", "new checkbox must be localized")
}
print("Editor preferences integration tests passed")
