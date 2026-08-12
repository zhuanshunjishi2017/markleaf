import XCTest
@testable import MarkLeaf

final class PreferencesParityTests: XCTestCase {
    func testThemeDefaultsSeparateLightAndDarkChoices() {
        let themes = [
            ColorThemeInfo(id: "colors-white-only", displayName: "White", css: "", isDark: false),
            ColorThemeInfo(id: "colors-morandi", displayName: "Morandi", css: "", isDark: false),
            ColorThemeInfo(id: "colors-dark", displayName: "Dark", css: "", isDark: true),
            ColorThemeInfo(id: "colors-forest", displayName: "Forest", css: "", isDark: true),
        ]

        let model = ThemeDefaultsSelectionModel(
            themes: themes,
            selectedLightID: "colors-morandi",
            selectedDarkID: "colors-forest"
        )

        XCTAssertEqual(model.lightThemes.map(\.id), ["colors-white-only", "colors-morandi"])
        XCTAssertEqual(model.darkThemes.map(\.id), ["colors-dark", "colors-forest"])
        XCTAssertEqual(model.selectedLightIndex, 1)
        XCTAssertEqual(model.selectedDarkIndex, 1)
    }

    func testMissingThemeDefaultsSelectFirstCompatibleTheme() {
        let themes = [
            ColorThemeInfo(id: "colors-white-only", displayName: "White", css: "", isDark: false),
            ColorThemeInfo(id: "colors-dark", displayName: "Dark", css: "", isDark: true),
        ]

        let model = ThemeDefaultsSelectionModel(
            themes: themes,
            selectedLightID: "missing-light",
            selectedDarkID: "missing-dark"
        )

        XCTAssertEqual(model.selectedLightIndex, 0)
        XCTAssertEqual(model.selectedDarkIndex, 0)
    }
}
