import AppKit
import Foundation

struct RecoverySnapshot {
    let documentId: String
    let documentPath: String?
    let markdown: String
    let revision: Int64
    let timestamp: Date
    let displayName: String?
}

struct ProbeSettings { var displayLanguage = "zh-Hans" }

final class SettingsService {
    static let shared = SettingsService()
    var settings = ProbeSettings()
}

enum L10n {
    static func t(_ text: String) -> String { text }
    static func translate(_ text: String, language: String) -> String { text }
    static func format(_ format: String, language: String, arguments: [CVarArg]) -> String {
        String(format: format, arguments: arguments)
    }
    static func f(_ format: String, _ args: CVarArg...) -> String {
        String(format: format, arguments: args)
    }
}

final class RecoveryService {
    static let shared = RecoveryService()
    private(set) var deletedDocumentIDs: [String] = []

    func delete(documentId: String) {
        deletedDocumentIDs.append(documentId)
    }

    static func discardAll() {}
}

final class AppWindowManager {
    static let shared = AppWindowManager()
    func openExternalDocuments(_ urls: [URL]) {}
}

enum AppLog {
    static func info(_ message: String) {}
    static func error(_ message: String) {}
}

enum RecoverySaveFailure {
    case fileMissing, unreachableVolume, readOnly, diskFull, other
    static func classify(error: Error) -> RecoverySaveFailure { .other }
}

@main
@MainActor
enum RecoveryWindowActionsProbe {
    static func main() {
        _ = NSApplication.shared
        NSApp.appearance = NSAppearance(named: .darkAqua)
        let snapshots = [
            RecoverySnapshot(
                documentId: "first",
                documentPath: nil,
                markdown: "first",
                revision: 1,
                timestamp: Date(timeIntervalSince1970: 1),
                displayName: "First"
            ),
            RecoverySnapshot(
                documentId: "second",
                documentPath: nil,
                markdown: "second",
                revision: 2,
                timestamp: Date(timeIntervalSince1970: 2),
                displayName: "Second"
            ),
        ]
        let controller = RecoveryWindowController(snapshots: snapshots, language: "zh-Hans")
        controller.showWindow(nil)

        guard
            let discardAllButton = controller.discardAllButton,
            let discardSelectedButton = controller.discardSelectedButton
        else {
            preconditionFailure("Recovery discard buttons are missing")
        }
        let discardAllReference = NSButton(title: discardAllButton.title, target: nil, action: nil)
        discardAllReference.bezelStyle = .rounded
        precondition(type(of: discardAllButton) == NSButton.self)
        precondition(type(of: discardSelectedButton) == NSButton.self)
        precondition(discardAllButton.bezelStyle == discardAllReference.bezelStyle)
        precondition(discardAllButton.intrinsicContentSize == discardAllReference.intrinsicContentSize)
        let actualInsets = discardAllButton.alignmentRectInsets
        let referenceInsets = discardAllReference.alignmentRectInsets
        precondition(actualInsets.top == referenceInsets.top)
        precondition(actualInsets.left == referenceInsets.left)
        precondition(actualInsets.bottom == referenceInsets.bottom)
        precondition(actualInsets.right == referenceInsets.right)
        precondition(discardAllButton.bezelColor == nil)
        precondition(discardSelectedButton.bezelColor == nil)
        precondition(discardAllButton.hasDestructiveAction == false)
        precondition(discardSelectedButton.hasDestructiveAction == false)
        precondition(controller.saveAsButton?.isHidden == true)
        precondition(controller.discardSelectedButton?.isHidden == true)
        precondition(controller.discardSelectedButton?.isEnabled == false)

        controller.tableView.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
        controller.tableViewSelectionDidChange(
            Notification(name: NSTableView.selectionDidChangeNotification)
        )
        precondition(controller.saveAsButton?.isHidden == false)
        precondition(controller.discardSelectedButton?.isHidden == false)
        precondition(controller.discardSelectedButton?.isEnabled == true)

        controller.discardSelectedButton?.performClick(nil)
        precondition(controller.numberOfRows(in: controller.tableView) == 1)
        precondition(controller.tableView.selectedRow == 0)
        precondition(RecoveryService.shared.deletedDocumentIDs == ["first"])
        precondition(controller.window?.isVisible == true)

        controller.discardSelectedButton?.performClick(nil)
        precondition(controller.numberOfRows(in: controller.tableView) == 0)
        precondition(RecoveryService.shared.deletedDocumentIDs == ["first", "second"])
        precondition(controller.window?.isVisible == false)
    }

}
