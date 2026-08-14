import Foundation

enum RecoveryQueueSelection {
    static func nextRow(afterRemoving removedRow: Int, remainingCount: Int) -> Int? {
        guard remainingCount > 0 else { return nil }
        return min(max(removedRow, 0), remainingCount - 1)
    }
}
