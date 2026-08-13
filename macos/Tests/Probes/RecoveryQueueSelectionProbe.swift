import Foundation

@main
enum RecoveryQueueSelectionProbe {
    static func main() {
        precondition(RecoveryQueueSelection.nextRow(afterRemoving: 1, remainingCount: 2) == 1)
        precondition(RecoveryQueueSelection.nextRow(afterRemoving: 2, remainingCount: 2) == 1)
        precondition(RecoveryQueueSelection.nextRow(afterRemoving: 0, remainingCount: 0) == nil)
    }
}
