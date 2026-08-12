import XCTest
@testable import MarkLeaf

final class SidebarLayoutTests: XCTestCase {
    func testSavedWidthBelowMinimumClampsToTwoHundred() {
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(150), 200)
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(199), 200)
    }

    func testValidSavedWidthsArePreserved() {
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(200), 200)
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(230), 230)
        XCTAssertEqual(SidebarLayout.clampedWorkspaceWidth(320), 320)
    }

    func testMaximumSidebarWidthReservesFourHundredTwentyForEditor() {
        XCTAssertEqual(SidebarLayout.maximumSidebarWidth(totalWidth: 900), 480)
    }
}
