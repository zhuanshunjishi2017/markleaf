import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class PreferencesLanguageRefreshTests: XCTestCase {
    func testPreferencesRestoresRequestedPageAndClampsInvalidIndex() {
        let general = PreferencesWindowController(
            styles: [],
            themes: [],
            initialSelectedPageIndex: 3
        )
        let invalid = PreferencesWindowController(
            styles: [],
            themes: [],
            initialSelectedPageIndex: 99
        )

        XCTAssertEqual(general.selectedPageIndex, 3)
        XCTAssertEqual(invalid.selectedPageIndex, 4)
    }

    func testVisiblePreferencesProducesRestorationWithPageAndFrame() {
        let frame = NSRect(x: 120, y: 140, width: 640, height: 540)
        let state = PreferencesRefreshState(
            selectedPageIndex: 3,
            frame: frame,
            wasVisible: true
        )

        XCTAssertEqual(
            state.restoration,
            PreferencesRestoration(selectedPageIndex: 3, frame: frame)
        )
    }

    func testHiddenPreferencesDoesNotProduceRestoration() {
        let state = PreferencesRefreshState(
            selectedPageIndex: 3,
            frame: NSRect(x: 120, y: 140, width: 640, height: 540),
            wasVisible: false
        )

        XCTAssertNil(state.restoration)
    }
}
