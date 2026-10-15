import AppKit

enum L10n { static func t(_ text: String) -> String { text } }
func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() { fputs("FAIL: \(message)\n", stderr); exit(1) }
}
let menu = NSMenu()
NativeFormatTextMenu.append(to: menu, actions: [
    "toggleBold": .init(enabled: true, checked: true),
    "toggleItalic": .init(enabled: true, checked: false),
    "formatPainterApply": .init(enabled: false, checked: false),
], canStartFormatPainter: true, formatPainterArmed: true) { title, _ in
    NSMenuItem(title: title, action: nil, keyEquivalent: "")
}
expect(menu.items.map(\.title) == ["", "格式刷", "应用格式刷", "", "格式", "", "清除格式"], "restore the traditional grouped text actions")
expect(menu.items.allSatisfy { $0.view == nil }, "traditional actions must not create a toolbar")
expect(menu.item(at: 1)!.isEnabled && menu.item(at: 1)!.state == .on, "painter availability and armed state should be retained")
expect(!menu.item(at: 2)!.isEnabled, "apply painter should remain disabled without captured formatting")
let format = menu.item(at: 4)!.submenu!
expect(!format.autoenablesItems, "AppKit must not override explicit command availability")
expect(format.items.map { $0.representedObject as? String ?? "" } == ["toggleBold", "toggleItalic", "toggleUnderline", "toggleStrike", "toggleHighlight"], "all format actions must keep their command identifiers")
expect(format.item(at: 0)!.state == .on && format.item(at: 1)!.state == .off, "selected formatting should be checked")
expect(!format.item(at: 2)!.isEnabled, "missing editor actions must remain disabled")
expect(menu.items.last!.representedObject as? String == "clearFormat", "clear formatting must remain actionable")
print("Native format text menu tests passed")
