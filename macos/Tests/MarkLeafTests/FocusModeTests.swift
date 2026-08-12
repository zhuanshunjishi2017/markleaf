import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class FocusModeTests: XCTestCase {
    func testFocusModeHidesChromeAndRestoresPreviousVisibility() {
        let session = EditorSession()
        let controller = EditorWindowController(session: session)
        session.sidebarVisible = true
        session.statusBarVisible = false

        controller.toggleFocusMode()

        XCTAssertTrue(controller.isFocusMode)
        XCTAssertFalse(session.sidebarVisible)
        XCTAssertFalse(session.statusBarVisible)

        controller.exitFocusMode()

        XCTAssertFalse(controller.isFocusMode)
        XCTAssertTrue(session.sidebarVisible)
        XCTAssertFalse(session.statusBarVisible)
    }

    func testSecondToggleExitsFocusMode() {
        let session = EditorSession()
        let controller = EditorWindowController(session: session)
        session.sidebarVisible = false
        session.statusBarVisible = true

        controller.toggleFocusMode()
        controller.toggleFocusMode()

        XCTAssertFalse(controller.isFocusMode)
        XCTAssertFalse(session.sidebarVisible)
        XCTAssertTrue(session.statusBarVisible)
    }

    func testF11EntersAndEscapeExitsFocusMode() {
        let session = EditorSession()
        let controller = EditorWindowController(session: session)

        XCTAssertTrue(controller.handleFocusModeKey(keyCode: 103))
        XCTAssertTrue(controller.isFocusMode)
        XCTAssertTrue(controller.handleFocusModeKey(keyCode: 53))
        XCTAssertFalse(controller.isFocusMode)
        XCTAssertFalse(controller.handleFocusModeKey(keyCode: 53))
    }
}
