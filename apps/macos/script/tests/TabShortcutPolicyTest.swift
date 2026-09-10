import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func tab(_ title: String) -> DocumentTab {
    DocumentTab(path: nil, title: title, encoding: "utf-8", newLine: "lf")
}

let store = TabStore()
let first = tab("First")
let second = tab("Second")
let third = tab("Third")
store.append(first)
store.append(second)
store.append(third)

store.activate(second.tabID)
expect(
    TabShortcutPolicy.cycleTarget(in: store, reverse: false) == third.tabID,
    "Control-Tab should select the tab to the right"
)

store.activate(third.tabID)
expect(
    TabShortcutPolicy.cycleTarget(in: store, reverse: false) == first.tabID,
    "Control-Tab should wrap from the last tab to the first"
)

store.activate(first.tabID)
expect(
    TabShortcutPolicy.cycleTarget(in: store, reverse: true) == third.tabID,
    "Control-Shift-Tab should wrap from the first tab to the last"
)

let solo = TabStore()
solo.append(tab("Only"))
expect(
    TabShortcutPolicy.cycleTarget(in: solo, reverse: false) == nil,
    "a one-tab window should not consume tab cycling"
)

expect(
    TabShortcutPolicy.numberedTarget(in: store, digit: 1) == first.tabID,
    "Option-1 should select the first tab"
)
expect(
    TabShortcutPolicy.numberedTarget(in: store, digit: 3) == third.tabID,
    "Option-3 should select the third tab"
)
expect(
    TabShortcutPolicy.numberedTarget(in: store, digit: 4) == nil,
    "an out-of-range tab number should not switch tabs"
)
expect(TabShortcutPolicy.digit(forKeyCode: 18) == 1, "key code 18 should map to digit 1")
expect(TabShortcutPolicy.digit(forKeyCode: 25) == 9, "key code 25 should map to digit 9")
expect(TabShortcutPolicy.digit(forKeyCode: 51) == nil, "non-digit keys should not map to tabs")

print("PASS")
