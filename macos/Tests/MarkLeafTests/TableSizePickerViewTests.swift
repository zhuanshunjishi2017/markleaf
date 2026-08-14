import AppKit
import XCTest
@testable import MarkLeaf

final class TableSizePickerViewTests: XCTestCase {
    func testGridStartsEmptyAndDoesNotRememberPreviousSelection() {
        let view = TableSizePickerView()

        XCTAssertEqual(view.selectedSize, TableSize(rows: 0, columns: 0))

        view.updateSelection(row: 7, column: 8)

        let freshView = TableSizePickerView()
        XCTAssertEqual(freshView.selectedSize, TableSize(rows: 0, columns: 0))
    }

    func testLeavingGridAreaResetsSelectionToBlank() {
        let view = TableSizePickerView()
        view.updateSelection(row: 7, column: 8)
        XCTAssertEqual(view.selectedSize, TableSize(rows: 7, columns: 8))

        view.resetSelection()

        XCTAssertEqual(view.selectedSize, TableSize(rows: 0, columns: 0))
    }

    func testHoveringInGridGapKeepsCurrentSelection() {
        let view = TableSizePickerView()
        view.updateSelection(row: 3, column: 3)
        let gap = NSPoint(
            x: view.contentPadding + CGFloat(2) * (view.cellSize + view.cellSpacing) + view.cellSize + view.cellSpacing / 2,
            y: view.titleTopPadding + view.titleHeight + view.titleToGridSpacing
                + CGFloat(2) * (view.cellSize + view.cellSpacing) + view.cellSize + view.cellSpacing / 2
        )

        view.handleHover(at: gap)

        XCTAssertEqual(view.selectedSize, TableSize(rows: 3, columns: 3))
    }

    func testHoveringOutsideGridResetsSelectionToBlank() {
        let view = TableSizePickerView()
        view.updateSelection(row: 7, column: 8)

        view.handleHover(at: NSPoint(x: 10, y: 10))

        XCTAssertEqual(view.selectedSize, TableSize(rows: 0, columns: 0))
    }

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
