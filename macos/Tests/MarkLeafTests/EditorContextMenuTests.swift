import AppKit
import XCTest
@testable import MarkLeaf

final class EditorContextMenuTests: XCTestCase {
    func testClientPointMapsToWebViewCoordinates() {
        // WKWebView 是 flipped 视图，clientY 与视图局部 y 一致。
        XCTAssertEqual(
            EditorSession.editorContextMenuPoint(clientX: 42, clientY: 18, viewHeight: 600, isFlipped: true),
            NSPoint(x: 42, y: 18)
        )
        XCTAssertEqual(
            EditorSession.editorContextMenuPoint(clientX: 42, clientY: 18, viewHeight: 600, isFlipped: false),
            NSPoint(x: 42, y: 582)
        )
    }

    func testFormatPainterUsesFreshCaretCapabilityForContextMenu() {
        XCTAssertTrue(EditorContextMenuState.formatPainterEnabled(
            isSourceMode: false,
            canStartFormatPainter: true,
            isFormatPainterArmed: false
        ))
        XCTAssertTrue(EditorContextMenuState.formatPainterEnabled(
            isSourceMode: false,
            canStartFormatPainter: false,
            isFormatPainterArmed: true
        ))
        XCTAssertTrue(EditorContextMenuState.formatPainterEnabled(
            isSourceMode: false,
            canStartFormatPainter: false,
            isFormatPainterArmed: false
        ))
        XCTAssertFalse(EditorContextMenuState.formatPainterEnabled(
            isSourceMode: true,
            canStartFormatPainter: true,
            isFormatPainterArmed: false
        ))
    }

    func testFormatPainterShortcutMenuDoesNotDependOnCachedCaretCapability() {
        XCTAssertTrue(EditorContextMenuState.formatPainterShortcutEnabled(isSourceMode: false))
        XCTAssertFalse(EditorContextMenuState.formatPainterShortcutEnabled(isSourceMode: true))
    }

}
