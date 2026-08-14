import XCTest
@testable import MarkLeaf

final class EditorSessionTableCommandTests: XCTestCase {
    func testTableCommandTextEncodesSelectedRowsAndColumns() {
        XCTAssertEqual(EditorSession.tableCommandText(rows: 7, columns: 8), "7,8")
    }

    func testTableCommandTextClampsInvalidDimensionsToDefault() {
        XCTAssertEqual(EditorSession.tableCommandText(rows: 0, columns: 8), "3,3")
        XCTAssertEqual(EditorSession.tableCommandText(rows: 101, columns: 8), "3,3")
    }
}
