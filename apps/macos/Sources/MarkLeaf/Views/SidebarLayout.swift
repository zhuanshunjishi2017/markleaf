import CoreGraphics

enum SidebarLayout {
    static let minimumWidth: CGFloat = 200
    private static let minimumEditorWidth: CGFloat = 420
    /// 右侧独立大纲所在分栏中，编辑器区域允许的最小宽度（约束与滑动动画目标共用）。
    static let minimumOutlineSplitWidth: CGFloat = 520

    static func clampedWorkspaceWidth(_ savedWidth: Int) -> CGFloat {
        max(CGFloat(savedWidth), minimumWidth)
    }

    static func maximumSidebarWidth(totalWidth: CGFloat) -> CGFloat {
        totalWidth - minimumEditorWidth
    }
}
