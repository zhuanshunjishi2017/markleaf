import XCTest
@testable import MarkLeaf

final class AppSettingsFollowSystemTests: XCTestCase {
    func testDefaultsToFollowSystemWhenFieldMissing() throws {
        let data = Data("{\"schemaVersion\":3}".utf8)
        let settings = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertTrue(settings.followSystemTheme)
    }

    func testRoundTripFalse() throws {
        var settings = AppSettings()
        settings.followSystemTheme = false
        let data = try JSONEncoder().encode(settings)
        let decoded = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertFalse(decoded.followSystemTheme)
    }

    func testRoundTripTrue() throws {
        var settings = AppSettings()
        settings.followSystemTheme = true
        let data = try JSONEncoder().encode(settings)
        let decoded = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertTrue(decoded.followSystemTheme)
    }

    func testThemeDefaultsAndSidebarTabUseBackwardCompatibleDefaults() throws {
        let data = Data("{\"schemaVersion\":3}".utf8)

        let settings = try JSONDecoder().decode(AppSettings.self, from: data)

        XCTAssertEqual(settings.defaultLightThemeID, "colors-white-only")
        XCTAssertEqual(settings.defaultDarkThemeID, "colors-dark")
        XCTAssertEqual(settings.sidebarTab, "workspace")
    }

    func testThemeDefaultsAndSidebarTabRoundTrip() throws {
        var settings = AppSettings()
        settings.defaultLightThemeID = "colors-morandi"
        settings.defaultDarkThemeID = "colors-forest"
        settings.sidebarTab = "outline"

        let data = try JSONEncoder().encode(settings)
        let decoded = try JSONDecoder().decode(AppSettings.self, from: data)

        XCTAssertEqual(decoded.defaultLightThemeID, "colors-morandi")
        XCTAssertEqual(decoded.defaultDarkThemeID, "colors-forest")
        XCTAssertEqual(decoded.sidebarTab, "outline")
    }

    func testParagraphBlockHandleDefaultsToEnabledWhenFieldMissing() throws {
        let data = Data("{\"schemaVersion\":3}".utf8)

        let settings = try JSONDecoder().decode(AppSettings.self, from: data)

        XCTAssertTrue(settings.showParagraphBlockHandle)
    }

    func testParagraphBlockHandleRoundTripsDisabled() throws {
        var settings = AppSettings()
        settings.showParagraphBlockHandle = false

        let data = try JSONEncoder().encode(settings)
        let decoded = try JSONDecoder().decode(AppSettings.self, from: data)

        XCTAssertFalse(decoded.showParagraphBlockHandle)
    }
}
