import XCTest
@testable import MarkLeaf

final class DocumentDispositionSheetTests: XCTestCase {
    func testSavedSheetUsesApprovedVerticalActionOrder() {
        let spec = DocumentDispositionSheetSpec.saved(filename: "notes.md")

        XCTAssertEqual(spec.title, L10n.f("是否保存对“%@”的修改？", "notes.md"))
        XCTAssertEqual(spec.actions.map(\.title), [L10n.t("保存"), L10n.t("不保存"), L10n.t("取消")])
        XCTAssertEqual(spec.actions.map(\.role), [.default, .destructive, .cancel])
        XCTAssertEqual(spec.defaultActionIndex, 0)
        XCTAssertEqual(spec.cancelActionIndex, 2)
        XCTAssertEqual(spec.savedChoice(forActionIndex: 0), .save)
        XCTAssertEqual(spec.savedChoice(forActionIndex: 1), .discard)
        XCTAssertEqual(spec.savedChoice(forActionIndex: 2), .cancel)
    }

    func testUntitledSheetUsesApprovedVerticalActionOrder() {
        let spec = DocumentDispositionSheetSpec.untitled()

        XCTAssertEqual(spec.title, L10n.t("是否保存此文档？"))
        XCTAssertEqual(spec.actions.map(\.title), [L10n.t("保存…"), L10n.t("删除"), L10n.t("取消")])
        XCTAssertEqual(spec.actions.map(\.role), [.default, .destructive, .cancel])
        XCTAssertEqual(spec.defaultActionIndex, 0)
        XCTAssertEqual(spec.cancelActionIndex, 2)
        XCTAssertEqual(spec.untitledChoice(forActionIndex: 0), .saveAs)
        XCTAssertEqual(spec.untitledChoice(forActionIndex: 1), .delete)
        XCTAssertEqual(spec.untitledChoice(forActionIndex: 2), .cancel)
    }

    func testUnknownActionIsNeverDestructive() {
        XCTAssertEqual(DocumentDispositionSheetSpec.saved(filename: "notes.md").savedChoice(forActionIndex: 99), .cancel)
        XCTAssertEqual(DocumentDispositionSheetSpec.untitled().untitledChoice(forActionIndex: 99), .cancel)
    }
}
