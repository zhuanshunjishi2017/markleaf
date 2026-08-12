import XCTest
@testable import MarkLeaf

final class ExternalFileOpenPreferenceTests: XCTestCase {
    func testPreferenceOrderAndSelectionMatchPersistedValues() {
        XCTAssertEqual(ExternalFileOpenPreferenceModel.selectedIndex(for: .newWindow), 0)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.selectedIndex(for: .currentWindow), 1)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.mode(at: 0), .newWindow)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.mode(at: 1), .currentWindow)
        XCTAssertEqual(ExternalFileOpenPreferenceModel.mode(at: -1), .newWindow)
    }

    func testPreferenceCopyIsLocalizedInAllDisplayLanguages() {
        let expected: [String: [String]] = [
            "zh-Hans": ["始终在新窗口中打开", "在当前窗口中打开"],
            "zh-Hant": ["永遠在新視窗中開啟", "在目前視窗中開啟"],
            "en": ["Always Open in New Window", "Open in Current Window"],
            "ja": ["常に新規ウィンドウで開く", "現在のウィンドウで開く"],
        ]
        for (language, titles) in expected {
            XCTAssertEqual(ExternalFileOpenPreferenceModel.titles(language: language), titles)
        }
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "zh-Hans"), "外部文件打开方式")
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "zh-Hant"), "外部檔案開啟方式")
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "en"), "When Opening External Files")
        XCTAssertEqual(L10n.translate("外部文件打开方式", language: "ja"), "外部ファイルを開く方法")
    }
}
