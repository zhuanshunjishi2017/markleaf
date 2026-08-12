import XCTest
@testable import MarkLeaf

final class StartupIntegrationStateTests: XCTestCase {
    func testBootstrapCompletionRequestsOneBlankInitialWindowThenBecomesNoOp() {
        var state = StartupBootstrapState()

        XCTAssertEqual(state.complete(), .createInitialWindow(documentPath: nil, additionalDocumentPaths: []))
        XCTAssertEqual(state.complete(), .noOp)
    }

    func testColdStartCachesEveryFinderFileWithoutOverwritingTheFirst() {
        var state = StartupBootstrapState()
        XCTAssertTrue(state.cacheIncomingDocumentsIfNeeded(["/a.md", "/b.md", "/c.md"]))
        XCTAssertEqual(state.complete(), .createInitialWindow(
            documentPath: "/a.md",
            additionalDocumentPaths: ["/b.md", "/c.md"]
        ))
    }

    func testColdStartCollapsesDuplicateFinderPathsInTheirFirstSeenOrder() {
        var state = StartupBootstrapState()
        XCTAssertTrue(state.cacheIncomingDocumentsIfNeeded(["/a.md", "/a.md", "/b.md"]))
        XCTAssertEqual(state.complete(), .createInitialWindow(
            documentPath: "/a.md",
            additionalDocumentPaths: ["/b.md"]
        ))
    }

    func testFinderFileAfterBootstrapIsNotCached() {
        var state = StartupBootstrapState()
        _ = state.complete()

        XCTAssertFalse(state.cacheIncomingDocumentsIfNeeded(["/finder/after-bootstrap.md"]))
    }

    func testColdStartFinderFileIsStoredAsPendingInitialSessionIntent() {
        let manager = AppWindowManager()
        let session = EditorSession()

        manager.routeIncomingDocument(
            URL(fileURLWithPath: "/finder/requested.md"),
            to: session
        )

        XCTAssertEqual(session.pendingInitialDocumentPath, "/finder/requested.md")
        XCTAssertNil(session.documentURL)
    }

    func testFinderFileOpensImmediatelyAfterStartupActionIsConsumed() {
        var state = StartupActionState()

        XCTAssertEqual(
            state.disposition(forIncomingFile: "/finder/requested.md"),
            .pendingInitialIntent("/finder/requested.md")
        )
        XCTAssertTrue(state.consume())
        XCTAssertEqual(
            state.disposition(forIncomingFile: "/finder/later.md"),
            .openImmediately("/finder/later.md")
        )
        XCTAssertFalse(state.consume())
    }

    func testRecoveryNoticeRemainsFinalStatusAfterInitialFrontendStatusEvents() {
        let session = EditorSession()
        session.statusText = "recovery notice"
        session.preserveStartupRecoveryNoticeForCurrentDocumentLoad("recovery notice")
        let documentID = session.currentDocumentIdentifier

        session.handleEditorMessage([
            "type": "outlineChanged",
            "documentId": documentID,
            "payload": ["headings": []],
        ])
        session.handleEditorMessage([
            "type": "editorStatusChanged",
            "documentId": documentID,
            "payload": [
                "blockType": "paragraph",
                "line": 1,
                "column": 1,
                "characterCount": 0,
            ],
        ])

        XCTAssertEqual(session.statusText, "recovery notice")
    }

    func testUserEditReleasesStartupRecoveryNotice() {
        let session = EditorSession()
        session.statusText = "recovery notice"
        session.preserveStartupRecoveryNoticeForCurrentDocumentLoad("recovery notice")
        let documentID = session.currentDocumentIdentifier

        session.handleEditorMessage([
            "type": "dirtyChanged",
            "documentId": documentID,
            "payload": ["dirty": true],
        ])

        XCTAssertEqual(session.statusText, L10n.t("已修改"))
    }
}
