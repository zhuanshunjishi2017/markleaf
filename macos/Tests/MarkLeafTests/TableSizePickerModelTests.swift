import XCTest
@testable import MarkLeaf

final class TableSizePickerModelTests: XCTestCase {
    func testDefaultSizeIsThreeByThree() {
        XCTAssertEqual(TableSizePickerModel.defaultSize, TableSize(rows: 3, columns: 3))
    }

    func testParseAcceptsVisibleAndCustomPositiveSizes() {
        XCTAssertEqual(TableSizePickerModel.parse("1,1"), TableSize(rows: 1, columns: 1))
        XCTAssertEqual(TableSizePickerModel.parse("10,10"), TableSize(rows: 10, columns: 10))
        XCTAssertEqual(TableSizePickerModel.parse("12,14"), TableSize(rows: 12, columns: 14))
    }

    func testParseRejectsMalformedAndNonPositiveSizes() {
        XCTAssertNil(TableSizePickerModel.parse("0,3"))
        XCTAssertNil(TableSizePickerModel.parse("-1,2"))
        XCTAssertNil(TableSizePickerModel.parse("3,0"))
        XCTAssertNil(TableSizePickerModel.parse("3.5,2"))
        XCTAssertNil(TableSizePickerModel.parse("not-a-size"))
    }

    func testClampedKeepsPositiveDimensionsWithinCustomLimit() {
        XCTAssertEqual(TableSizePickerModel.clamped(rows: 0, columns: 120), TableSize(rows: 1, columns: 100))
        XCTAssertEqual(TableSizePickerModel.clamped(rows: 12, columns: 14), TableSize(rows: 12, columns: 14))
    }
}
