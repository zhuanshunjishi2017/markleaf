import XCTest
@testable import MarkLeaf

final class DocumentDispositionSheetTests: XCTestCase {
    func testSavedAlertMarksOnlyDontSaveAsDestructive() {
        let alert = DocumentDispositionSheetPresenter.makeSavedAlert(filename: "notes.md")

        XCTAssertEqual(alert.buttons.map(\.title), [L10n.t("保存"), L10n.t("取消"), L10n.t("不保存")])
        XCTAssertEqual(alert.buttons.map(\.hasDestructiveAction), [false, false, true])
        XCTAssertEqual(alert.buttons[1].keyEquivalent, "\u{1b}")
    }

    func testUntitledAlertMarksOnlyDeleteAsDestructive() {
        let alert = DocumentDispositionSheetPresenter.makeUntitledAlert()

        XCTAssertEqual(alert.buttons.map(\.title), [L10n.t("保存…"), L10n.t("取消"), L10n.t("删除")])
        XCTAssertEqual(alert.buttons.map(\.hasDestructiveAction), [false, false, true])
        XCTAssertEqual(alert.buttons[1].keyEquivalent, "\u{1b}")
    }
}
