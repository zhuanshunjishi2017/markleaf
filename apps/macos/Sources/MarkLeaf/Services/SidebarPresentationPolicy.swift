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

    /// 分隔线允许的最小位置（即侧边栏可折叠到的宽度）。
    /// 侧边栏隐藏或正处于收起/展开动画时需要允许收到 0，
    /// 否则窗口 resize 会触发 NSSplitView 重排，用最小宽度约束把隐藏的侧边栏重新撑开。
    static func minimumDividerCoordinate(
        isSidebarVisible: Bool,
        isAnimating: Bool,
        minimumWidth: CGFloat
    ) -> CGFloat {
        if isAnimating || !isSidebarVisible {
            return 0
        }
        return minimumWidth
    }
}
