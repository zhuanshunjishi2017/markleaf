import XCTest
@testable import MarkLeaf

final class ReleaseVersionTests: XCTestCase {
    func testFallbackVersionIs117() {
        XCTAssertEqual(AppVersion.fallback, "1.1.7")
    }

    func testBuildScriptPackagesVersion117() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let script = try String(contentsOf: root.appendingPathComponent("script/build_and_run.sh"))
        XCTAssertTrue(script.contains("APP_VERSION=\"1.1.7\""))
        XCTAssertEqual(script.components(separatedBy: "<string>$APP_VERSION</string>").count - 1, 2)
        XCTAssertTrue(script.contains("--build-only|build-only"))
        XCTAssertFalse(script.contains("<string>1.1.5</string>"))
    }
}
