import Foundation
import CoreGraphics

enum HiddenSidebarLayoutAction: Equatable {
    case animateCollapse
    case keepHiddenWithoutDividerMutation
}

enum SidebarMenuPolicy {
    /// 左侧栏隐藏时，工作区/大纲/树结构/文档列表等“左栏内容”应置灰；
    /// “在右侧显示大纲”控制独立右栏，不受左栏可见性影响，因此始终可用。
    static func leftSidebarContentEnabled(sidebarVisible: Bool) -> Bool {
        sidebarVisible
    }
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

enum SidebarTabTransitionPolicy {
    static func interpolatedFrame(from start: CGRect, to end: CGRect, progress: CGFloat) -> CGRect {
        let clamped = min(1, max(0, progress))
        return CGRect(
            x: start.origin.x + (end.origin.x - start.origin.x) * clamped,
            y: start.origin.y + (end.origin.y - start.origin.y) * clamped,
            width: start.size.width + (end.size.width - start.size.width) * clamped,
            height: start.size.height + (end.size.height - start.size.height) * clamped
        )
    }

    static func easeInOutCubic(_ progress: CGFloat) -> CGFloat {
        let clamped = min(1, max(0, progress))
        if clamped < 0.5 {
            return 4 * clamped * clamped * clamped
        }
        return 1 - pow(-2 * clamped + 2, 3) / 2
    }

    static func shouldApplyCompletion(transition: Int, currentTransition: Int) -> Bool {
        transition == currentTransition
    }
}
