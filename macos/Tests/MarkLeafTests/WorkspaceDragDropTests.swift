import AppKit
import XCTest
@testable import MarkLeaf

final class WorkspaceDragDropTests: XCTestCase {
    @MainActor
    func testDragExportsFileURLAndPrivateWorkspacePath() throws {
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        let entry = WorkspaceEntry(name: "note.md", path: "/probe/note.md", isDirectory: false)

        let writer = try XCTUnwrap(outline.outlineView(outline, pasteboardWriterForItem: entry) as? NSPasteboardItem)

        XCTAssertEqual(writer.string(forType: .fileURL), URL(fileURLWithPath: entry.path).absoluteString)
        XCTAssertEqual(writer.string(forType: WorkspaceTreeView.localDragPasteboardType), entry.path)
    }

    @MainActor
    func testLocalDragMovesAndExternalDragCopies() {
        let outline = WorkspaceTreeView(frame: .zero)

        XCTAssertEqual(outline.draggingSession(NSDraggingSession(), sourceOperationMaskFor: .withinApplication), .move)
        XCTAssertEqual(outline.draggingSession(NSDraggingSession(), sourceOperationMaskFor: .outsideApplication), .copy)
    }

    @MainActor
    func testDropTargetUsesDirectoryOrWorkspaceRootButRejectsFiles() {
        let outline = WorkspaceTreeView(frame: .zero)
        let folder = WorkspaceEntry(name: "folder", path: "/probe/folder", isDirectory: true)
        let file = WorkspaceEntry(name: "note.md", path: "/probe/note.md", isDirectory: false)

        XCTAssertEqual(outline.dropTargetDirectory(for: folder, workspaceRoot: "/probe")?.path, "/probe/folder")
        XCTAssertEqual(outline.dropTargetDirectory(for: nil, workspaceRoot: "/probe")?.path, "/probe")
        XCTAssertNil(outline.dropTargetDirectory(for: file, workspaceRoot: "/probe"))
    }
}
