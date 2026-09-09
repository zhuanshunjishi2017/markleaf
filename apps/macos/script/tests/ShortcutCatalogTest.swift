import AppKit

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let findEntries = ShortcutCatalog.entries.filter { $0.command == "find" || $0.command == "replace" }
expect(findEntries.count == 1,
       "Keyboard Shortcuts should expose one combined Find & Replace entry")
expect(findEntries.first?.command == "find",
       "the combined shortcut should drive the menu's unified find command")
expect(findEntries.first?.titleKey == "查找与替换",
       "the combined shortcut should use the unified localized title")
expect(findEntries.first?.defaultKey == "f" && findEntries.first?.defaultMask == [.command],
       "the combined entry should retain Command-F as its default")

let themeEntry = ShortcutCatalog.entry(for: "showThemeSettings")
expect(themeEntry != nil, "theme settings participates in customization and conflict validation")
expect(themeEntry?.defaultKey == "t" && themeEntry?.defaultMask == [.option, .shift],
       "theme settings uses Option-Shift-T")
let collisions = ShortcutCatalog.entries.filter {
    $0.defaultKey.lowercased() == "t" && $0.defaultMask == [.option, .shift]
}
expect(collisions.count == 1, "theme settings default shortcut must be unique")

print("PASS")
