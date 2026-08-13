import AppKit
import XCTest
@testable import MarkLeaf

final class EditorContextMenuTests: XCTestCase {
    func testClientPointMapsToWebViewCoordinates() {
        XCTAssertEqual(
            EditorSession.editorContextMenuPoint(clientX: 42, clientY: 18, viewHeight: 600),
            NSPoint(x: 42, y: 582)
        )
    }
}
