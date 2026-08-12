import XCTest
@testable import MarkLeaf

final class CjkGlyphPreferenceTests: XCTestCase {
    func testMissingCjkPreferenceDefaultsToSimplifiedChinese() throws {
        let settings = try JSONDecoder().decode(AppSettings.self, from: Data("{\"schemaVersion\":3}".utf8))
        XCTAssertEqual(settings.cjkLanguageTag, .simplifiedChinese)
        XCTAssertEqual(settings.cjkLanguageTag.rawValue, "zh-Hans")
    }

    func testAllCjkPreferencesRoundTrip() throws {
        for tag in CJKLanguageTag.allCases {
            var settings = AppSettings()
            settings.cjkLanguageTag = tag
            let decoded = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
            XCTAssertEqual(decoded.cjkLanguageTag, tag)
        }
    }

    func testCjkScriptSetsLangAttributeAndCssVariable() {
        XCTAssertEqual(
            EditorSession.cjkLanguageScript(for: .japanese),
            "document.documentElement.setAttribute('lang','ja');document.documentElement.style.setProperty('--ml-cjk-lang','ja');"
        )
    }
}
