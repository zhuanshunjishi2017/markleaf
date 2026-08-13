import XCTest
@testable import MarkLeaf

final class ChangelogMenuTests: XCTestCase {
    func testHelpMenuHasChangelogCommand() {
        let menu = NativeMenuBuilder().build()
        let helpMenu = menu.items.last?.submenu
        let commands = helpMenu?.items.compactMap { $0.representedObject as? String } ?? []
        XCTAssertTrue(commands.contains("openChangelog"))
    }

    func testChangelogCandidatesUseRequestedLanguageThenSimplifiedChineseFallback() {
        XCTAssertEqual(ChangelogResource.candidateFileNames(displayLanguage: "en"), [
            "changelog.en.md", "changelog.zh-Hans.md",
        ])
        XCTAssertEqual(ChangelogResource.candidateFileNames(displayLanguage: "zh-Hans"), [
            "changelog.zh-Hans.md",
        ])
        XCTAssertEqual(ChangelogResource.candidateFileNames(displayLanguage: "unsupported"), [
            "changelog.zh-Hans.md",
        ])
    }

    func testBundledLookupFallsBackAndReturnsNilWhenEveryCandidateIsMissing() throws {
        let (fallbackBundle, fallbackDir) = try makeTemporaryBundle(resources: [
            "Changelog/changelog.zh-Hans.md": "# fallback",
        ])
        defer { try? FileManager.default.removeItem(at: fallbackDir) }
        XCTAssertEqual(
            ChangelogResource.bundledURL(in: fallbackBundle, displayLanguage: "en")?.lastPathComponent,
            "changelog.zh-Hans.md"
        )

        let (emptyBundle, emptyDir) = try makeTemporaryBundle(resources: [:])
        defer { try? FileManager.default.removeItem(at: emptyDir) }
        XCTAssertNil(ChangelogResource.bundledURL(in: emptyBundle, displayLanguage: "ja"))
    }

    func testCachedTargetKeepsMarkdownExtensionAndSelectedLanguageName() {
        let source = URL(fileURLWithPath: "/bundle/Changelog/changelog.ja.md")
        let cache = URL(fileURLWithPath: "/tmp/MarkLeaf/Cache", isDirectory: true)
        XCTAssertEqual(
            ChangelogResource.cachedURL(for: source, cacheDirectory: cache).lastPathComponent,
            "changelog.ja.md"
        )
    }

    func testEveryLocalizedMarkdownContainsFullHistory() throws {
        let changelogDirectory = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Changelog")
        for language in ["zh-Hans", "zh-Hant", "en", "ja"] {
            let url = changelogDirectory.appendingPathComponent("changelog.\(language).md")
            let text = try String(contentsOf: url, encoding: .utf8)
            for version in ["1.1.7", "1.1.6", "1.1.5", "1.1.4", "1.1.3"] {
                XCTAssertTrue(text.contains(version), "\(language) is missing \(version)")
            }
        }
    }

    func testBuildCopiesTheWholeChangelogDirectory() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        let script = try String(contentsOf: root.appendingPathComponent("script/build_and_run.sh"))
        XCTAssertTrue(script.contains("cp -R \"$ROOT_DIR/Changelog/.\" \"$APP_CONTENTS/Resources/Changelog/\""))
        XCTAssertFalse(script.contains("changelog.txt"))
    }

    private func makeTemporaryBundle(resources: [String: String]) throws -> (Bundle, URL) {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        for (relativePath, contents) in resources {
            let url = dir.appendingPathComponent(relativePath)
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try Data(contents.utf8).write(to: url)
        }
        let plist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0"><dict></dict></plist>
        """
        try Data(plist.utf8).write(to: dir.appendingPathComponent("Info.plist"))
        guard let bundle = Bundle(path: dir.path) else {
            throw NSError(domain: "ChangelogMenuTests", code: 1, userInfo: [NSLocalizedDescriptionKey: "无法创建临时 Bundle"])
        }
        return (bundle, dir)
    }
}
