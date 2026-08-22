import AppKit

/// 让手工布局的弹窗输入框与「偏好设置」中的标准输入框保持相同高度与圆角比例。
enum DialogTextFieldStyle {
    static func apply(to field: NSTextField) {
        field.bezelStyle = .squareBezel
        field.frame.size.height = field.fittingSize.height
        field.setContentHuggingPriority(.required, for: .vertical)
        field.setContentCompressionResistancePriority(.required, for: .vertical)
    }
}
