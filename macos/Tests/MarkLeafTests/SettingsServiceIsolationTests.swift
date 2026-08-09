import Foundation
import XCTest
@testable import MarkLeaf

final class SettingsServiceIsolationTests: XCTestCase {
    func testFirstSavePersistsAndReloadsUsingEnvironmentApplicationSupportRoot() {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("MarkLeafSettingsTests-\(UUID().uuidString)", isDirectory: true)
        defer { try? FileManager.default.removeItem(at: root) }

        let environment = ["MARKLEAF_APP_SUPPORT_DIR": root.path]
        let writer = SettingsService(environment: environment)
        writer.update { $0.startupAction = .openLastWorkspace }

        let settingsURL = root.appendingPathComponent("MarkLeaf/settings.json")
        XCTAssertTrue(FileManager.default.fileExists(atPath: settingsURL.path))

        let reader = SettingsService(environment: environment)
        reader.load()
        XCTAssertEqual(reader.settings.startupAction, .openLastWorkspace)
    }
}
