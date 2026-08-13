import XCTest
@testable import MarkLeaf

final class FormatPainterMenuTests: XCTestCase {
    func testFormatMenuContainsFormatPainterShortcuts() {
        let main = NativeMenuBuilder().build()
        let format = main.items.first { $0.title == L10n.t("格式") }?.submenu
        let item = format?.items.first { ($0.representedObject as? String) == "formatPainter" }
        XCTAssertEqual(item?.title, L10n.t("格式刷"))
        XCTAssertEqual(item?.keyEquivalent, "c")
        XCTAssertEqual(item?.keyEquivalentModifierMask, [.command, .shift])

        let applyItem = format?.items.first { ($0.representedObject as? String) == "formatPainterApply" }
        XCTAssertEqual(applyItem?.title, L10n.t("应用格式刷"))
        XCTAssertEqual(applyItem?.keyEquivalent, "v")
        XCTAssertEqual(applyItem?.keyEquivalentModifierMask, [.command, .shift])
    }

    func testFormatPainterHasFourLanguageCopy() {
        XCTAssertEqual(L10n.translate("格式刷", language: "zh-Hans"), "格式刷")
        XCTAssertEqual(L10n.translate("格式刷", language: "zh-Hant"), "格式刷")
        XCTAssertEqual(L10n.translate("格式刷", language: "en"), "Format Painter")
        XCTAssertEqual(L10n.translate("格式刷", language: "ja"), "書式のコピー/貼り付け")
        XCTAssertEqual(L10n.translate("应用格式刷", language: "en"), "Apply Format Painter")
    }
}
