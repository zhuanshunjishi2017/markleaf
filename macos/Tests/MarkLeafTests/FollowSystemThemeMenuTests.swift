import XCTest
@testable import MarkLeaf

final class FollowSystemThemeMenuTests: XCTestCase {
    func testAppearanceMenuHasFollowSystemToggle() {
        let menu = NativeMenuBuilder().build()
        let appearance = menu.items.first { $0.title == L10n.t("外观") }?.submenu
        let commands = appearance?.items.compactMap { $0.representedObject as? String } ?? []
        XCTAssertTrue(commands.contains("toggleFollowSystemTheme"))
    }
}
