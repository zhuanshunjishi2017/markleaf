import XCTest
@testable import MarkLeaf

final class DocumentDispositionTests: XCTestCase {
    func testPolicyCoversCleanSavedAndUntitledDocuments() {
        var settings = AppSettings()
        settings.autoSaveEnabled = false
        settings.saveOnDocumentSwitch = false

        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: false, hasFileURL: false, reason: .closeWindow, settings: settings
        ), .proceed)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings
        ), .promptSaved)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .terminateApplication, settings: settings
        ), .promptSaved)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .replaceDocument, settings: settings
        ), .promptSaved)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: false, reason: .replaceDocument, settings: settings
        ), .promptUntitled)
    }

    func testPolicyUsesTheReasonSpecificAutoSaveSetting() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        settings.saveOnDocumentSwitch = false
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings
        ), .autoSave)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .terminateApplication, settings: settings
        ), .autoSave)
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .replaceDocument, settings: settings
        ), .promptSaved)

        settings.autoSaveEnabled = false
        settings.saveOnDocumentSwitch = true
        XCTAssertEqual(DocumentDispositionPolicy.decision(
            isDirty: true, hasFileURL: true, reason: .replaceDocument, settings: settings
        ), .autoSave)
    }

    func testUntitledNeverAutoSaves() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        settings.saveOnDocumentSwitch = true
        for reason in DocumentDispositionReason.allCases {
            XCTAssertEqual(DocumentDispositionPolicy.decision(
                isDirty: true, hasFileURL: false, reason: reason, settings: settings
            ), .promptUntitled)
        }
    }

    func testSavedPromptSaveWaitsForSuccessfulWriteBeforeProceeding() {
        var settings = AppSettings()
        settings.autoSaveEnabled = false
        let coordinator = DocumentDispositionCoordinator()
        var savedChoice: ((SavedDocumentChoice) -> Void)?
        var saveCompletion: ((Bool) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true,
            hasFileURL: true,
            reason: .closeWindow,
            settings: settings,
            saveExisting: { saveCompletion = $0 },
            saveAs: { XCTFail("saved document must not use Save As"); $0(false) },
            presentSavedPrompt: { savedChoice = $0 },
            presentUntitledPrompt: { _ in XCTFail("saved document must not show untitled prompt") },
            completion: { results.append($0) }
        ))

        savedChoice?(.save)
        XCTAssertTrue(results.isEmpty)
        saveCompletion?(true)
        XCTAssertEqual(results, [.proceed])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testSavedDiscardProceeds() {
        var settings = AppSettings()
        settings.autoSaveEnabled = false
        let coordinator = DocumentDispositionCoordinator()
        var savedChoice: ((SavedDocumentChoice) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings,
            saveExisting: { XCTFail("discard must not save"); $0(false) },
            saveAs: { XCTFail("discard must not use Save As"); $0(false) },
            presentSavedPrompt: { savedChoice = $0 },
            presentUntitledPrompt: { _ in XCTFail("saved document must not show untitled prompt") },
            completion: { results.append($0) }
        ))
        savedChoice?(.discard)
        XCTAssertEqual(results, [.proceed])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testSavedCancelCancels() {
        var settings = AppSettings()
        settings.autoSaveEnabled = false
        let coordinator = DocumentDispositionCoordinator()
        var savedChoice: ((SavedDocumentChoice) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings,
            saveExisting: { XCTFail("cancel must not save"); $0(false) },
            saveAs: { XCTFail("cancel must not use Save As"); $0(false) },
            presentSavedPrompt: { savedChoice = $0 },
            presentUntitledPrompt: { _ in XCTFail("saved document must not show untitled prompt") },
            completion: { results.append($0) }
        ))
        savedChoice?(.cancel)
        XCTAssertEqual(results, [.cancel])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testAutoSaveSuccessProceeds() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        let coordinator = DocumentDispositionCoordinator()
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings,
            saveExisting: { $0(true) },
            saveAs: { XCTFail("auto-save must not use Save As"); $0(false) },
            presentSavedPrompt: { _ in XCTFail("auto-save must not prompt") },
            presentUntitledPrompt: { _ in XCTFail("auto-save must not show untitled prompt") },
            completion: { results.append($0) }
        ))
        XCTAssertEqual(results, [.proceed])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testAutoSaveFailureCancels() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        let coordinator = DocumentDispositionCoordinator()
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings,
            saveExisting: { $0(false) },
            saveAs: { XCTFail("auto-save must not use Save As"); $0(false) },
            presentSavedPrompt: { _ in XCTFail("auto-save must not prompt") },
            presentUntitledPrompt: { _ in XCTFail("auto-save must not show untitled prompt") },
            completion: { results.append($0) }
        ))
        XCTAssertEqual(results, [.cancel])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testUntitledSaveAsProceedsAfterSuccessfulSave() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        let coordinator = DocumentDispositionCoordinator()
        var untitledChoice: ((UntitledDocumentChoice) -> Void)?
        var saveAsCompletion: ((Bool) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: false, reason: .closeWindow, settings: settings,
            saveExisting: { XCTFail("untitled must not save existing"); $0(false) },
            saveAs: { saveAsCompletion = $0 },
            presentSavedPrompt: { _ in XCTFail("untitled must not show saved prompt") },
            presentUntitledPrompt: { untitledChoice = $0 },
            completion: { results.append($0) }
        ))
        untitledChoice?(.saveAs)
        XCTAssertTrue(results.isEmpty)
        saveAsCompletion?(true)
        XCTAssertEqual(results, [.proceed])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testUntitledSaveAsFailureCancels() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        let coordinator = DocumentDispositionCoordinator()
        var untitledChoice: ((UntitledDocumentChoice) -> Void)?
        var saveAsCompletion: ((Bool) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: false, reason: .closeWindow, settings: settings,
            saveExisting: { XCTFail("untitled must not save existing"); $0(false) },
            saveAs: { saveAsCompletion = $0 },
            presentSavedPrompt: { _ in XCTFail("untitled must not show saved prompt") },
            presentUntitledPrompt: { untitledChoice = $0 },
            completion: { results.append($0) }
        ))
        untitledChoice?(.saveAs)
        saveAsCompletion?(false)
        XCTAssertEqual(results, [.cancel])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testUntitledDeleteProceeds() {
        var settings = AppSettings()
        settings.autoSaveEnabled = true
        let coordinator = DocumentDispositionCoordinator()
        var untitledChoice: ((UntitledDocumentChoice) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: false, reason: .closeWindow, settings: settings,
            saveExisting: { XCTFail("delete must not save existing"); $0(false) },
            saveAs: { XCTFail("delete must not use Save As"); $0(false) },
            presentSavedPrompt: { _ in XCTFail("untitled must not show saved prompt") },
            presentUntitledPrompt: { untitledChoice = $0 },
            completion: { results.append($0) }
        ))
        untitledChoice?(.delete)
        XCTAssertEqual(results, [.proceed])
        XCTAssertFalse(coordinator.isInProgress)
    }

    func testReentrantRequestIsRejectedUntilTheFirstPromptFinishes() {
        var settings = AppSettings()
        settings.autoSaveEnabled = false
        let coordinator = DocumentDispositionCoordinator()
        var firstPrompt: ((SavedDocumentChoice) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings,
            saveExisting: { $0(true) }, saveAs: { $0(true) },
            presentSavedPrompt: { firstPrompt = $0 },
            presentUntitledPrompt: { _ in },
            completion: { results.append($0) }
        ))
        XCTAssertFalse(coordinator.request(
            isDirty: false, hasFileURL: false, reason: .closeWindow, settings: settings,
            saveExisting: { $0(true) }, saveAs: { $0(true) },
            presentSavedPrompt: { _ in }, presentUntitledPrompt: { _ in },
            completion: { results.append($0) }
        ))

        firstPrompt?(.cancel)
        XCTAssertEqual(results, [.cancel])
    }

    func testStaleCallbackCannotFinishLaterRequest() {
        var settings = AppSettings()
        settings.autoSaveEnabled = false
        let coordinator = DocumentDispositionCoordinator()
        var firstSaveCompletion: ((Bool) -> Void)?
        var firstPrompt: ((SavedDocumentChoice) -> Void)?
        var secondUntitledChoice: ((UntitledDocumentChoice) -> Void)?
        var secondSaveAsCompletion: ((Bool) -> Void)?
        var results: [DocumentDispositionResult] = []

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: true, reason: .closeWindow, settings: settings,
            saveExisting: { firstSaveCompletion = $0 },
            saveAs: { XCTFail("saved document must not use Save As"); $0(false) },
            presentSavedPrompt: { firstPrompt = $0 },
            presentUntitledPrompt: { _ in XCTFail("saved document must not show untitled prompt") },
            completion: { results.append($0) }
        ))
        firstPrompt?(.discard)
        XCTAssertEqual(results, [.proceed])
        XCTAssertFalse(coordinator.isInProgress)

        XCTAssertTrue(coordinator.request(
            isDirty: true, hasFileURL: false, reason: .closeWindow, settings: settings,
            saveExisting: { $0(true) },
            saveAs: { secondSaveAsCompletion = $0 },
            presentSavedPrompt: { _ in },
            presentUntitledPrompt: { secondUntitledChoice = $0 },
            completion: { results.append($0) }
        ))

        secondUntitledChoice?(.saveAs)
        XCTAssertEqual(results, [.proceed])
        XCTAssertTrue(coordinator.isInProgress)

        firstSaveCompletion?(true)
        XCTAssertEqual(results, [.proceed])
        XCTAssertTrue(coordinator.isInProgress)

        secondSaveAsCompletion?(true)
        XCTAssertEqual(results, [.proceed, .proceed])
        XCTAssertFalse(coordinator.isInProgress)
    }
}
