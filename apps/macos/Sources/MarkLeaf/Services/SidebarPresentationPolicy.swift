import Foundation

enum HiddenSidebarLayoutAction: Equatable {
    case animateCollapse
    case keepHiddenWithoutDividerMutation
}

enum SidebarPresentationPolicy {
    /// 侧栏首次加入分栏后会先被归一化到 0 宽，再从 0 展开到保存宽度，
    /// 因此无论启动时是否已加入 arrangedSubviews，只要是真实的可见性变化都可以动画。
    static func shouldAnimateReveal(wasArranged: Bool, visibilityChanged: Bool) -> Bool {
        visibilityChanged
    }

    /// 设置保存、最近文件更新等状态刷新会再次应用窗口布局；若侧栏仍处于隐藏状态，
    /// 不应再次写入分隔线位置，否则 NSSplitView 的最小宽度约束会把侧栏重新撑开。
    static func hiddenLayoutAction(
        isArranged: Bool,
        visibilityChanged: Bool,
        currentWidth: CGFloat
    ) -> HiddenSidebarLayoutAction {
        if isArranged && visibilityChanged && currentWidth > 1 {
            return .animateCollapse
        }
        return .keepHiddenWithoutDividerMutation
    }
}
