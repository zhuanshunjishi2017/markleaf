import AppKit

/// 限制为 `[min...max]` 正整数的文本输入格式化器：
/// 只接受数字，允许清空（便于重新输入），非数字或超出范围直接拒绝。
final class BoundedIntegerFormatter: NumberFormatter {
    let range: ClosedRange<Int>

    init(min: Int, max: Int) {
        self.range = min...max
        super.init()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func isPartialStringValid(
        _ partialString: String,
        newEditingString: AutoreleasingUnsafeMutablePointer<NSString?>?,
        errorDescription: AutoreleasingUnsafeMutablePointer<NSString?>?
    ) -> Bool {
        if partialString.isEmpty { return true }
        guard partialString.allSatisfy(\.isNumber), let value = Int(partialString) else { return false }
        return range.contains(value)
    }
}
