import XCTest
@testable import MarkLeaf

final class IncomingFileRoutingPolicyTests: XCTestCase {
    func testDuplicateAlwaysActivatesExistingWindow() {
        for mode in ExternalFileOpenMode.allCases {
            XCTAssertEqual(IncomingFileRoutingPolicy.action(
                mode: mode, eventIndex: 0, hasActiveEditor: true,
                hasOpenDuplicate: true
            ), .activateExisting)
        }
    }

    func testNewWindowModeCreatesAWindowAfterBootstrap() {
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .newWindow, eventIndex: 0, hasActiveEditor: true,
            hasOpenDuplicate: false
        ), .createWindow)
    }

    func testCurrentWindowModeReplacesOnlyFirstURL() {
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .currentWindow, eventIndex: 0, hasActiveEditor: true,
            hasOpenDuplicate: false
        ), .replaceActive)
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .currentWindow, eventIndex: 1, hasActiveEditor: true,
            hasOpenDuplicate: false
        ), .createWindow)
    }

    func testCurrentWindowModeCreatesWhenNoEditorExists() {
        XCTAssertEqual(IncomingFileRoutingPolicy.action(
            mode: .currentWindow, eventIndex: 0, hasActiveEditor: false,
            hasOpenDuplicate: false
        ), .createWindow)
    }

    func testRouterActivatesDuplicateReplacesFirstAndCreatesRemainingWindow() {
        let duplicate = URL(fileURLWithPath: "/workspace/already.md")
        let replacement = URL(fileURLWithPath: "/workspace/replace.md")
        let additional = URL(fileURLWithPath: "/workspace/additional.md")
        var actions: [String] = []

        IncomingFileRouter.route(
            urls: [duplicate, replacement, additional],
            mode: .currentWindow,
            activeEditor: true,
            openDocuments: [duplicate],
            activateExisting: { actions.append("activate:\($0.path)") },
            replaceActive: { actions.append("replace:\($0.path)") },
            createWindow: { actions.append("create:\($0.path)") }
        )

        XCTAssertEqual(actions, [
            "activate:/workspace/already.md",
            "create:/workspace/replace.md",
            "create:/workspace/additional.md",
        ])
    }

    func testCurrentWindowReplacesFirstThenCreatesRemaining() {
        let replacement = URL(fileURLWithPath: "/workspace/replace.md")
        let additional = URL(fileURLWithPath: "/workspace/additional.md")
        var actions: [String] = []

        IncomingFileRouter.route(
            urls: [replacement, additional],
            mode: .currentWindow,
            activeEditor: true,
            openDocuments: [],
            activateExisting: { actions.append("activate:\($0.path)") },
            replaceActive: { actions.append("replace:\($0.path)") },
            createWindow: { actions.append("create:\($0.path)") }
        )

        XCTAssertEqual(actions, [
            "replace:/workspace/replace.md",
            "create:/workspace/additional.md",
        ])
    }

    func testRouterCollapsesDuplicateURLsInsideOneFinderEvent() {
        let file = URL(fileURLWithPath: "/workspace/repeated.md")
        var actions: [String] = []

        IncomingFileRouter.route(
            urls: [file, file],
            mode: .newWindow,
            activeEditor: true,
            openDocuments: [],
            activateExisting: { actions.append("activate:\($0.path)") },
            replaceActive: { actions.append("replace:\($0.path)") },
            createWindow: { actions.append("create:\($0.path)") }
        )

        XCTAssertEqual(actions, ["create:/workspace/repeated.md"])
    }
}
