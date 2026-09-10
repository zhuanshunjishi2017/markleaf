import Foundation

/// 校验共享编辑器返回的滚动偏移；无效值一律回到顶部。
enum EditorScrollSnapshotPolicy {
    static func restoreValue(rawValue: Any?) -> Double {
        if let value = rawValue as? Double, value.isFinite, value >= 0 {
            return value
        }
        if let value = rawValue as? Int, value >= 0 {
            return Double(value)
        }
        if let value = rawValue as? NSNumber, value.doubleValue.isFinite, value.doubleValue >= 0 {
            return value.doubleValue
        }
        return 0
    }
}
