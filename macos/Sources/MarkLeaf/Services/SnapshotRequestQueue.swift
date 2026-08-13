import Foundation

struct EditorSnapshot: Equatable {
    let markdown: String
    let revision: Int64
}

final class SnapshotRequestQueue {
    typealias Completion = (Result<EditorSnapshot, Error>) -> Void

    private var completions: [Completion] = []

    var isEmpty: Bool { completions.isEmpty }

    @discardableResult
    func enqueue(_ completion: @escaping Completion) -> Bool {
        let shouldStart = completions.isEmpty
        completions.append(completion)
        return shouldStart
    }

    @discardableResult
    func completeNext(_ result: Result<EditorSnapshot, Error>) -> Bool {
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
