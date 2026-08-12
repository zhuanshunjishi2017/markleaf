import Foundation

enum DocumentDispositionReason: CaseIterable {
    case closeWindow
    case replaceDocument
    case terminateApplication
}

enum DocumentDispositionResult: Equatable {
    case proceed
    case cancel
}

enum SavedDocumentChoice { case save, discard, cancel }
enum UntitledDocumentChoice { case saveAs, delete, cancel }
enum DocumentDispositionDecision: Equatable { case proceed, autoSave, promptSaved, promptUntitled }

enum DocumentDispositionPolicy {
    static func decision(
        isDirty: Bool,
        hasFileURL: Bool,
        reason: DocumentDispositionReason,
        settings: AppSettings
    ) -> DocumentDispositionDecision {
        guard isDirty else { return .proceed }
        guard hasFileURL else { return .promptUntitled }
        switch reason {
        case .replaceDocument:
            return settings.saveOnDocumentSwitch ? .autoSave : .promptSaved
        case .closeWindow, .terminateApplication:
            return settings.autoSaveEnabled ? .autoSave : .promptSaved
        }
    }
}

final class DocumentDispositionCoordinator {
    private var activeRequestID: UUID?
    var isInProgress: Bool { activeRequestID != nil }

    @discardableResult
    func request(
        isDirty: Bool,
        hasFileURL: Bool,
        reason: DocumentDispositionReason,
        settings: AppSettings,
        saveExisting: @escaping (@escaping (Bool) -> Void) -> Void,
        saveAs: @escaping (@escaping (Bool) -> Void) -> Void,
        presentSavedPrompt: @escaping (@escaping (SavedDocumentChoice) -> Void) -> Void,
        presentUntitledPrompt: @escaping (@escaping (UntitledDocumentChoice) -> Void) -> Void,
        completion: @escaping (DocumentDispositionResult) -> Void
    ) -> Bool {
        guard !isInProgress else { return false }
        let requestID = UUID()
        activeRequestID = requestID
        let finish: (DocumentDispositionResult) -> Void = { [weak self] result in
            guard self?.activeRequestID == requestID else { return }
            self?.activeRequestID = nil
            completion(result)
        }
        switch DocumentDispositionPolicy.decision(
            isDirty: isDirty,
            hasFileURL: hasFileURL,
            reason: reason,
            settings: settings
        ) {
        case .proceed:
            finish(.proceed)
        case .autoSave:
            saveExisting { finish($0 ? .proceed : .cancel) }
        case .promptSaved:
            presentSavedPrompt { choice in
                switch choice {
                case .save: saveExisting { finish($0 ? .proceed : .cancel) }
                case .discard: finish(.proceed)
                case .cancel: finish(.cancel)
                }
            }
        case .promptUntitled:
            presentUntitledPrompt { choice in
                switch choice {
                case .saveAs: saveAs { finish($0 ? .proceed : .cancel) }
                case .delete: finish(.proceed)
                case .cancel: finish(.cancel)
                }
            }
        }
        return true
    }
}
