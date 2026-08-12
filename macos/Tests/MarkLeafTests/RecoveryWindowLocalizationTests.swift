import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class RecoveryWindowLocalizationTests: XCTestCase {
    func testRecoveryIntroductionHasNaturalSingularCopyInAllLanguages() {
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 1, language: "zh-Hans"),
            "检测到 1 个未保存的文档。请选择要恢复的快照："
        )
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 1, language: "zh-Hant"),
            "偵測到 1 個未儲存的文件。請選擇要復原的快照："
        )
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 1, language: "en"),
            "Found 1 unsaved document. Choose a snapshot to recover:"
        )
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 1, language: "ja"),
            "1 件の未保存ドキュメントが見つかりました。復元するスナップショットを選択してください："
        )
    }

    func testRecoveryIntroductionHasNaturalPluralCopyInAllLanguages() {
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 2, language: "zh-Hans"),
            "检测到 2 个未保存的文档。请选择要恢复的快照："
        )
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 2, language: "zh-Hant"),
            "偵測到 2 個未儲存的文件。請選擇要復原的快照："
        )
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 2, language: "en"),
            "Found 2 unsaved documents. Choose a snapshot to recover:"
        )
        XCTAssertEqual(
            RecoveryWindowCopy.introduction(snapshotCount: 2, language: "ja"),
            "2 件の未保存ドキュメントが見つかりました。復元するスナップショットを選択してください："
        )
    }

    func testEnglishRecoveryWindowUsesEnglishIntroduction() {
        let snapshot = RecoverySnapshot(
            documentId: "probe",
            documentPath: nil,
            markdown: "# Probe",
            revision: 1,
            timestamp: Date(timeIntervalSince1970: 0),
            displayName: "Probe"
        )
        let controller = RecoveryWindowController(snapshots: [snapshot], language: "en")

        XCTAssertEqual(controller.window?.title, "Recover Unsaved Documents")
        XCTAssertEqual(
            controller.introductionLabel.stringValue,
            "Found 1 unsaved document. Choose a snapshot to recover:"
        )
        XCTAssertFalse(controller.introductionLabel.stringValue.contains("异常退出"))
    }

    func testObsoleteParentheticalRecoveryKeyIsRemoved() {
        let obsolete = "检测到 %d 个未保存的文档（上次异常退出遗留）。选择要恢复的快照："
        for language in ["en", "zh-Hant", "ja"] {
            XCTAssertFalse(L10n.translationKeys(for: language).contains(obsolete))
        }
    }
}
