import XCTest
@testable import MarkLeaf

final class StartupIntegrationStateTests: XCTestCase {
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
        session.newDocument()
        session.preserveStartupRecoveryNoticeForCurrentDocumentLoad("recovery notice")
        let documentID = session.currentDocumentIdentifier

        session.handleEditorMessage(["type": "documentLoaded", "documentId": documentID])
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
        session.newDocument()
        session.preserveStartupRecoveryNoticeForCurrentDocumentLoad("recovery notice")
        let documentID = session.currentDocumentIdentifier
        session.handleEditorMessage(["type": "documentLoaded", "documentId": documentID])

        session.handleEditorMessage([
            "type": "dirtyChanged",
            "documentId": documentID,
            "payload": ["dirty": true],
        ])

        XCTAssertEqual(session.statusText, L10n.t("已修改"))
    }
}
