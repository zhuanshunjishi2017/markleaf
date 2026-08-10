import XCTest
@testable import MarkLeaf

final class ThemeImportValidationTests: XCTestCase {
    func testThemeFileNameRequiresColorsPrefixAndCssExtension() {
        XCTAssertTrue(StyleManager.isThemeFileName("colors-dark.css"))
        XCTAssertFalse(StyleManager.isThemeFileName("theme.css"))
        XCTAssertFalse(StyleManager.isThemeFileName("colors-dark.txt"))
        XCTAssertFalse(StyleManager.isThemeFileName("colors-dark"))
    }

    func testThemeContentAcceptsTypeMarker() {
        let css = "/* @type: color-theme @name: 测试 */\n:root { --bg-primary: #fff; }"
        XCTAssertTrue(StyleManager.isValidThemeContent(css))
    }

    func testThemeContentAcceptsParseableColorVariable() {
        let css = ":root { --bg-primary: #1E1E1E; }"
        XCTAssertTrue(StyleManager.isValidThemeContent(css))
    }

    func testThemeContentRejectsGarbage() {
        XCTAssertFalse(StyleManager.isValidThemeContent("hello world"))
        XCTAssertFalse(StyleManager.isValidThemeContent(""))
    }
}
