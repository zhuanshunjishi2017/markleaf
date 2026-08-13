import Foundation

final class SerialWriteCoordinator {
    typealias Start = (@escaping () -> Void) -> Void

    private var starts: [Start] = []
    private var active = false

    var isWriting: Bool { active }

    func enqueue(_ start: @escaping Start) {
        starts.append(start)
        advance()
    }

    private func advance() {
        guard !active, !starts.isEmpty else { return }
        active = true
        let start = starts.removeFirst()
        var finished = false
        start { [weak self] in
            guard !finished else { return }
            finished = true
            self?.active = false
            self?.advance()
        }
    }
}

enum DocumentSaveRevisionPolicy {
    static func isDirty(savedRevision: Int64, currentRevision: Int64) -> Bool {
        savedRevision != currentRevision
    }
}

enum DocumentWriteWatchPolicy {
    static func originalDocumentURL(previousDocumentURL: URL?) -> URL? {
        previousDocumentURL
    }
}
