import AppKit

/// 偏好设置窗口的尺寸与内容列布局策略。
/// 将窗口的紧凑边界与页面内容的居中规则集中管理，便于回归测试。
enum PreferencesWindowLayout {
    static let minimumWindowWidth: CGFloat = 500
    static let maximumWindowWidth: CGFloat = 620
    static let minimumWindowHeight: CGFloat = 430
    static let maximumWindowHeight: CGFloat = 560
    static let maximumContentColumnWidth: CGFloat = 460
    static let contentColumnMinimumMargin: CGFloat = 24
    /// 提示文案不占用表单标签列，但可按上下文向右缩进，保持与说明对象的视觉起点一致。
    static let fieldHintLeadingInset: CGFloat = 48
    static let sectionHintLeadingInset: CGFloat = 8
    /// 文件页的表单视觉重心略偏右，向左补偿以对齐页面标题与底部操作区。
    static let filePageContentHorizontalOffset: CGFloat = -14
    static let bottomBarTopInset: CGFloat = 12
    static let bottomBarBottomInset: CGFloat = 12

    static func windowContentSize(for fittingSize: NSSize) -> NSSize {
        let width = min(
            maximumWindowWidth,
            max(minimumWindowWidth, ceil(fittingSize.width) + 32)
        )
        let height = min(
            maximumWindowHeight,
            max(minimumWindowHeight, ceil(fittingSize.height) + 24)
        )
        return NSSize(width: width, height: height)
    }

    static func centeredColumnFrame(containerWidth: CGFloat, fittingWidth: CGFloat) -> NSRect {
        let availableWidth = max(0, containerWidth - 2 * contentColumnMinimumMargin)
        let width = min(max(0, fittingWidth), min(maximumContentColumnWidth, availableWidth))
        return NSRect(
            x: (containerWidth - width) / 2,
            y: 0,
            width: width,
            height: 0
        )
    }
}
