import XCTest
@testable import MarkLeaf

final class WorkspaceMovePolicyTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    func testFileCanMoveIntoDifferentWorkspaceFolder() throws {
        let source = root.appendingPathComponent("note.md")
        let targetDirectory = root.appendingPathComponent("archive", isDirectory: true)
        try Data("note".utf8).write(to: source)
        try FileManager.default.createDirectory(at: targetDirectory, withIntermediateDirectories: true)

        let destination = try WorkspaceMovePolicy.destination(
            source: source,
            targetDirectory: targetDirectory,
            workspaceRoot: root
        )

        XCTAssertEqual(destination.path, targetDirectory.appendingPathComponent("note.md").path)
    }

    func testMoveToCurrentParentIsRejected() throws {
        let source = root.appendingPathComponent("note.md")
        try Data("note".utf8).write(to: source)

        XCTAssertThrowsError(try WorkspaceMovePolicy.destination(
            source: source,
            targetDirectory: root,
            workspaceRoot: root
        )) { error in
            XCTAssertEqual(error as? WorkspaceMoveError, .sameParent)
        }
    }

    func testExistingDestinationIsNeverOverwritten() throws {
        let source = root.appendingPathComponent("note.md")
        let targetDirectory = root.appendingPathComponent("archive", isDirectory: true)
        try FileManager.default.createDirectory(at: targetDirectory, withIntermediateDirectories: true)
        try Data("source".utf8).write(to: source)
        try Data("existing".utf8).write(to: targetDirectory.appendingPathComponent("note.md"))

        XCTAssertThrowsError(try WorkspaceMovePolicy.destination(
            source: source,
            targetDirectory: targetDirectory,
            workspaceRoot: root
        )) { error in
            XCTAssertEqual(error as? WorkspaceMoveError, .destinationExists)
        }
    }

    func testDirectoryCannotMoveIntoItsDescendant() throws {
        let source = root.appendingPathComponent("folder", isDirectory: true)
        let child = source.appendingPathComponent("child", isDirectory: true)
        try FileManager.default.createDirectory(at: child, withIntermediateDirectories: true)

        XCTAssertThrowsError(try WorkspaceMovePolicy.destination(
            source: source,
            targetDirectory: child,
            workspaceRoot: root
        )) { error in
            XCTAssertEqual(error as? WorkspaceMoveError, .descendantTarget)
        }
    }

    func testMoveCannotCrossWorkspaceBoundary() throws {
        let source = root.appendingPathComponent("note.md")
        let outside = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try Data("note".utf8).write(to: source)
        try FileManager.default.createDirectory(at: outside, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: outside) }

        XCTAssertThrowsError(try WorkspaceMovePolicy.destination(
            source: source,
            targetDirectory: outside,
            workspaceRoot: root
        )) { error in
            XCTAssertEqual(error as? WorkspaceMoveError, .outsideWorkspace)
        }
    }
}
