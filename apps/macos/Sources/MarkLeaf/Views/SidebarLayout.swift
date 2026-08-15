import CoreGraphics

enum SidebarLayout {
    static let minimumWidth: CGFloat = 200
    private static let minimumEditorWidth: CGFloat = 420

    static func clampedWorkspaceWidth(_ savedWidth: Int) -> CGFloat {
        max(CGFloat(savedWidth), minimumWidth)
    }

    static func maximumSidebarWidth(totalWidth: CGFloat) -> CGFloat {
        totalWidth - minimumEditorWidth
    }
}
