import XCTest
@testable import MarkLeaf

final class StyleManagerThemeDefaultsTests: XCTestCase {
    private func makeManager(themeIDs: [String]) throws -> StyleManager {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: dir) }
        for id in themeIDs {
            let css = "/* @name: \(id) */\n:root { --bg-primary: #fff; }\n"
            try Data(css.utf8).write(to: dir.appendingPathComponent("\(id).css"))
        }
        return try XCTUnwrap(StyleManager(directories: [dir]))
    }

    func testReturnsDefaultLightAndDark() throws {
        let manager = try makeManager(themeIDs: ["colors-white-only", "colors-dark", "colors-rose"])
        XCTAssertEqual(manager.defaultThemeID(forDark: false), "colors-white-only")
        XCTAssertEqual(manager.defaultThemeID(forDark: true), "colors-dark")
    }

    func testFallsBackToDefaultThemeId() throws {
        let manager = try makeManager(themeIDs: ["colors-rose"])
        XCTAssertEqual(manager.defaultThemeID(forDark: false), manager.defaultThemeId)
        XCTAssertEqual(manager.defaultThemeID(forDark: true), manager.defaultThemeId)
    }
}
