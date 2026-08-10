import XCTest
@testable import MarkLeaf

final class FollowSystemThemeMenuTests: XCTestCase {
    func testAppearanceThemeMenuHasFollowSystemToggle() {
        let menu = NativeMenuBuilder().build()
        let appearance = menu.items.first { $0.title == L10n.t("外观") }?.submenu
        let themeItem = appearance?.items.first { $0.title == L10n.t("颜色主题") && $0.submenu != nil }
        let themeMenu = themeItem?.submenu
        let commands = themeMenu?.items.compactMap { $0.representedObject as? String } ?? []
        XCTAssertTrue(commands.contains("toggleFollowSystemTheme"))
    }
}
