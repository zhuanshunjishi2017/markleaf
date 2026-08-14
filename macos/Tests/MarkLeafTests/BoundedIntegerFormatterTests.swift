import AppKit
import XCTest
@testable import MarkLeaf

final class BoundedIntegerFormatterTests: XCTestCase {
    private func makeFormatter(_ range: ClosedRange<Int>) -> BoundedIntegerFormatter {
        BoundedIntegerFormatter(min: range.lowerBound, max: range.upperBound)
    }

    private func accepts(_ formatter: BoundedIntegerFormatter, _ input: String) -> Bool {
        var error: NSString?
        return formatter.isPartialStringValid(input, newEditingString: nil, errorDescription: &error)
    }

    func testAcceptsEmptyAndPositiveIntegersWithinRange() {
        let formatter = makeFormatter(1...100)
        XCTAssertTrue(accepts(formatter, ""))
        XCTAssertTrue(accepts(formatter, "1"))
        XCTAssertTrue(accepts(formatter, "100"))
    }

    func testRejectsNonDigitsAndOutOfRangeValues() {
        let formatter = makeFormatter(1...100)
        XCTAssertFalse(accepts(formatter, "abc"))
        XCTAssertFalse(accepts(formatter, "1.5"))
        XCTAssertFalse(accepts(formatter, "0"))
        XCTAssertFalse(accepts(formatter, "-1"))
        XCTAssertFalse(accepts(formatter, "101"))
    }

    func testCustomTableUsesMaxCustomSize() {
        let formatter = makeFormatter(1...TableSizePickerModel.maxCustomSize)
        XCTAssertTrue(accepts(formatter, "100"))
        XCTAssertFalse(accepts(formatter, "101"))
    }
}
