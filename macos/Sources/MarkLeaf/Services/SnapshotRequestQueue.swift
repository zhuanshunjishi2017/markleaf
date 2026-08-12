import Foundation

final class SnapshotRequestQueue {
    typealias Completion = (Result<String, Error>) -> Void

    private var completions: [Completion] = []

    var isEmpty: Bool { completions.isEmpty }

    @discardableResult
    func enqueue(_ completion: @escaping Completion) -> Bool {
        let shouldStart = completions.isEmpty
        completions.append(completion)
        return shouldStart
    }

    @discardableResult
    func completeNext(_ result: Result<String, Error>) -> Bool {
        guard !completions.isEmpty else { return false }
        completions.removeFirst()(result)
        return !completions.isEmpty
    }

    func cancelAll(with error: Error) {
        let pending = completions
        completions.removeAll()
        pending.forEach { $0(.failure(error)) }
    }
}
