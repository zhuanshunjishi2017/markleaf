import AppKit

/// 以「输入后过滤」实现数值文本框约束（替代 NumberFormatter）：
/// - 不做任何回显归一化：`1.60`、`1.` 这类合法输入原样保留，聚焦/失焦不会改写内容；
/// - 非法字符（字母、多余小数点）输入即被剔除；超出上限的变更整串回退并提示；
/// - 整数字段（fractionDigits == 0）输入小数点直接回退本次变更；
/// - 空值与低于下限的中间值不拦截，交给各窗口的失焦校验与按钮禁用兜底。
final class BoundedTextFieldMonitor {
    let maxFractionDigits: Int
    let upperBound: Double
    /// 仅在监视器改写了文本时回调，用于同步「确定 / 应用更改」按钮状态。
    var onRewrite: (() -> Void)?

    private let field: NSTextField
    private var lastAccepted: String
    private var observer: NSObjectProtocol?

    init(
        field: NSTextField,
        fractionDigits: Int,
        upperBound: Double,
        onChange: (() -> Void)? = nil
    ) {
        self.field = field
        self.maxFractionDigits = fractionDigits
        self.upperBound = upperBound
        self.onRewrite = onChange
        self.lastAccepted = field.stringValue
        observer = NotificationCenter.default.addObserver(
            forName: NSControl.textDidChangeNotification,
            object: field,
            queue: .main
        ) { [weak self, weak field] _ in
            guard let self, let field else { return }
            self.handleEdit(field)
        }
    }

    deinit {
        if let observer {
            NotificationCenter.default.removeObserver(observer)
        }
    }

    private func handleEdit(_ field: NSTextField) {
        let original = field.stringValue
        // 整数字段不接受小数点（等价于旧格式化器的按键拒绝）。
        if maxFractionDigits == 0, original.contains(".") {
            NSSound.beep()
            field.stringValue = lastAccepted
            onRewrite?()
            return
        }
        let candidate = Self.sanitized(original, maxFractionDigits: maxFractionDigits) ?? original
        if let value = Self.value(candidate), value > upperBound {
            NSSound.beep()
            field.stringValue = lastAccepted
        } else if candidate != original {
            field.stringValue = candidate
            lastAccepted = candidate
        } else {
            lastAccepted = candidate
        }
        if field.stringValue != original {
            onRewrite?()
        }
    }

    /// 过滤输入：只保留数字和最多一个小数点，小数位截断到上限；
    /// 小数字段以 “.” 开头时补全为 “0.”（如 “.6” → “0.6”，而不是报错）；
    /// 返回 nil 表示内容合法、无需改写。
    static func sanitized(_ text: String, maxFractionDigits: Int) -> String? {
        var sawDecimalPoint = false
        var fractionCount = 0
        var kept = ""
        kept.reserveCapacity(text.count)
        for char in text {
            if char.isASCII, char.isNumber {
                if sawDecimalPoint {
                    fractionCount += 1
                    if fractionCount > maxFractionDigits { continue }
                }
                kept.append(char)
            } else if char == ".", !sawDecimalPoint, maxFractionDigits > 0 {
                sawDecimalPoint = true
                fractionCount = 0
                kept.append(char)
            }
            // 其余字符（字母、第二个小数点等）直接丢弃。
        }
        if maxFractionDigits > 0, kept.hasPrefix(".") {
            kept = "0" + kept
        }
        return kept == text ? nil : kept
    }

    /// 与各窗口校验一致的数值解析：忽略首尾空白，容忍逗号小数点；空串返回 nil。
    static func value(_ text: String) -> Double? {
        Double(text
            .trimmingCharacters(in: .whitespaces)
            .replacingOccurrences(of: ",", with: "."))
    }
}
