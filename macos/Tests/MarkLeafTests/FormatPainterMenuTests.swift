import XCTest
@testable import MarkLeaf

final class FormatPainterMenuTests: XCTestCase {
    func testFormatMenuContainsFormatPainterWithoutShortcut() {
        let main = NativeMenuBuilder().build()
        let format = main.items.first { $0.title == L10n.t("格式") }?.submenu
        let item = format?.items.first { ($0.representedObject as? String) == "formatPainter" }
        XCTAssertEqual(item?.title, L10n.t("格式刷"))
        XCTAssertEqual(item?.keyEquivalent, "")
    }

    func testFormatPainterHasFourLanguageCopy() {
        XCTAssertEqual(L10n.translate("格式刷", language: "zh-Hans"), "格式刷")
        XCTAssertEqual(L10n.translate("格式刷", language: "zh-Hant"), "格式刷")
        XCTAssertEqual(L10n.translate("格式刷", language: "en"), "Format Painter")
        XCTAssertEqual(L10n.translate("格式刷", language: "ja"), "書式のコピー/貼り付け")
    }
}
