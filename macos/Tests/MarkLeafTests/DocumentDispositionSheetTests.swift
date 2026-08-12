import XCTest
@testable import MarkLeaf

final class DocumentDispositionSheetTests: XCTestCase {
    func testSavedSheetUsesApprovedHorizontalActionOrder() {
        let spec = DocumentDispositionSheetSpec.saved(filename: "notes.md")

        XCTAssertEqual(spec.actions.map(\.title), [L10n.t("不保存"), L10n.t("取消"), L10n.t("保存")])
        XCTAssertEqual(spec.defaultActionIndex, 2)
        XCTAssertEqual(spec.cancelActionIndex, 1)
        XCTAssertEqual(spec.savedChoice(forActionIndex: 0), .discard)
        XCTAssertEqual(spec.savedChoice(forActionIndex: 1), .cancel)
        XCTAssertEqual(spec.savedChoice(forActionIndex: 2), .save)
    }

    func testUntitledSheetUsesApprovedHorizontalActionOrder() {
        let spec = DocumentDispositionSheetSpec.untitled()

        XCTAssertEqual(spec.actions.map(\.title), [L10n.t("删除"), L10n.t("取消"), L10n.t("保存…")])
        XCTAssertEqual(spec.defaultActionIndex, 2)
        XCTAssertEqual(spec.cancelActionIndex, 1)
        XCTAssertEqual(spec.untitledChoice(forActionIndex: 0), .delete)
        XCTAssertEqual(spec.untitledChoice(forActionIndex: 1), .cancel)
        XCTAssertEqual(spec.untitledChoice(forActionIndex: 2), .saveAs)
    }

    func testUnknownActionIsNeverDestructive() {
        XCTAssertEqual(DocumentDispositionSheetSpec.saved(filename: "notes.md").savedChoice(forActionIndex: 99), .cancel)
        XCTAssertEqual(DocumentDispositionSheetSpec.untitled().untitledChoice(forActionIndex: 99), .cancel)
    }
}
