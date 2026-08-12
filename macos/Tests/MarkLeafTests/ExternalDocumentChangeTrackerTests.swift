import XCTest
@testable import MarkLeaf

final class ExternalDocumentChangeTrackerTests: XCTestCase {
    private func makeFile(_ text: String) throws -> URL {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("markleaf-change-tracker-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: directory) }
        let url = directory.appendingPathComponent("document.md")
        try text.write(to: url, atomically: false, encoding: .utf8)
        return url
    }

    func testAtomicSelfWriteIsIgnoredAfterAcceptingReplacementVersion() throws {
        let url = try makeFile("before")
        let tracker = ExternalDocumentChangeTracker()
        try tracker.acceptCurrentVersion(at: url)

        tracker.beginSelfWrite()
        try "after".write(to: url, atomically: true, encoding: .utf8)
        try tracker.finishSelfWrite(at: url)

        XCTAssertEqual(try tracker.decision(forEventAt: url), .ignore)
    }

    func testExternalAtomicReplacementRequiresRebindThenPrompt() throws {
        let url = try makeFile("before")
        let tracker = ExternalDocumentChangeTracker()
        try tracker.acceptCurrentVersion(at: url)
        tracker.beginSelfWrite()
        try "saved".write(to: url, atomically: true, encoding: .utf8)
        try tracker.finishSelfWrite(at: url)

        try "external".write(to: url, atomically: true, encoding: .utf8)

        XCTAssertEqual(try tracker.decision(forEventAt: url), .rebindAndRecheck)
        XCTAssertEqual(try tracker.decision(forEventAt: url), .presentExternalChange)
    }

    func testAcceptingIgnoredVersionAllowsLaterReplacementToPrompt() throws {
        let url = try makeFile("before")
        let tracker = ExternalDocumentChangeTracker()
        try tracker.acceptCurrentVersion(at: url)

        try "external one".write(to: url, atomically: true, encoding: .utf8)
        XCTAssertEqual(try tracker.decision(forEventAt: url), .rebindAndRecheck)
        XCTAssertEqual(try tracker.decision(forEventAt: url), .presentExternalChange)
        try tracker.acceptCurrentVersion(at: url)

        try "external two".write(to: url, atomically: true, encoding: .utf8)
        XCTAssertEqual(try tracker.decision(forEventAt: url), .rebindAndRecheck)
        XCTAssertEqual(try tracker.decision(forEventAt: url), .presentExternalChange)
    }

    func testMissingFileReturnsMissingDecision() throws {
        let url = try makeFile("before")
        let tracker = ExternalDocumentChangeTracker()
        try tracker.acceptCurrentVersion(at: url)

        try FileManager.default.removeItem(at: url)

        XCTAssertEqual(try tracker.decision(forEventAt: url), .missing)
    }
}
