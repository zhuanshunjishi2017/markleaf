import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

expect(!SnapshotDebouncePolicy.shouldFire(elapsed: 0.5, interval: 1.5), "debounce waits inside interval")
expect(SnapshotDebouncePolicy.shouldFire(elapsed: 1.6, interval: 1.5), "debounce fires after interval")
let tracker = ChangedTabTracker()
tracker.markChanged("tab-1"); tracker.markChanged("tab-1"); tracker.markChanged("tab-2")
expect(tracker.drainChanged() == ["tab-1", "tab-2"], "changed tabs are unique and ordered")
expect(tracker.drainChanged().isEmpty, "drain resets tracker")
print("PASS")
