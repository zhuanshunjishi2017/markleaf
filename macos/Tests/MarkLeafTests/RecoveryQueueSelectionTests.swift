import XCTest
@testable import MarkLeaf

final class RecoveryQueueSelectionTests: XCTestCase {
    func testRemovingMiddleRowSelectsRowThatShiftedIntoItsPlace() {
        XCTAssertEqual(RecoveryQueueSelection.nextRow(afterRemoving: 1, remainingCount: 2), 1)
    }

    func testRemovingLastRowSelectsNewLastRow() {
        XCTAssertEqual(RecoveryQueueSelection.nextRow(afterRemoving: 2, remainingCount: 2), 1)
    }

    func testRemovingFinalRowLeavesNoSelection() {
        XCTAssertNil(RecoveryQueueSelection.nextRow(afterRemoving: 0, remainingCount: 0))
    }
}
