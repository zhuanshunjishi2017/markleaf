import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

expect(!TerminationTransactionPolicy.windowCloseMayMutateSession(isTerminationCommitted: true), "committed termination blocks window mutation")
expect(TerminationTransactionPolicy.windowCloseMayMutateSession(isTerminationCommitted: false), "normal close mutates next session")
expect(TerminationTransactionPolicy.orderOfOperations == ["disposition", "flushSnapshots", "commitManifest", "allowClose"], "termination ordering is stable")
print("PASS")
