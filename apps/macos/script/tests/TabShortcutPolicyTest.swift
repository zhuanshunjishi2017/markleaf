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

expect(TabShortcutPolicy.closesWindow(tabCount: 1), "closing the last tab should close its window")
expect(!TabShortcutPolicy.closesWindow(tabCount: 2), "closing one of multiple tabs should keep its window open")

let solo = TabStore()
solo.append(tab("Only"))
expect(
    TabShortcutPolicy.cycleTarget(in: solo, reverse: false) == nil,
    "a one-tab window should not consume tab cycling"
)

print("PASS")
