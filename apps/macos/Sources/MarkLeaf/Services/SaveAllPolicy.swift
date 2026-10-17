import Foundation

enum SaveAllPolicy {
    static func targets(tabs: [DocumentTab]) -> [DocumentTabID] {
        tabs.filter { $0.isDirty && !$0.isReadOnly }.map(\.tabID)
    }

    static func hasTarget(tabs: [DocumentTab]) -> Bool {
        !targets(tabs: tabs).isEmpty
    }

    /// Shared sequential runner for saving and closing: a failed/cancelled item
    /// stops the batch without undoing already completed items.
    static func run<T>(_ items: [T], operation: @escaping (T, @escaping (Bool) -> Void) -> Void,
                       completion: @escaping (Bool) -> Void) {
        func advance(_ index: Int) {
            guard items.indices.contains(index) else { completion(true); return }
            operation(items[index]) { success in
                if success { advance(index + 1) } else { completion(false) }
            }
        }
        advance(0)
    }
}
