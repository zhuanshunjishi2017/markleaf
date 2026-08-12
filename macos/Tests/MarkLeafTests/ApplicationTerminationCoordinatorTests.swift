import XCTest
@testable import MarkLeaf

final class ApplicationTerminationCoordinatorTests: XCTestCase {
    func testSequentialQueueProcessesEveryWindowInOrder() {
        var visited: [Int] = []
        let requests = [1, 2, 3].map { value in
            { (finish: @escaping (DocumentDispositionResult) -> Void) in
                visited.append(value)
                finish(.proceed)
            }
        }
        SequentialDocumentDispositionQueue.run(requests) { result in
            XCTAssertEqual(result, .proceed)
            XCTAssertEqual(visited, [1, 2, 3])
        }
    }

    func testSequentialQueueStopsAtFirstCancellation() {
        var visited: [Int] = []
        let requests: [SequentialDocumentDispositionQueue.Request] = [
            { visited.append(1); $0(.proceed) },
            { visited.append(2); $0(.cancel) },
            { visited.append(3); $0(.proceed) },
        ]
        SequentialDocumentDispositionQueue.run(requests) { result in
            XCTAssertEqual(result, .cancel)
            XCTAssertEqual(visited, [1, 2])
        }
    }
}
