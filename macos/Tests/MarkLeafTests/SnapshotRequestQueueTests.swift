import XCTest
@testable import MarkLeaf

final class SnapshotRequestQueueTests: XCTestCase {
    private struct TestError: Error {}

    func testCompletesRequestsInEnqueueOrder() {
        let queue = SnapshotRequestQueue()
        var received: [String] = []
        queue.enqueue { result in
            if case .success(let value) = result { received.append("first: \(value)") }
        }
        queue.enqueue { result in
            if case .success(let value) = result { received.append("second: \(value)") }
        }

        queue.completeNext(.success("one"))
        queue.completeNext(.success("two"))

        XCTAssertEqual(received, ["first: one", "second: two"])
        XCTAssertTrue(queue.isEmpty)
    }

    func testCompletionWithNoRequestDoesNothing() {
        let queue = SnapshotRequestQueue()

        queue.completeNext(.success("orphan"))

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
