import AppKit
import XCTest
@testable import MarkLeaf

final class SidebarViewTests: XCTestCase {
    @MainActor
    func testEnglishCopyIsLocalizedAtConstruction() {
        let sidebar = makeSidebar(language: "en")

        XCTAssertEqual(sidebar.tabControl.label(forSegment: 0), "Workspace")
        XCTAssertEqual(sidebar.tabControl.label(forSegment: 1), "Outline")
        XCTAssertEqual(sidebar.emptyStateLabel.stringValue, "No workspace open")
        XCTAssertEqual(sidebar.emptyStateOpenFolderButton.title, "Open Folder")
        XCTAssertFalse(sidebar.emptyStateLabel.stringValue.contains("暂未"))
    }

    @MainActor
    func testApplyLanguageRefreshesEverySidebarControl() {
        var language = "en"
        let sidebar = SidebarView(session: EditorSession(), localize: {
            L10n.translate($0, language: language)
        })

        language = "ja"
        sidebar.applyLanguage()

        XCTAssertEqual(sidebar.tabControl.label(forSegment: 0), "ワークスペース")
        XCTAssertEqual(sidebar.tabControl.label(forSegment: 1), "アウトライン")
        XCTAssertEqual(sidebar.headerOpenFolderButton.toolTip, "フォルダを開く")
        XCTAssertEqual(sidebar.headerOpenFolderButton.accessibilityLabel(), "フォルダを開く")
        XCTAssertEqual(sidebar.emptyStateLabel.stringValue, "ワークスペースはまだ開かれていません")
        XCTAssertEqual(sidebar.emptyStateOpenFolderButton.title, "フォルダを開く")
    }

    @MainActor
    func testFolderControlsShareOneAction() {
        let sidebar = makeSidebar(language: "en")

        XCTAssertTrue(sidebar.headerOpenFolderButton.target === sidebar)
        XCTAssertTrue(sidebar.emptyStateOpenFolderButton.target === sidebar)
        XCTAssertEqual(sidebar.headerOpenFolderButton.action, sidebar.emptyStateOpenFolderButton.action)
    }

    @MainActor
    func testEnglishHeaderFitsAtDefaultWidth() throws {
        let sidebar = makeSidebar(language: "en")
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 230, height: 600),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = sidebar
        sidebar.frame = NSRect(x: 0, y: 0, width: 230, height: 600)
        sidebar.layoutSubtreeIfNeeded()

        let header = try XCTUnwrap(sidebar.headerOpenFolderButton.superview as? NSStackView)
        XCTAssertEqual(sidebar.headerOpenFolderButton.title, "")
        XCTAssertNotNil(sidebar.headerOpenFolderButton.image)
        XCTAssertEqual(sidebar.headerOpenFolderButton.imagePosition, .imageOnly)
        XCTAssertEqual(sidebar.headerOpenFolderButton.frame.width, 32, accuracy: 0.5)
        XCTAssertGreaterThanOrEqual(
            sidebar.headerOpenFolderButton.frame.minX,
            sidebar.tabControl.frame.maxX + header.spacing - 0.5
        )
        XCTAssertGreaterThanOrEqual(
            sidebar.emptyStateOpenFolderButton.frame.width,
            sidebar.emptyStateOpenFolderButton.intrinsicContentSize.width - 0.5
        )
    }

    @MainActor
    func testSidebarHeaderUsesTwoRows() throws {
        let sidebar = makeSidebar(language: "en")
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 230, height: 600),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = sidebar
        sidebar.frame = NSRect(x: 0, y: 0, width: 230, height: 600)
        sidebar.layoutSubtreeIfNeeded()

        let navigationRow = try XCTUnwrap(sidebar.tabControl.superview as? NSStackView)
        let header = try XCTUnwrap(navigationRow.superview as? NSStackView)
        let searchRow = try XCTUnwrap(sidebar.searchFieldForTesting.superview as? NSStackView)

        XCTAssertEqual(header.orientation, .vertical)
        XCTAssertEqual(header.arrangedSubviews.count, 2)
        XCTAssertEqual(navigationRow.orientation, .horizontal)
        XCTAssertEqual(searchRow.orientation, .horizontal)
        XCTAssertEqual(sidebar.searchFieldForTesting.frame.width, 218, accuracy: 1)
        XCTAssertGreaterThanOrEqual(sidebar.tabControl.frame.maxX, sidebar.tabControl.frame.minX)
        XCTAssertEqual(sidebar.headerOpenFolderButton.frame.width, 32, accuracy: 0.5)
    }

    @MainActor
    func testSidebarHeaderKeepsTwoRowsAcrossLanguagesAndUpdatesOutlinePlaceholder() throws {
        for language in ["zh", "en", "ja"] {
            let sidebar = makeSidebar(language: language)
            let window = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 200, height: 600),
                styleMask: [.titled],
                backing: .buffered,
                defer: false
            )
            window.contentView = sidebar
            sidebar.frame = NSRect(x: 0, y: 0, width: 200, height: 600)
            sidebar.layoutSubtreeIfNeeded()

            let navigationRow = try XCTUnwrap(sidebar.tabControl.superview as? NSStackView)
            let header = try XCTUnwrap(navigationRow.superview as? NSStackView)
            XCTAssertEqual(header.orientation, .vertical)
            XCTAssertEqual(header.arrangedSubviews.count, 2)
            XCTAssertGreaterThanOrEqual(sidebar.searchFieldForTesting.frame.width, 188)
            XCTAssertFalse(sidebar.searchFieldForTesting.placeholderString?.isEmpty ?? true)

            if language == "en" {
                XCTAssertEqual(sidebar.searchFieldForTesting.placeholderString, "Search Workspace")
                sidebar.selectTab(1)
                XCTAssertEqual(sidebar.searchFieldForTesting.placeholderString, "Search Outline")
            }
        }
    }

    @MainActor
    func testEmptyStateFollowsTabAndWorkspaceAvailability() {
        let sidebar = makeSidebar(language: "en")

        XCTAssertFalse(sidebar.emptyStateView.isHidden)
        sidebar.selectTab(1)
        XCTAssertTrue(sidebar.emptyStateView.isHidden)
        sidebar.selectTab(0)
        sidebar.updateEmptyStateVisibility(hasWorkspace: true)
        XCTAssertTrue(sidebar.emptyStateView.isHidden)
        XCTAssertFalse(sidebar.headerOpenFolderButton.isHidden)
        sidebar.updateEmptyStateVisibility(hasWorkspace: false)
        XCTAssertFalse(sidebar.emptyStateView.isHidden)
    }

    @MainActor
    func testSelectingOutlinePersistsActiveSidebarTab() {
        let session = EditorSession()
        var persistedTabs: [String] = []
        let sidebar = SidebarView(
            session: session,
            persistSidebarTab: { persistedTabs.append($0) },
            localize: { $0 }
        )

        sidebar.selectTab(1)

        XCTAssertEqual(session.sidebarTabIndex, 1)
        XCTAssertEqual(persistedTabs, ["outline"])
    }

    @MainActor
    private func makeSidebar(language: String) -> SidebarView {
        SidebarView(session: EditorSession(), localize: {
            L10n.translate($0, language: language)
        })
    }
}
