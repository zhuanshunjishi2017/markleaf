import XCTest
@testable import MarkLeaf

final class SettingsRigidityTests: XCTestCase {
    func testSaveOnDocumentSwitchDefaultsTrue() throws {
        let data = Data("{\"schemaVersion\":3}".utf8)
        let settings = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertTrue(settings.saveOnDocumentSwitch)
    }

    func testSaveOnDocumentSwitchRoundTripsFalse() throws {
        var settings = AppSettings()
        settings.saveOnDocumentSwitch = false
        let data = try JSONEncoder().encode(settings)
        let decoded = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertFalse(decoded.saveOnDocumentSwitch)
    }

    func testClampSettingRangesAlignsWithWindows() {
        var settings = AppSettings()
        settings.snapshotIntervalSeconds = 5
        settings.visualLineHeight = 0.5
        settings.visualFontSize = 99
        settings.visualMaxContentWidth = 100
        settings.sourceFontSize = 1
        settings.sourceIndentWidth = 20

        settings.clampSettingRanges()

        XCTAssertEqual(settings.snapshotIntervalSeconds, 10)
        XCTAssertEqual(settings.visualLineHeight, 1.0)
        XCTAssertEqual(settings.visualFontSize, 24)
        XCTAssertEqual(settings.visualMaxContentWidth, 600)
        XCTAssertEqual(settings.sourceFontSize, 12)
        XCTAssertEqual(settings.sourceIndentWidth, 8)
    }

    func testClampKeepsInRangeValues() {
        var settings = AppSettings()
        settings.snapshotIntervalSeconds = 60
        settings.visualLineHeight = 1.6
        settings.visualFontSize = 16
        settings.visualMaxContentWidth = 820
        settings.sourceFontSize = 14
        settings.sourceIndentWidth = 2

        settings.clampSettingRanges()

        XCTAssertEqual(settings.snapshotIntervalSeconds, 60)
        XCTAssertEqual(settings.visualLineHeight, 1.6)
        XCTAssertEqual(settings.visualFontSize, 16)
        XCTAssertEqual(settings.visualMaxContentWidth, 820)
        XCTAssertEqual(settings.sourceFontSize, 14)
        XCTAssertEqual(settings.sourceIndentWidth, 2)
    }


    func testExternalFileOpenModeDefaultsToNewWindowWhenKeyIsMissing() throws {
        let data = Data("{\"schemaVersion\":3}".utf8)
        let settings = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertEqual(settings.externalFileOpenMode, .newWindow)
    }

    func testExternalFileOpenModeRoundTripsCurrentWindow() throws {
        var settings = AppSettings()
        settings.externalFileOpenMode = .currentWindow
        let decoded = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(decoded.externalFileOpenMode, .currentWindow)
    }

}
