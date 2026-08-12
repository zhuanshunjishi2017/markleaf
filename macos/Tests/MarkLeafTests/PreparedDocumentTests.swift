import XCTest
@testable import MarkLeaf

final class PreparedDocumentTests: XCTestCase {
    func testReadsValidMarkdownAndStandardizesURL() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }

        let url = dir.appendingPathComponent("sample.md")
        try Data("# 标题\n正文".utf8).write(to: url)

        let prepared = try PreparedDocument.read(from: url)
        XCTAssertEqual(prepared.markdown, "# 标题\n正文")
        XCTAssertEqual(prepared.url, url.standardizedFileURL.resolvingSymlinksInPath())
    }

    func testMissingPathThrows() {
        let url = URL(fileURLWithPath: "/tmp/definitely-missing-markleaf-\(UUID().uuidString).md")
        XCTAssertThrowsError(try PreparedDocument.read(from: url))
    }

    func testInvalidUTF8Throws() throws {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }

        let url = dir.appendingPathComponent("invalid.md")
        try Data([0xFF, 0xFE, 0x00, 0xC3, 0x28]).write(to: url)

        XCTAssertThrowsError(try PreparedDocument.read(from: url))
    }

    func testInvalidIncomingFileDoesNotStartDisposition() throws {
        let session = EditorSession()
        session.loadDocument(markdown: "original", fileURL: URL(fileURLWithPath: "/tmp/current.md"))
        let originalID = session.currentDocumentIdentifier

        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: dir) }

        let url = dir.appendingPathComponent("invalid.md")
        try Data([0xFF, 0xFE, 0x00]).write(to: url)

        session.openDocument(at: url)

        XCTAssertEqual(session.dispositionRequestCount, 0)
        XCTAssertEqual(session.documentURL, URL(fileURLWithPath: "/tmp/current.md"))
        XCTAssertFalse(session.isDirty)
        XCTAssertEqual(session.currentDocumentIdentifier, originalID)
    }
}
