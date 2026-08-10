import XCTest
@testable import MarkLeaf

final class AppLogCleanupTests: XCTestCase {
    private func makeLogFile(mtime: Date) -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: dir) }
        let file = dir.appendingPathComponent("markleaf-app.log")
        try? Data("log".utf8).write(to: file)
        try? FileManager.default.setAttributes([.modificationDate: mtime], ofItemAtPath: file.path)
        return file
    }

    func testCleanupDeletesOldLogFile() {
        let file = makeLogFile(mtime: Date(timeIntervalSinceNow: -8 * 24 * 3600))
        AppLog.cleanup(fileURL: file, olderThanDays: 7, now: Date())
        XCTAssertFalse(FileManager.default.fileExists(atPath: file.path))
    }

    func testCleanupKeepsRecentLogFile() {
        let file = makeLogFile(mtime: Date())
        AppLog.cleanup(fileURL: file, olderThanDays: 7, now: Date())
        XCTAssertTrue(FileManager.default.fileExists(atPath: file.path))
    }

    func testCleanupIgnoresMissingFile() {
        let missing = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathComponent("missing.log")
        AppLog.cleanup(fileURL: missing, olderThanDays: 7, now: Date())
    }
}
