import XCTest
@testable import MarkLeaf

final class SnapshotRequestQueueTests: XCTestCase {
    private struct TestError: Error {}

    func testCompletesRequestsInEnqueueOrder() {
        let queue = SnapshotRequestQueue()
        var received: [String] = []
        queue.enqueue { result in
            if case .success(let snapshot) = result {
                received.append("first: \(snapshot.markdown)@\(snapshot.revision)")
            }
        }
        queue.enqueue { result in
            if case .success(let snapshot) = result {
                received.append("second: \(snapshot.markdown)@\(snapshot.revision)")
            }
        }

        queue.completeNext(.success(EditorSnapshot(markdown: "one", revision: 4)))
        queue.completeNext(.success(EditorSnapshot(markdown: "two", revision: 7)))

        XCTAssertEqual(received, ["first: one@4", "second: two@7"])
        XCTAssertTrue(queue.isEmpty)
    }

    func testCompletionWithNoRequestDoesNothing() {
        let queue = SnapshotRequestQueue()

        queue.completeNext(.success(EditorSnapshot(markdown: "orphan", revision: 1)))

        XCTAssertTrue(queue.isEmpty)
    }

    func testCancelAllFailsEveryPendingRequestInEnqueueOrder() {
        let queue = SnapshotRequestQueue()
        var completions: [String] = []
        queue.enqueue { result in
            if case .failure = result { completions.append("first") }
        }
        queue.enqueue { result in
            if case .failure = result { completions.append("second") }
        }

        queue.cancelAll(with: TestError())

        XCTAssertEqual(completions, ["first", "second"])
        XCTAssertTrue(queue.isEmpty)
    }
}
