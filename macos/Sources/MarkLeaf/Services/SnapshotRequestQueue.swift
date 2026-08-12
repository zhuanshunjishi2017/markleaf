import Foundation

final class SnapshotRequestQueue {
    typealias Completion = (Result<String, Error>) -> Void

    private var completions: [Completion] = []

    var isEmpty: Bool { completions.isEmpty }

    func enqueue(_ completion: @escaping Completion) {
        completions.append(completion)
    }

    func completeNext(_ result: Result<String, Error>) {
        guard !completions.isEmpty else { return }
        completions.removeFirst()(result)
    }

    func cancelAll(with error: Error) {
        let pending = completions
        completions.removeAll()
        pending.forEach { $0(.failure(error)) }
    }
}
