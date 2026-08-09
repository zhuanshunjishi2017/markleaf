import XCTest
@testable import MarkLeaf

final class StartupActionResolverTests: XCTestCase {
    func testExplicitFileTakesPrecedenceForEveryStartupAction() {
        for action in [
            AppSettings.StartupAction.newDocument,
            .openLastWorkspace,
            .openLastWorkspaceAndFiles
        ] {
            XCTAssertEqual(
                resolve(action, folder: "/w", file: "/w/a.md", explicit: "/requested.md", validFolders: ["/w"], validFiles: ["/w/a.md"]),
                .init(operation: .openExplicitFile("/requested.md"), notice: nil)
            )
        }
    }

    func testNewDocumentAlwaysCreatesBlankDocument() {
        XCTAssertEqual(resolve(.newDocument), .init(operation: .newDocument, notice: nil))
        XCTAssertEqual(resolve(.newDocument, folder: "/w", file: "/w/a.md", validFolders: ["/w"], validFiles: ["/w/a.md"]), .init(operation: .newDocument, notice: nil))
    }

    func testOpenLastWorkspaceHandlesValidInvalidAndNilRememberedPaths() {
        XCTAssertEqual(resolve(.openLastWorkspace, folder: "/w", validFolders: ["/w"]), .init(operation: .openWorkspace("/w"), notice: nil))
        XCTAssertEqual(resolve(.openLastWorkspace, folder: "/missing"), .init(operation: .newDocument, notice: .missingWorkspace))
        XCTAssertEqual(resolve(.openLastWorkspace), .init(operation: .newDocument, notice: .missingWorkspace))
    }

    func testOpenLastWorkspaceAndFilesHandlesEveryRememberedPathValidityCombination() {
        XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/w", file: "/w/a.md", validFolders: ["/w"], validFiles: ["/w/a.md"]), .init(operation: .openWorkspaceAndFile(workspace: "/w", file: "/w/a.md"), notice: nil))
        XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/w", file: "/missing", validFolders: ["/w"]), .init(operation: .openWorkspace("/w"), notice: .missingFile))
        XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/missing", file: "/a.md", validFiles: ["/a.md"]), .init(operation: .openFile("/a.md"), notice: .missingWorkspace))
        XCTAssertEqual(resolve(.openLastWorkspaceAndFiles, folder: "/missing", file: "/missing.md"), .init(operation: .newDocument, notice: .missingWorkspaceAndFile))
        XCTAssertEqual(resolve(.openLastWorkspaceAndFiles), .init(operation: .newDocument, notice: .missingWorkspaceAndFile))
    }

    private func resolve(
        _ action: AppSettings.StartupAction,
        folder: String? = nil,
        file: String? = nil,
        explicit: String? = nil,
        validFolders: Set<String> = [],
        validFiles: Set<String> = []
    ) -> StartupPlan {
        StartupActionResolver.resolve(
            action: action, lastFolder: folder, lastFile: file, explicitFile: explicit,
            isDirectory: validFolders.contains, isFile: validFiles.contains
        )
    }
}
