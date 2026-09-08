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
