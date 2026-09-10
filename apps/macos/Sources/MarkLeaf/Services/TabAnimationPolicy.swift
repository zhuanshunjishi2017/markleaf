import Foundation

/// 标签动画时长与「减少动态效果」策略（设计稿 §5）。
enum TabAnimationPolicy {
    enum Kind {
        case insertRemoveReorder
        case activeState
        case statusMark
        case editorFade
    }

    static func duration(for kind: Kind, reduceMotion: Bool) -> TimeInterval {
        guard !reduceMotion else { return 0 }
        switch kind {
        case .insertRemoveReorder: return 0.19
        case .activeState: return 0.14
        case .statusMark: return 0.12
        case .editorFade: return 0.1
        }
    }

    /// 是否允许位移/缩放/回弹；关闭时只保留瞬时切换或简单淡化。
    static func allowsMotion(reduceMotion: Bool) -> Bool {
        !reduceMotion
    }
}
