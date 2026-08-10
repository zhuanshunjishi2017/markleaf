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
}
