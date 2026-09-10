import Foundation

enum TerminationTransactionPolicy {
    static let orderOfOperations = ["disposition", "flushSnapshots", "commitManifest", "allowClose"]
    static func windowCloseMayMutateSession(isTerminationCommitted: Bool) -> Bool { !isTerminationCommitted }
}
