import AppKit

/// 判断标签拖拽释放后应重新排序，还是拖出新窗口。
enum TabDetachPolicy {
    enum Action: Equatable {
        case reorder
        case detach
    }

    /// 忽略窗口边缘附近释放造成的误拖出。
    static let edgeTolerance: CGFloat = 24

    static func action(globalPoint: NSPoint, windowFrame: NSRect) -> Action {
        let outsideDistance = max(
            windowFrame.minX - globalPoint.x,
            globalPoint.x - windowFrame.maxX,
            windowFrame.minY - globalPoint.y,
            globalPoint.y - windowFrame.maxY
        )
        return outsideDistance > edgeTolerance ? .detach : .reorder
    }
}
