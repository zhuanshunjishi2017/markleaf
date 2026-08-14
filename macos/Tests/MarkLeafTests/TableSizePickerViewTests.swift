import AppKit
import XCTest
@testable import MarkLeaf

final class TableSizePickerViewTests: XCTestCase {
    func testGridSelectionUpdatesToHoveredRowAndColumn() {
        let view = TableSizePickerView()

        view.updateSelection(row: 7, column: 8)

        XCTAssertEqual(view.selectedSize, TableSize(rows: 7, columns: 8))
    }

    func testCommitSelectionCallsSelectHandler() {
        let view = TableSizePickerView()
        var selected: TableSize?
        view.onSelect = { selected = $0 }
        view.updateSelection(row: 7, column: 8)

        view.commitSelection()

        XCTAssertEqual(selected, TableSize(rows: 7, columns: 8))
    }

    func testCancelCallsCancelHandler() {
        let view = TableSizePickerView()
        var cancelled = false
        view.onCancel = { cancelled = true }

        view.cancelSelection()

        XCTAssertTrue(cancelled)
    }
}
