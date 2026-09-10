import AppKit

/// 判断标签拖拽释放后应重新排序，还是拖出新窗口。
///
/// 参照浏览器（Chrome / Safari / Firefox）的共性行为：标签只要离开标签栏条带约一个标签高度，
/// 就视为“撕下”，在光标处生成新窗口；而在条带内移动始终是重排。
/// 因此判定基准是标签栏条带本身，而不是窗口边界——这样向下拖进编辑区、
/// 向上拖到标题栏之外都能拆窗，不再只有左右拖出窗口才生效。
enum TabDetachPolicy {
    enum Action: Equatable {
        case reorder
        case detach
    }

    /// 离开标签栏条带多远才算撕下：约等于一个标签的高度，避免轻微下移就拆窗。
    static let tearOffDistance: CGFloat = 28

    /// 标签栏条带在全局坐标下的吸附区，用于判断光标是否还停在这条标签栏旁边。
    static func stripRegion(stripGlobalRect: NSRect, tolerance: CGFloat = tearOffDistance) -> NSRect {
        stripGlobalRect.insetBy(dx: -tolerance, dy: -tolerance)
    }

    /// - Parameters:
    ///   - globalPoint: 鼠标释放点的全局坐标。
    ///   - stripGlobalRect: 来源窗口标签栏条带的全局矩形。
    ///   - windowFrame: 来源窗口的屏幕矩形，用于区分“拖到窗口内的别处”和“拖出窗口”。
    static func action(
        globalPoint: NSPoint,
        stripGlobalRect: NSRect,
        windowFrame: NSRect,
        tolerance: CGFloat = tearOffDistance
    ) -> Action {
        // 垂直方向离开条带（向上越过标题栏，或向下拖进编辑区）即撕下。
        if globalPoint.y < stripGlobalRect.minY - tolerance
            || globalPoint.y > stripGlobalRect.maxY + tolerance {
            return .detach
        }
        // 水平方向只要还在窗口内，就允许拖到条带两端之外继续排序。
        if windowFrame.contains(globalPoint) { return .reorder }
        // 左右拖出窗口同样是撕下。
        return .detach
    }

    /// 释放后的去向：先看是否落在别的窗口标签栏上，其次判断是否离开条带撕下，否则原条带内重排。
    enum DropOutcome: Equatable {
        case reorder
        case transfer
        case tearOff
    }

    static func outcome(
        hasOtherStripHit: Bool,
        globalPoint: NSPoint,
        stripGlobalRect: NSRect,
        windowFrame: NSRect,
        tolerance: CGFloat = tearOffDistance
    ) -> DropOutcome {
        if hasOtherStripHit { return .transfer }
        return action(
            globalPoint: globalPoint,
            stripGlobalRect: stripGlobalRect,
            windowFrame: windowFrame,
            tolerance: tolerance
        ) == .detach ? .tearOff : .reorder
    }

    /// 撕下的新窗口原点：沿用光标在来源窗口中的相对位置，让标签继续停在鼠标下方。
    static func tearOffOrigin(globalPoint: NSPoint, windowPoint: NSPoint) -> NSPoint {
        NSPoint(x: globalPoint.x - windowPoint.x, y: globalPoint.y - windowPoint.y)
    }
}
