import Foundation

enum SnapshotDebouncePolicy {
    static func shouldFire(elapsed: TimeInterval, interval: TimeInterval) -> Bool { elapsed >= interval }
}

struct OrderedSetLike {
    private(set) var ordered: [String] = []
    private var seen: Set<String> = []
    mutating func insert(_ value: String) {
        guard seen.insert(value).inserted else { return }
        ordered.append(value)
    }
    mutating func removeAll() { ordered.removeAll(); seen.removeAll() }
}

final class ChangedTabTracker {
    private var changed = OrderedSetLike()
    func markChanged(_ tabID: String) { changed.insert(tabID) }
    func drainChanged() -> [String] { defer { changed.removeAll() }; return changed.ordered }
}

final class SessionWriteScheduler {
    enum FlushReason { case tabSwitch, windowResigned, memoryPressure, termination, manifestOnly }
    private let store: SessionStore
    private let debounceInterval: TimeInterval
    private let tracker = ChangedTabTracker()
    private var pendingWork: [String: DispatchWorkItem] = [:]
    private var pendingContent: [String: (content: String, revision: Int64, encoding: String, newLine: String)] = [:]
    var manifestProvider: () -> SessionManifest? = { nil }
    var onSnapshotWritten: ((String, String) -> Void)?
    var onWriteFailure: ((String) -> Void)?

    init(store: SessionStore, debounceInterval: TimeInterval = 1.5) {
        self.store = store
        self.debounceInterval = debounceInterval
    }

    func scheduleSnapshot(tabID: String, content: String, revision: Int64, encoding: String, newLine: String) {
        tracker.markChanged(tabID)
        pendingContent[tabID] = (content, revision, encoding, newLine)
        pendingWork[tabID]?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.writePendingSnapshot(tabID: tabID) }
        pendingWork[tabID] = work
        DispatchQueue.main.asyncAfter(deadline: .now() + debounceInterval, execute: work)
    }

    private func writePendingSnapshot(tabID: String) {
        pendingWork[tabID] = nil
        guard let value = pendingContent.removeValue(forKey: tabID) else { return }
        do {
            let fileName = try store.writeSnapshot(tabID: tabID, content: value.content, revision: value.revision, encoding: value.encoding, newLine: value.newLine)
            onSnapshotWritten?(tabID, fileName)
        } catch { onWriteFailure?(tabID) }
    }

    func flushNow(reason: FlushReason, completion: @escaping (Bool) -> Void) {
        pendingWork.values.forEach { $0.cancel() }
        pendingWork.removeAll()
        let pending = pendingContent
        pendingContent.removeAll()
        do {
            for (tabID, value) in pending {
                let fileName = try store.writeSnapshot(tabID: tabID, content: value.content, revision: value.revision, encoding: value.encoding, newLine: value.newLine)
                onSnapshotWritten?(tabID, fileName)
            }
            _ = tracker.drainChanged()
            if reason != .manifestOnly, let manifest = manifestProvider() {
                try store.commit(manifest: manifest)
                store.pruneOrphanSnapshots(keeping: manifest)
            }
            completion(true)
        } catch {
            let failedIDs = Set(pending.keys)
            failedIDs.forEach { onWriteFailure?($0) }
            completion(false)
        }
    }
}
