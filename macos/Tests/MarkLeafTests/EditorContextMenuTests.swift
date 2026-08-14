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

    func testTableSizePickerMenuItemUsesTheSharedPickerView() {
        let item = tableSizePickerMenuItem { _ in }

        XCTAssertTrue(item.view is TableSizePickerView)
    }

    func testTableSizePickerSitsInsideInsertTableSubmenu() {
        let item = tableSizePickerSubmenu { _ in }

        XCTAssertEqual(item.title, L10n.t("插入表格"))
        XCTAssertEqual(item.submenu?.title, L10n.t("插入表格"))
        XCTAssertEqual(item.submenu?.items.count, 1)
        XCTAssertTrue(item.submenu?.items.first?.view is TableSizePickerView)
    }

    func testSubmenuComputedSizeTracksPicker() {
        let item = tableSizePickerSubmenu { _ in }
        let view = try? XCTUnwrap(item.submenu?.items.first?.view)
        let submenu = try? XCTUnwrap(item.submenu)

        XCTAssertLessThanOrEqual(submenu?.size.width ?? .infinity, (view?.frame.width ?? 0) + 4)
        XCTAssertLessThanOrEqual(submenu?.size.height ?? .infinity, (view?.frame.height ?? 0) + 16)
    }

    func testPickerFrameUsesCompactMetrics() {
        let view = TableSizePickerView()

        XCTAssertLessThan(view.frame.width, 250)
        XCTAssertLessThan(view.frame.height, 310)
    }

}
