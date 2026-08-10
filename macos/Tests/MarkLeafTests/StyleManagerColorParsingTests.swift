import XCTest
@testable import MarkLeaf

final class StyleManagerColorParsingTests: XCTestCase {
    func testParsesBgPrimaryHex() {
        let css = ":root { --bg-primary: #1E1E1E; }"
        XCTAssertEqual(StyleManager.parseColorVariable("--bg-primary", in: css), "#1E1E1E")
    }

    func testParsesShortHex() {
        let css = "--bg-primary: #123;"
        XCTAssertEqual(StyleManager.parseColorVariable("--bg-primary", in: css), "#123")
    }

    func testReturnsNilWhenVariableAbsent() {
        let css = ":root { --text-primary: #000000; }"
        XCTAssertNil(StyleManager.parseColorVariable("--bg-primary", in: css))
    }
}
