import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class OutlineWorkspaceStyleTests: XCTestCase {
    func testOutlineUsesWorkspaceTypographyAndSelectionStyle() throws {
        let outline = OutlineTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
        outline.configure(session: EditorSession())
        let heading = OutlineHeading(level: 1, text: "Heading", position: 0)

        let cell = try XCTUnwrap(outline.outlineView(outline, viewFor: nil, item: heading) as? NSTableCellView)
        let row = try XCTUnwrap(outline.outlineView(outline, rowViewForItem: heading))

        XCTAssertEqual(cell.textField?.font?.pointSize, 13)
        XCTAssertEqual(outline.selectionHighlightStyle, .sourceList)
        XCTAssertTrue(row is FinderWorkspaceRowView)
        XCTAssertTrue(outline.outlineView(outline, shouldSelectItem: heading))
    }
}
