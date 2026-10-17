import Foundation

struct ClosedTabRecord: Equatable {
    let path: String?
    let title: String
    let untitledSequence: Int?
    let isDirty: Bool
    let isReadOnly: Bool
    let encoding: String
    let newLine: String
    let visualSelectionFrom: Int?
    let visualSelectionTo: Int?
    let sourceSelectionFrom: Int?
    let sourceSelectionTo: Int?
    let scrollTop: Double?
    let snapshotFileName: String?
    let documentKind: NewDocumentKind
    var isReadOnlyLocked: Bool? = nil
    var markdown: String? = nil
}

/// One instance per WindowSession; unlike the app singleton it cannot leak
/// closed buffers into another window's Reopen action.
final class ClosedTabHistory {
    private var records: [ClosedTabRecord] = []
    var hasRecords: Bool { !records.isEmpty }
    var latest: ClosedTabRecord? { records.last }
    func push(_ record: ClosedTabRecord) { records = ClosedTabHistoryPolicy.push(record, into: records) }
    func pop() -> ClosedTabRecord? { records.popLast() }
    func consume(_ record: ClosedTabRecord) { records.removeAll { $0 == record } }
}

enum TabCloseReason {
    case closeTab
    case closeWindow
    case terminate
}

enum ClosedTabHistoryPolicy {
    static let maximumCount = 20

    static func shouldRecord(closeReason: TabCloseReason) -> Bool {
        closeReason == .closeTab
    }

    static func push(
        _ record: ClosedTabRecord,
        into history: [ClosedTabRecord]
    ) -> [ClosedTabRecord] {
        var next = history.filter { existing in
            switch (existing.path, record.path) {
            case (.some(let existingPath), .some(let newPath)):
                return existingPath != newPath
            case (.none, .none):
                return existing.untitledSequence != record.untitledSequence
            default:
                return true
            }
        }
        next.append(record)
        return Array(next.suffix(maximumCount))
    }
}
