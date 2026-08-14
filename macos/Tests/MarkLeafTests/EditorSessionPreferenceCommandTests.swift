import XCTest
@testable import MarkLeaf

final class EditorSessionPreferenceCommandTests: XCTestCase {
    func testBlockHandleVisibilityCommandUsesOneWhenEnabled() {
        var settings = AppSettings()
        settings.showParagraphBlockHandle = true

        XCTAssertEqual(EditorSession.blockHandleVisibilityCommandText(for: settings), "1")
    }

    func testBlockHandleVisibilityCommandUsesZeroWhenDisabled() {
        var settings = AppSettings()
        settings.showParagraphBlockHandle = false

        XCTAssertEqual(EditorSession.blockHandleVisibilityCommandText(for: settings), "0")
    }
}
