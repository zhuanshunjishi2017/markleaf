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

print("PASS")
