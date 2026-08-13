import XCTest
@testable import MarkLeaf

final class SerialWriteCoordinatorTests: XCTestCase {
    func testSaveCompletionStaysDirtyWhenRevisionAdvancedAfterSnapshot() {
        XCTAssertFalse(DocumentSaveRevisionPolicy.isDirty(
            savedRevision: 7,
            currentRevision: 7
        ))
        XCTAssertTrue(DocumentSaveRevisionPolicy.isDirty(
            savedRevision: 7,
            currentRevision: 8
        ))
    }

    func testFailedSaveAsRestoresTheOriginalDocumentWatch() {
        let original = URL(fileURLWithPath: "/tmp/original.md")

        XCTAssertEqual(
            DocumentWriteWatchPolicy.originalDocumentURL(previousDocumentURL: original),
            original
        )
        XCTAssertNil(DocumentWriteWatchPolicy.originalDocumentURL(previousDocumentURL: nil))
    }

    func testStartsOnlyOneWriteUntilTheFirstFinishes() {
        let coordinator = SerialWriteCoordinator()
        var started: [String] = []
        var finishFirst: (() -> Void)?
        var finishSecond: (() -> Void)?

        coordinator.enqueue { finish in
            started.append("first")
            finishFirst = finish
        }
        coordinator.enqueue { finish in
            started.append("second")
            finishSecond = finish
        }

        XCTAssertEqual(started, ["first"])
        XCTAssertTrue(coordinator.isWriting)
        finishFirst?()
        XCTAssertEqual(started, ["first", "second"])
        finishSecond?()
        XCTAssertFalse(coordinator.isWriting)
    }

    func testDuplicateFinishDoesNotAdvanceTheQueueTwice() {
        let coordinator = SerialWriteCoordinator()
        var starts = 0
        var finishFirst: (() -> Void)?

        coordinator.enqueue { finish in
            starts += 1
            finishFirst = finish
        }
        coordinator.enqueue { finish in
            starts += 1
            finish()
        }

        finishFirst?()
        finishFirst?()

        XCTAssertEqual(starts, 2)
        XCTAssertFalse(coordinator.isWriting)
    }
}
