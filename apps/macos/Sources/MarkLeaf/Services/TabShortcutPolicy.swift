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
