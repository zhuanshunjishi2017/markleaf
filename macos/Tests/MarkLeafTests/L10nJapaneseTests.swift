import XCTest
@testable import MarkLeaf

final class L10nJapaneseTests: XCTestCase {
    func testJapaneseCoversExactlyTheEnglishKeys() {
        XCTAssertEqual(L10n.translationKeys(for: "ja"), L10n.translationKeys(for: "en"))
    }

    func testJapaneseSpotTranslations() {
        XCTAssertEqual(L10n.translate("保存", language: "ja"), "保存")
        XCTAssertEqual(L10n.translate("打开文件夹", language: "ja"), "フォルダを開く")
        XCTAssertEqual(L10n.translate("暂未打开工作区", language: "en"), "No workspace open")
        XCTAssertEqual(L10n.translate("暂未打开工作区", language: "zh-Hant"), "尚未開啟工作區")
        XCTAssertEqual(L10n.translate("暂未打开工作区", language: "ja"), "ワークスペースはまだ開かれていません")
        XCTAssertEqual(L10n.translate("与操作系统同步", language: "ja"), "OS と同期")
        XCTAssertEqual(L10n.translate("显示段落块句柄", language: "zh-Hant"), "顯示段落區塊控點")
        XCTAssertEqual(L10n.translate("显示段落块句柄", language: "en"), "Show Paragraph Block Handle")
        XCTAssertEqual(L10n.translate("显示段落块句柄", language: "ja"), "段落ブロックハンドルを表示")
        XCTAssertEqual(L10n.translate("保存", language: "zh-Hans"), "保存")
    }

    func testDetectSystemLanguageJapanese() {
        XCTAssertEqual(AppSettings.detectSystemLanguage(preferred: ["ja-JP"]), "ja")
        XCTAssertEqual(AppSettings.detectSystemLanguage(preferred: ["en-US"]), "en")
        XCTAssertEqual(AppSettings.detectSystemLanguage(preferred: ["zh-CN"]), "zh-Hans")
    }
}
