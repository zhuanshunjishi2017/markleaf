import Foundation

/// 多标签窗口快捷键的纯决策：循环目标与最后一个标签的关闭语义。
enum TabShortcutPolicy {
    static func cycleTarget(in store: TabStore, reverse: Bool) -> DocumentTabID? {
        let tabs = store.tabs
        guard tabs.count > 1,
              let activeID = store.activeTabID,
              let activeIndex = tabs.firstIndex(where: { $0.tabID == activeID }) else {
            return nil
        }
        let delta = reverse ? -1 : 1
        return tabs[(activeIndex + delta + tabs.count) % tabs.count].tabID
    }

}

extension TabShortcutPolicy {
    /// Windows `Alt+1..9` 对应 macOS `Option+1..9`。
    static func numberedTarget(in store: TabStore, digit: Int) -> DocumentTabID? {
        guard (1...9).contains(digit), store.tabs.indices.contains(digit - 1) else { return nil }
        return store.tabs[digit - 1].tabID
    }

    static func digit(forKeyCode keyCode: UInt16) -> Int? {
        let keyCodes: [UInt16: Int] = [
            18: 1, 19: 2, 20: 3, 21: 4, 23: 5,
            22: 6, 26: 7, 28: 8, 25: 9,
        ]
        return keyCodes[keyCode]
    }
}
