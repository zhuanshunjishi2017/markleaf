import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class OutlineWorkspaceStyleTests: XCTestCase {
    func testOutlineAndWorkspaceUseIdenticalNativeRowPresentation() throws {
        let workspace = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
        workspace.configure(session: EditorSession())
        let outline = OutlineTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
        outline.configure(session: EditorSession())
        let file = WorkspaceEntry(name: "file.md", path: "/probe/file.md", isDirectory: false)
        let heading = OutlineHeading(level: 1, text: "Heading", position: 0)

        let workspaceCell = try XCTUnwrap(
            workspace.outlineView(workspace, viewFor: nil, item: file) as? NSTableCellView
        )
        let outlineCell = try XCTUnwrap(
            outline.outlineView(outline, viewFor: nil, item: heading) as? NSTableCellView
        )
        let workspaceRow = try XCTUnwrap(workspace.outlineView(workspace, rowViewForItem: file))
        let outlineRow = try XCTUnwrap(outline.outlineView(outline, rowViewForItem: heading))

        XCTAssertEqual(outline.rowSizeStyle, workspace.rowSizeStyle)
        XCTAssertEqual(outline.rowHeight, workspace.rowHeight)
        XCTAssertEqual(outline.intercellSpacing, workspace.intercellSpacing)
        XCTAssertEqual(outline.selectionHighlightStyle, workspace.selectionHighlightStyle)
        XCTAssertEqual(outlineCell.textField?.font?.pointSize, workspaceCell.textField?.font?.pointSize)
        XCTAssertEqual(outlineCell.textField?.font?.pointSize, 13)
        XCTAssertFalse(
            NSFontManager.shared.traits(of: try XCTUnwrap(outlineCell.textField?.font))
                .contains(.boldFontMask)
        )
        XCTAssertTrue(workspaceRow is FinderWorkspaceRowView)
        XCTAssertTrue(outlineRow is FinderWorkspaceRowView)
    }

    func testOutlinePreservesHeadingHierarchyThroughIndentation() throws {
        let outline = OutlineTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
        outline.configure(session: EditorSession())
        let levelOne = OutlineHeading(level: 1, text: "One", position: 0)
        let levelThree = OutlineHeading(level: 3, text: "Three", position: 10)

        let levelOneCell = try XCTUnwrap(
            outline.outlineView(outline, viewFor: nil, item: levelOne) as? NSTableCellView
        )
        let levelThreeCell = try XCTUnwrap(
            outline.outlineView(outline, viewFor: nil, item: levelThree) as? NSTableCellView
        )
        let levelOneLeading = try XCTUnwrap(levelOneCell.constraints.first {
            $0.identifier == OutlineTreeView.headingLeadingConstraintIdentifier
        }).constant
        let levelThreeLeading = try XCTUnwrap(levelThreeCell.constraints.first {
            $0.identifier == OutlineTreeView.headingLeadingConstraintIdentifier
        }).constant

        XCTAssertGreaterThan(levelThreeLeading, levelOneLeading)
    }
}
