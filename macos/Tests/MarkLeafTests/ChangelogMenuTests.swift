import XCTest
@testable import MarkLeaf

final class ChangelogMenuTests: XCTestCase {
    func testHelpMenuHasChangelogCommand() {
        let menu = NativeMenuBuilder().build()
        let helpMenu = menu.items.last?.submenu
        let commands = helpMenu?.items.compactMap { $0.representedObject as? String } ?? []
        XCTAssertTrue(commands.contains("openChangelog"))
    }
}
