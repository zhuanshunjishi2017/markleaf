import AppKit

enum FindPanelLayout {
    static func contentHeight(isReplaceExpanded: Bool) -> CGFloat {
        isReplaceExpanded ? 110 : 74
    }

    static func replaceRowHeight(isExpanded: Bool) -> CGFloat {
        isExpanded ? 28 : 0
    }

    static func replaceTopSpacing(isExpanded: Bool) -> CGFloat {
        isExpanded ? 8 : 0
    }

    /// 查找面板靠近文档窗口顶部；展开替换行时固定上边缘并向下生长，避免面板跳动。
    static func frameKeepingTop(currentFrame: NSRect, targetHeight: CGFloat) -> NSRect {
        NSRect(
            x: currentFrame.minX,
            y: currentFrame.maxY - targetHeight,
            width: currentFrame.width,
            height: targetHeight
        )
    }
}
